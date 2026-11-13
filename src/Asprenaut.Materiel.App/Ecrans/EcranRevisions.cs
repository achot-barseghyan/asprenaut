using System.ComponentModel;
using System.Drawing.Drawing2D;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Révisions en tableau (maquette « Révisions ») : chaque équipement avance de gauche à droite, jusqu'à son retour en service.
/// À prévoir → Réparation interne → À envoyer → Chez le prestataire → Fait. En haut, deux compteurs : au club / hors du club ;
/// au-dessus des colonnes 3 et 4, l'accolade « Réparation externe ». Le panneau « Ajouter une intervention » s'ouvre en haut
/// depuis une carte, ou en scannant l'équipement.
/// </summary>
public sealed class EcranRevisions : EcranBase
{
    private readonly ServiceRevisions _revisions;
    private readonly ServicePrestataires _prestataires;
    private readonly ServiceMoniteurs _moniteurs;
    private readonly ServiceReglages _reglages;
    private readonly INavigation _navigation;
    private readonly GardeGestion _garde;
    private readonly FiltreCategories _filtre = new(Theme.AccentClair) { Margin = Padding.Empty };
    private readonly ChampSaisie _scan = new() { TexteIndicatif = "Scannez un équipement", Icone = Icones.Qr, IndicatifSiFocus = true };
    private readonly PageRevisions _page = new();
    private readonly PanneauIntervention _panneau;
    private readonly CompteursLieu _compteurs = new();
    private readonly AccoladeExterne _accolade = new();
    private readonly ColonneRevision[] _colonnes;
    private readonly ToolTip _bulle = new();

    // Archivées pendant cette visite : « Annuler » les remet dans « Fait » tant qu'on reste sur la page.
    private readonly List<long> _archives = [];
    private IReadOnlyList<Moniteur> _inspecteurs = [];
    private TableauRevisions? _tableau;

    private readonly ServiceInventaire _inventaire;

    public EcranRevisions(ServiceRevisions revisions, ServicePrestataires prestataires, ServiceMoniteurs moniteurs, ServiceReglages reglages,
        INavigation navigation, GardeGestion garde, ServiceInventaire inventaire)
    {
        _inventaire = inventaire;
        _revisions = revisions;
        _prestataires = prestataires;
        _moniteurs = moniteurs;
        _reglages = reglages;
        _navigation = navigation;
        _garde = garde;
        Disposed += (_, _) => _bulle.Dispose();

        var registre = new BoutonArrondi("Registre PDF", StyleBouton.Secondaire, Icones.Document);
        registre.Click += (_, _) => Executer(async () =>
        {
            var registreControles = await _revisions.RegistreAsync();
            var r = _reglages.Actuels;
            await Fichiers.EnregistrerPdfAsync(this, "Enregistrer le registre des contrôles", _revisions.NomFichierRegistre(),
                chemin => Impression.RegistrePdf.Enregistrer(registreControles, r.NomClub, r.ResponsableMateriel, chemin));
        });

        // Filtre par type d'entretien et champ de scan (le pistolet ouvre l'intervention de l'équipement).
        _filtre.Changement += (_, _) => AfficherColonnes();
        _scan.Champ.Font = Theme.PoliceCode(11.25f);
        _scan.Champ.AccessibleName = "Code de l'équipement";
        _scan.Champ.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                Executer(() => ScannerAsync(_scan.Valeur));
            }
        };
        var barre = Ui.Colonnes((_filtre, SizeType.Percent, 100), (_scan, SizeType.Absolute, 300));
        _scan.Margin = new Padding(16, 0, 0, 0);

        _panneau = new PanneauIntervention(revisions) { Visible = false };
        _panneau.Annule += (_, _) => FermerPanneau();
        _panneau.Enregistre += (_, _) => Executer(async () =>
        {
            FermerPanneau();
            await RechargerAsync();
        });
        _panneau.FicheDemandee += (_, code) => _navigation.AllerA<EcranHistorique>(code);
        _panneau.Valider.AfficherPastille = false;
        _garde.Lier(_panneau.Valider, ActionProtegee.Revisions);

        _colonnes =
        [
            new ColonneRevision(1, ServiceRevisions.LibelleEtape(EtapeRevision.APrevoir), "Échéance proche, dépassée ou problème signalé. Choisissez : au club ou chez un prestataire.", Theme.Avertissement),
            new ColonneRevision(2, ServiceRevisions.LibelleEtape(EtapeRevision.Interne), "Fait au club : séance TIV, petite réparation par le responsable matériel.", Theme.Retour),
            new ColonneRevision(3, ServiceRevisions.LibelleEtape(EtapeRevision.AEnvoyer), "Part chez un prestataire, mais encore au club. À déposer ou expédier.", Theme.PrestataireClair),
            new ColonneRevision(4, ServiceRevisions.LibelleEtape(EtapeRevision.ChezPrestataire), "Hors du club. On attend son retour.", Theme.Prestataire),
            new ColonneRevision(5, ServiceRevisions.LibelleEtape(EtapeRevision.Fait), "Intervention enregistrée. Retour en service.", Theme.Retour),
        ];
        // Colonnes repliables (bouton œil) : pour mieux voir celles sur lesquelles on travaille ; choix retenu.
        var repliees = _reglages.Actuels.ColonnesRevisionsRepliees;
        foreach (var (colonne, etape) in _colonnes.Zip(Enum.GetValues<EtapeRevision>()))
        {
            colonne.Repliee = repliees.Contains(etape);
            BulleColonne(colonne);
            colonne.Basculee += (_, _) => BasculerColonne(colonne);
        }

        var note = new NoteInfo(TexteNote());
        _page.Placer(_panneau, _compteurs, _accolade, _colonnes, note);

        var defilement = new Panel { BackColor = Theme.Fond, Margin = Padding.Empty };
        defilement.HorizontalScroll.Maximum = 0;
        defilement.AutoScroll = false;
        defilement.VerticalScroll.Visible = false;
        defilement.AutoScroll = true;
        Natif.ScrollbarsDuTheme(defilement);
        defilement.Controls.Add(_page);
        void Ajuster()
        {
            // Largeur extérieure moins la barre (à la résolution de l'écran) dès que la page dépasse : ne dépend pas
            // du moment où Windows affiche la barre, qui recouvrait sinon le bord droit de la colonne « Fait ».
            var barreDefilement = _page.Height > defilement.Height ? SystemInformation.GetVerticalScrollBarWidthForDpi(defilement.DeviceDpi) : 0;
            var largeur = defilement.Width - barreDefilement - (int)Dessin.Echelle(this, 6);
            if (largeur > 0 && _page.Width != largeur)
            {
                _page.Width = largeur;
            }
        }

        defilement.Layout += (_, _) => Ajuster();
        defilement.ClientSizeChanged += (_, _) => Ajuster();
        _page.SizeChanged += (_, _) => Ajuster();

        // « Ajouter une intervention » : choisir l'équipement (recherche ou médaille), puis le panneau d'intervention.
        var ajouter = new BoutonArrondi("Ajouter une intervention", StyleBouton.Principal, Icones.Plus) { AfficherPastille = false };
        _garde.Lier(ajouter, ActionProtegee.Revisions);
        ajouter.Click += (_, _) => Executer(AjouterInterventionAsync);

        var consultation = new BandeauConsultation(_garde, "Consultez librement. Planifier, envoyer chez un prestataire ou enregistrer une intervention demande le code gestionnaire.");
        _garde.Lier(consultation, ActionProtegee.Revisions);

        Controls.Add(Ui.Colonne(
            (Ui.Entete(Ui.Pile(Ui.Titre(Titre), Ui.SousTitre("Chaque équipement avance de gauche à droite, jusqu'à son retour en service.")), ajouter, registre), SizeType.AutoSize, 0),
            (consultation, SizeType.AutoSize, 0),
            (barre, SizeType.Absolute, Theme.HauteurChamp + 18),
            (defilement, SizeType.Percent, 100)));
        barre.Margin = new Padding(0, 0, 0, 18);
    }

    /// <summary>Replie ou rouvre une colonne ; le choix est enregistré (au moins une colonne reste ouverte).</summary>
    private void BasculerColonne(ColonneRevision colonne)
    {
        var etapes = Enum.GetValues<EtapeRevision>();
        var repliees = _colonnes.Zip(etapes)
            .Where(t => t.First == colonne ? !t.First.Repliee : t.First.Repliee)
            .Select(t => t.Second)
            .ToList();
        var resultat = _reglages.ChangerColonnesRevisionsRepliees(repliees);
        if (!resultat.Reussi)
        {
            Dialogues.Information(FindForm(), resultat.Message);
            return;
        }

        colonne.Repliee = !colonne.Repliee;
        BulleColonne(colonne);
        _page.PerformLayout();
        colonne.Bascule.Focus();
    }

    private void BulleColonne(ColonneRevision colonne)
    {
        var texte = colonne.Repliee ? $"Afficher « {colonne.Titre} »" : $"Replier « {colonne.Titre} »";
        _bulle.SetToolTip(colonne.Bascule, texte);
        _bulle.SetToolTip(colonne, colonne.Repliee ? texte : null);
    }

    public override string Titre => "Révisions";

    public override EntreeMenu Menu => EntreeMenu.Revisions;

    protected override async Task ChargerAsync(object? parametre)
    {
        _inspecteurs = await _moniteurs.ListerInspecteursTivAsync();
        await RechargerAsync();
        if (parametre is string code && !IsDisposed)
        {
            await ScannerAsync(code);
        }

        _scan.Champ.Focus();
    }

    private async Task RechargerAsync()
    {
        var tableau = await _revisions.TableauAsync();
        if (IsDisposed)
        {
            return;
        }

        _tableau = tableau;
        var actifs = tableau.Cartes.Where(c => c.Etape != EtapeRevision.Fait).ToList();
        _filtre.Afficher(actifs.Count, tableau.Familles.Select(f => (f, actifs.Count(c => c.Famille == f))));
        _compteurs.Afficher(tableau);
        AfficherColonnes();
    }

    private void AfficherColonnes()
    {
        if (_tableau is not { } tableau)
        {
            return;
        }

        _page.SuspendLayout();
        foreach (var (colonne, etape) in _colonnes.Zip(Enum.GetValues<EtapeRevision>()))
        {
            var cartes = tableau.Cartes.Where(c => c.Etape == etape && _filtre.Accepte(c.Famille)).Select(Carte).ToList();
            colonne.Remplir(cartes, etape switch
            {
                EtapeRevision.Interne => EnteteSeance(tableau.Seance),
                EtapeRevision.Fait => EnteteFait(cartes.Select(c => c.Revision.SuiviId).OfType<long>().ToList()),
                _ => null,
            });
        }

        // Les cartes sont ajoutées aux colonnes, pas à la page : la page ne demande pas d'elle-même une mise en page.
        _page.ResumeLayout(false);
        _page.PerformLayout();
    }

    // ---------------------------------------------------------------- Cartes et actions

    private CarteEquipement Carte(CarteRevision c)
    {
        var carte = new CarteEquipement(c, CouleurEtat(c.Etat));
        BoutonArrondi Action(string texte, StyleBouton style, Func<Task> action)
        {
            var bouton = carte.AjouterAction(texte, style);
            bouton.AfficherPastille = false; // Le bandeau « Mode consultation » de la page suffit.
            _garde.Lier(bouton, ActionProtegee.Revisions);
            bouton.Click += (_, _) => Executer(action);
            return bouton;
        }

        switch (c.Etape)
        {
            case EtapeRevision.APrevoir:
                if (c.Prevue)
                {
                    // Intervention prévue à la main : on peut la retirer si elle n'est plus nécessaire.
                    var retirer = carte.AjouterCoin(Icones.Croix, $"Retirer l'intervention prévue sur {c.Code}");
                    _bulle.SetToolTip(retirer, "Retirer l'intervention prévue");
                    retirer.Click += (_, _) => Executer(() => RetirerPrevueAsync(c));
                }

                // Un problème signalé ou une intervention prévue sur un bloc ne se règle pas en séance TIV.
                if (c.Type == TypeControle.Tiv && !c.Signalement && !c.Prevue)
                {
                    Action("Séance TIV", StyleBouton.Principal, () => SeanceTivAsync(c));
                }
                else
                {
                    Action("Réparer au club", StyleBouton.Principal, () => ReparerAuClubAsync(c));
                }

                Action("Envoyer chez un prestataire", StyleBouton.PrestataireContour, () => EnvoyerChezPrestataireAsync(c));
                break;
            case EtapeRevision.Interne:
                Action("Enregistrer le résultat", StyleBouton.Principal, () => OuvrirAsync(c));
                if (!c.SeanceTiv)
                {
                    Action("Envoyer chez un prestataire", StyleBouton.PrestataireContour, () => EnvoyerChezPrestataireAsync(c));
                }

                break;
            case EtapeRevision.AEnvoyer:
                Action("Marquer comme parti", StyleBouton.Prestataire, () => MarquerPartiAsync(c));
                Action("Réparer au club", StyleBouton.Secondaire, () => ReparerAuClubAsync(c));
                break;
            case EtapeRevision.ChezPrestataire:
                Action("Retour du prestataire", StyleBouton.Prestataire, () => OuvrirAsync(c));
                break;
            case EtapeRevision.Fait when c.SuiviId is { } id:
                // Archiver cette carte seulement : libre (aucune donnée de matériel ne change).
                var archiver = carte.AjouterArchive();
                _bulle.SetToolTip(archiver, "Archiver (reste dans l'Historique)");
                archiver.Click += (_, _) => Executer(() => ArchiverAsync([id]));
                break;
        }

        return carte;
    }

    private static Color CouleurEtat(EtatCarte etat) => etat switch
    {
        EtatCarte.EnRetard or EtatCarte.HorsService => Theme.Erreur,
        EtatCarte.CeMois or EtatCarte.ASurveiller => Theme.Avertissement,
        EtatCarte.EnAtelier => Theme.PrestataireClair,
        EtatCarte.Conforme => Theme.Retour,
        _ => Theme.AccentClair,
    };

    private async Task<bool> ReussirAsync(Task<ResultatOperation> action)
    {
        var resultat = await action;
        if (!resultat.Reussi)
        {
            Dialogues.Erreur(FindForm(), resultat.Message);
        }

        await RechargerAsync();
        return resultat.Reussi;
    }

    private async Task RetirerPrevueAsync(CarteRevision c)
    {
        if (_garde.Exiger(FindForm(), ActionProtegee.Revisions, "Retirer l'intervention prévue")
            && Dialogues.Confirmer(FindForm(), $"Retirer l'intervention prévue sur {c.Code} ?\n\n« {c.Controle} »"))
        {
            await ReussirAsync(_revisions.RetirerInterventionPrevueAsync(c.EquipementId));
        }
    }

    private async Task SeanceTivAsync(CarteRevision c)
    {
        if (_garde.Exiger(FindForm(), ActionProtegee.Revisions, "Séance TIV"))
        {
            await ReussirAsync(_revisions.AjouterASeanceAsync(c.EquipementId));
        }
    }

    private async Task ReparerAuClubAsync(CarteRevision c)
    {
        if (_garde.Exiger(FindForm(), ActionProtegee.Revisions, "Réparer au club"))
        {
            await ReussirAsync(_revisions.ReparerAuClubAsync(c.EquipementId, c.Controle));
        }
    }

    /// <summary>« Envoyer chez un prestataire » : choix du prestataire (obligatoire) et note d'envoi (facultative).</summary>
    private async Task EnvoyerChezPrestataireAsync(CarteRevision c)
    {
        if (!_garde.Exiger(FindForm(), ActionProtegee.Revisions, "Envoyer chez un prestataire"))
        {
            return;
        }

        var actifs = await _prestataires.ListerActifsAsync();
        if (actifs.Count == 0)
        {
            Dialogues.Information(FindForm(), "Aucun prestataire enregistré.\n\nAjoutez le magasin ou l'organisme dans Paramètres › Prestataires.");
            return;
        }

        var liste = new ListeDeroulante { AccessibleName = "Prestataire" };
        foreach (var p in actifs)
        {
            liste.Items.Add(new Choix<long>(p.Id, p.Type == TypePrestataire.Organisme ? $"{p.Nom} · organisme" : p.Nom));
        }

        // Requalification : un organisme d'abord ; le reste : un magasin.
        var voulu = c.Type == TypeControle.Requalification ? TypePrestataire.Organisme : TypePrestataire.Magasin;
        liste.SelectedIndex = Math.Max(0, actifs.ToList().FindIndex(p => p.Type == voulu));
        var note = new ChampSaisie { TexteIndicatif = "Dépôt prévu le 15/11, envoi groupé…" };
        note.Champ.AccessibleName = "Note d'envoi";
        note.Champ.MaxLength = ServiceRevisions.LongueurNoteEnvoi;
        using var fenetre = new FenetreFormulaire($"Envoyer {c.Code} chez un prestataire", "Envoyer", () =>
            _revisions.EnvoyerChezPrestataireAsync(c.EquipementId, (liste.SelectedItem as Choix<long>)?.Valeur, note.Valeur, c.Controle));
        fenetre.EnTete($"{c.Libelle} · {c.Controle}");
        fenetre.Champ("Prestataire", liste);
        fenetre.Champ("Note d'envoi (facultatif)", note);
        fenetre.Ajouter(Ui.Libelle("Il reste au club jusqu'à son départ : « Marquer comme parti » le moment venu.", Theme.Police(9.75f), Theme.TexteSecondaire));
        fenetre.Shown += (_, _) => liste.Focus();
        if (fenetre.ShowDialog(FindForm()) == DialogResult.OK)
        {
            await RechargerAsync();
        }
    }

    /// <summary>« Marquer comme parti » : date de retour prévue (facultative) ; l'équipement est hors service jusqu'à son retour.</summary>
    private async Task MarquerPartiAsync(CarteRevision c)
    {
        if (!_garde.Exiger(FindForm(), ActionProtegee.Revisions, "Marquer comme parti"))
        {
            return;
        }

        var retour = new ChampDate { AccessibleName = "Retour prévu" };
        using var fenetre = new FenetreFormulaire($"{c.Code} part chez {c.Prestataire ?? "le prestataire"}", "Marquer comme parti", () =>
            _revisions.MarquerPartiAsync(c.EquipementId, retour.Date));
        fenetre.EnTete($"{c.Libelle} · parti aujourd'hui");
        fenetre.Champ("Retour prévu (facultatif)", retour);
        fenetre.Ajouter(Ui.Libelle("Il sera hors service jusqu'à son retour.", Theme.Police(9.75f), Theme.TexteSecondaire));
        if (fenetre.ShowDialog(FindForm()) == DialogResult.OK)
        {
            await RechargerAsync();
        }
    }

    /// <summary>Ouvre le panneau d'intervention pour une carte (action protégée : le code est demandé avant).</summary>
    private Task OuvrirAsync(CarteRevision c)
    {
        if (!_garde.Exiger(FindForm(), ActionProtegee.Revisions, "Enregistrer une intervention"))
        {
            return Task.CompletedTask;
        }

        _panneau.Preparer(c, _inspecteurs);
        _panneau.Visible = true;
        _page.PerformLayout();
        if (_page.Parent is ScrollableControl defilement)
        {
            defilement.AutoScrollPosition = Point.Empty; // Le panneau est en haut de la page.
        }

        _panneau.PrendreFocus();
        return Task.CompletedTask;
    }

    private void FermerPanneau()
    {
        _panneau.Visible = false;
        _page.PerformLayout();
        _scan.Valeur = "";
        _scan.Champ.Focus();
    }

    /// <summary>
    /// Bouton « Ajouter une intervention » : fenêtre de choix de l'équipement (recherche par code, nom, marque… ou médaille
    /// scannée ; clic, flèches + Entrée), puis le panneau d'intervention comme après un scan.
    /// </summary>
    private async Task AjouterInterventionAsync()
    {
        if (!_garde.Exiger(FindForm(), ActionProtegee.Revisions, "Ajouter une intervention"))
        {
            return;
        }

        var recherche = new ChampSaisie { Icone = Icones.Recherche, TexteIndicatif = "Code, nom, marque… ou scannez la médaille", IndicatifSiFocus = true };
        recherche.Champ.AccessibleName = "Rechercher l'équipement";
        var liste = new ListeChoix { AccessibleName = "Équipements de l'inventaire" };
        var prefixe = _reglages.Actuels.PrefixeEtiquette;

        async Task RemplirAsync()
        {
            var saisie = recherche.Valeur;
            var lignes = await _inventaire.ListerAsync(saisie.Trim().Length == 0 ? null : saisie.Trim(), null);
            if (liste.IsDisposed || saisie != recherche.Valeur)
            {
                return;
            }

            liste.Afficher(lignes.Where(l => l.Etat != EtatEquipement.Reforme).Select(l => new ElementChoix(
                l.Code, l.Code, true, l.Libelle,
                string.Join(" · ", new[] { l.Categorie, $"{l.Marque} {l.Modele}".Trim(), l.Statut == StatutInventaire.Disponible ? "" : Libelles.De(l.Statut) }
                    .Where(t => t.Length > 0)))).ToList(), "Aucun équipement ne correspond.");
        }

        using var attente = new System.Windows.Forms.Timer { Interval = 250 };
        attente.Tick += (_, _) =>
        {
            attente.Stop();
            Executer(RemplirAsync);
        };
        recherche.Champ.TextChanged += (_, _) =>
        {
            attente.Stop();
            attente.Start();
        };

        // À prévoir (il faudra intervenir : l'équipement entre dans la colonne) ou déjà faite (le panneau d'intervention).
        var mode = new Bascule("À prévoir", "Déjà faite") { Etirer = true, AccessibleName = "Intervention à prévoir ou déjà faite", Margin = new Padding(0, 0, 0, 14) };
        var aFaire = new ChampSaisie { TexteIndicatif = "Changer le joint du robinet, vérifier l'inflateur…" };
        aFaire.Champ.AccessibleName = "Ce qu'il faudra faire";
        aFaire.Champ.MaxLength = ServiceRevisions.LongueurMotif;
        var description = new ChampSaisie { TexteIndicatif = "Ce qu'on a vu, pièce à commander…" };
        description.Champ.AccessibleName = "Description de l'intervention prévue";
        description.Champ.MaxLength = ServiceRevisions.LongueurDescription;
        bool APrevoir() => mode.SelectedIndex == 0;

        // « Continuer » (ou Entrée, y compris celle du pistolet) : le code tapé ou scanné s'il est dans l'inventaire, sinon
        // l'équipement choisi dans la liste, sinon le seul résultat de la recherche. À prévoir : l'intervention est enregistrée ici.
        string? choisi = null;
        using var fenetre = new FenetreFormulaire("Ajouter une intervention", "Ajouter à « À prévoir »", async () =>
        {
            attente.Stop();
            var lecture = Core.Saisie.CodeScanne.Lire(recherche.Valeur, prefixe);
            var code = lecture.Code;
            if (code.Length > 0)
            {
                if (lecture.Type is not null)
                {
                    recherche.Valeur = code; // « (Detendeur) A24 Asprenaut… » → A24.
                    attente.Stop();
                }

                await RemplirAsync();
                if (!liste.Choisir(code) && liste.Choix is null && liste.Nombre == 1)
                {
                    liste.Deplacer(1);
                }
            }

            choisi = liste.Choix?.Cle;
            if (choisi is null || liste.Choix is not { } element)
            {
                return ResultatOperation.Echec("Choisissez l'équipement dans la liste, ou scannez sa médaille.");
            }

            if (!APrevoir())
            {
                return ResultatOperation.Ok("");
            }

            if (aFaire.Valeur.Trim().Length == 0)
            {
                aFaire.Champ.Focus();
            }

            var equipement = (await _inventaire.ListerAsync(element.Cle, null)).FirstOrDefault(l => l.Code == element.Cle);
            return equipement is null
                ? ResultatOperation.Echec("Équipement introuvable.")
                : await _revisions.PrevoirInterventionAsync(equipement.EquipementId, aFaire.Valeur, description.Valeur);
        });
        fenetre.EnTete("Choisissez l'équipement, puis ce qu'il faudra faire.");

        recherche.Champ.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Down or Keys.Up)
            {
                liste.Deplacer(e.KeyCode == Keys.Down ? 1 : -1);
                e.Handled = e.SuppressKeyPress = true;
            }
        };

        // Déjà faite : le clic sur l'équipement continue. À prévoir : il le choisit, on passe à « À faire ».
        liste.ChoixValide += (_, _) =>
        {
            if (APrevoir())
            {
                aFaire.Champ.Focus();
            }
            else
            {
                fenetre.Valider.PerformClick();
            }
        };

        fenetre.Ajouter(mode);
        recherche.Margin = new Padding(0, 0, 0, 12);
        fenetre.Ajouter(recherche, SizeType.Absolute, Theme.HauteurChamp + 12);
        liste.Margin = new Padding(0, 0, 0, 14);
        fenetre.Ajouter(liste, SizeType.Absolute, 250);
        var champsPrevue = fenetre.DeuxChamps("À faire", aFaire, "Description (facultatif)", description);
        mode.Changement += (_, _) =>
        {
            champsPrevue.Visible = APrevoir();
            fenetre.Valider.Text = APrevoir() ? "Ajouter à « À prévoir »" : "Continuer";
            fenetre.EnTete(APrevoir() ? "Choisissez l'équipement, puis ce qu'il faudra faire." : "Choisissez l'équipement révisé, réparé ou contrôlé.");
            recherche.Champ.Focus();
        };
        fenetre.Shown += (_, _) => recherche.Champ.Focus();
        await RemplirAsync();
        if (fenetre.ShowDialog(FindForm()) != DialogResult.OK || IsDisposed)
        {
            return;
        }

        if (APrevoir())
        {
            await RechargerAsync();
        }
        else if (choisi is { } cle)
        {
            await ScannerAsync(cle);
        }
    }

    /// <summary>Scan : la carte de l'équipement (hors « Fait ») s'il en a une, sinon une intervention « faire maintenant ».</summary>
    private async Task ScannerAsync(string saisie)
    {
        if (saisie.Trim().Length == 0)
        {
            return;
        }

        var preparation = await _revisions.PreparerControleAsync(saisie);
        if (IsDisposed)
        {
            return;
        }

        if (preparation is null)
        {
            Dialogues.Information(FindForm(), $"Code inconnu : {saisie.Trim()}.");
            _scan.Champ.SelectAll();
            return;
        }

        var e = preparation.Equipement;
        var carte = _tableau?.Cartes.FirstOrDefault(c => c.EquipementId == e.Id && c.Etape != EtapeRevision.Fait)
            ?? new CarteRevision(EtapeRevision.APrevoir, e.Id, e.Code, e.Libelle, preparation.Categorie, preparation.Categorie,
                preparation.TypePropose, ServiceRevisions.LibelleControle(preparation.TypePropose, preparation.Categorie),
                "", EtatCarte.APlanifier, "", false, null);
        _scan.Valeur = e.Code;
        await OuvrirAsync(carte);
    }

    // ---------------------------------------------------------------- Archivage de « Fait »

    /// <summary>En tête de « Fait » : « Archiver tout » s'il y a des cartes, sinon le rappel des cartes archivées (et Annuler).</summary>
    private Control? EnteteFait(IReadOnlyList<long> ids)
    {
        if (ids.Count > 0)
        {
            var tout = new BoutonArrondi("Archiver tout", StyleBouton.Secondaire, Icones.Archive) { AutoSize = false, Font = Theme.PoliceTitre(9f) };
            _bulle.SetToolTip(tout, "Retire les cartes de la colonne. Elles restent dans l'Historique et la fiche EPI.");
            tout.Click += (_, _) => Executer(() => ArchiverAsync(ids));
            return tout;
        }

        if (_archives.Count == 0)
        {
            return null;
        }

        var etat = new EtatArchivage(_archives.Count);
        etat.Historique.Click += (_, _) => _navigation.AllerA<EcranHistorique>();
        etat.Annuler.Click += (_, _) => Executer(AnnulerArchivageAsync);
        return etat;
    }

    private async Task ArchiverAsync(IReadOnlyList<long> ids)
    {
        if ((await _revisions.ArchiverAsync(ids)).Reussi)
        {
            _archives.AddRange(ids.Except(_archives));
        }

        await RechargerAsync();
    }

    private async Task AnnulerArchivageAsync()
    {
        await _revisions.DesarchiverAsync([.. _archives]);
        _archives.Clear();
        await RechargerAsync();
    }

    // ---------------------------------------------------------------- Séance TIV

    /// <summary>En tête de « Réparation interne » : la prochaine séance, ou le bouton pour en planifier une.</summary>
    private Control EnteteSeance(ProchaineSeance? prochaine)
    {
        if (prochaine is null)
        {
            var planifier = new BoutonArrondi("Planifier une séance TIV", StyleBouton.Secondaire, Icones.Calendrier) { AutoSize = false, AfficherPastille = false, Font = Theme.PoliceTitre(9f) };
            _garde.Lier(planifier, ActionProtegee.Revisions);
            planifier.Click += (_, _) => Executer(PlanifierAsync);
            return planifier;
        }

        var s = prochaine.Seance;
        var titre = $"Séance TIV · {s.DateSeance.ToString("dddd", Libelles.Culture)}{(string.IsNullOrWhiteSpace(s.Heure) ? "" : $" {s.Heure}")}";
        var detail = string.Join(" · ", new[] { s.Lieu, prochaine.Inspecteurs.Count == 0 ? "aucun inspecteur" : string.Join(", ", prochaine.Inspecteurs) }
            .Where(t => !string.IsNullOrWhiteSpace(t)));
        return new CarteSeance(s.DateSeance, titre, detail);
    }

    private async Task PlanifierAsync()
    {
        if (!_garde.Exiger(FindForm(), ActionProtegee.Revisions, "Planifier une séance TIV"))
        {
            return;
        }

        var date = new ChampDate { Date = DateOnly.FromDateTime(DateTime.Today).AddDays(7) };
        var heure = new ChampSaisie { Valeur = "9:00", TexteIndicatif = "9:00" };
        var lieu = new ChampSaisie { Valeur = "Local du club" };
        heure.Champ.AccessibleName = "Heure";
        lieu.Champ.AccessibleName = "Lieu";
        using var fenetre = new FenetreFormulaire("Planifier une séance TIV", "Planifier", () =>
            date.Date is { } d
                ? _revisions.PlanifierSeanceAsync(d, heure.Valeur, lieu.Valeur)
                : Task.FromResult(ResultatOperation.Echec("Date invalide : écrivez-la comme 21/11/2026.")));
        fenetre.Champ("Date", date);
        fenetre.Champ("Heure", heure);
        fenetre.Champ("Lieu", lieu);
        if (fenetre.ShowDialog(FindForm()) == DialogResult.OK)
        {
            await RechargerAsync();
        }
    }

    private string TexteNote()
    {
        var r = _reglages.Actuels;
        var blocage = r.DepartSiRevisionDepassee
            ? "En retard, il part encore avec un avertissement"
            : "En retard, il est bloqué pour les sorties jusqu'à l'intervention";
        blocage += r.BloquerAuSeuil ? " ; au seuil d'utilisations, il est bloqué." : " ; au seuil d'utilisations, il part avec un avertissement.";
        return $"Un équipement arrive seul dans « À prévoir » {r.JoursAPlanifier} jours avant son échéance, à 90 % de son seuil d'utilisations, "
            + $"ou quand un problème est signalé. {blocage} Signalé « Hors service », il ne sort plus jusqu'à l'étape « Fait ». "
            + $"Échéances : TIV tous les {r.MoisEntreTiv} mois, requalification tous les {r.AnneesEntreRequalifications} ans, "
            + $"détendeurs tous les {ServiceRevisions.MoisRevision(r, "Détendeurs")} mois, stabs sans date obligatoire.";
    }

    // ================================================================ Mise en page

    /// <summary>
    /// Page qui défile : panneau d'intervention (s'il est ouvert), compteurs, accolade « Réparation externe », colonnes
    /// (5, ou 2 par rangée sur écran étroit : l'accolade reste au-dessus de « À envoyer » et « Chez le prestataire »), note.
    /// </summary>
    private sealed class PageRevisions : Panel
    {
        private PanneauIntervention _panneau = null!;
        private CompteursLieu _compteurs = null!;
        private AccoladeExterne _accolade = null!;
        private ColonneRevision[] _colonnes = [];
        private NoteInfo _note = null!;

        public PageRevisions()
        {
            BackColor = Theme.Fond;
            Margin = Padding.Empty;
            DoubleBuffered = true;
        }

        public void Placer(PanneauIntervention panneau, CompteursLieu compteurs, AccoladeExterne accolade, ColonneRevision[] colonnes, NoteInfo note)
        {
            (_panneau, _compteurs, _accolade, _colonnes, _note) = (panneau, compteurs, accolade, colonnes, note);
            Controls.Add(panneau);
            Controls.Add(compteurs);
            Controls.Add(accolade);
            Controls.AddRange(colonnes);
            Controls.Add(note);
        }

        private int E(float v) => (int)Dessin.Echelle(this, v);

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (_colonnes.Length == 0)
            {
                return;
            }

            // Changer la hauteur peut faire apparaître la barre de défilement, donc réduire la largeur
            // (voir Ajuster) ; la mise en page imbriquée est ignorée par WinForms : on recommence ici.
            for (var essai = 0; essai < 3; essai++)
            {
                var largeur = ClientSize.Width;
                var hauteur = Placer(largeur);
                if (Height != hauteur)
                {
                    Height = hauteur;
                }

                if (ClientSize.Width == largeur)
                {
                    break;
                }
            }
        }

        private int Placer(int largeur)
        {
            var ecart = E(12);
            var y = 0;
            if (_panneau.Visible)
            {
                var h = _panneau.HauteurPour(largeur);
                _panneau.Bounds = new Rectangle(0, 0, largeur, h);
                y = h + E(18);
            }

            _compteurs.Bounds = new Rectangle(0, y, largeur, E(CompteursLieu.Hauteur));
            y += _compteurs.Height + E(18);

            // Rangées : 5 colonnes ouvertes côte à côte (2 sous 1050 px) ; les colonnes repliées, étroites, ne comptent pas
            // dans cette limite et laissent leur place aux colonnes ouvertes de la rangée.
            var nb = largeur >= E(1050) ? 5 : 2;
            var lr = E(ColonneRevision.LargeurRepliee);
            var rangees = new List<List<ColonneRevision>>();
            var courante = new List<ColonneRevision>();
            var ouvertes = 0;
            foreach (var c in _colonnes)
            {
                if (!c.Repliee && ouvertes == nb)
                {
                    rangees.Add(courante);
                    (courante, ouvertes) = ([], 0);
                }

                courante.Add(c);
                ouvertes += c.Repliee ? 0 : 1;
            }

            rangees.Add(courante);
            _accolade.Visible = false;
            foreach (var rangee in rangees)
            {
                var nOuvertes = rangee.Count(c => !c.Repliee);
                var lc = nOuvertes == 0 ? 0 : (largeur - ((rangee.Count - 1) * ecart) - ((rangee.Count - nOuvertes) * lr)) / nOuvertes;
                var xs = new List<(ColonneRevision Colonne, int X, int L)>();
                var x = 0;
                foreach (var c in rangee)
                {
                    var l = c.Repliee ? lr : lc;
                    xs.Add((c, x, l));
                    x += l + ecart;
                }

                // L'accolade « Réparation externe » au-dessus des colonnes 3 et 4, quand elles sont côte à côte et assez larges.
                if (xs.FindIndex(t => t.Colonne == _colonnes[2]) is var i3 and >= 0 && xs.FindIndex(t => t.Colonne == _colonnes[3]) is var i4 and >= 0)
                {
                    var gauche = xs[i3].X;
                    var droite = xs[i4].X + xs[i4].L;
                    if (droite - gauche >= E(220))
                    {
                        _accolade.Bounds = new Rectangle(gauche, y, droite - gauche, E(AccoladeExterne.Hauteur));
                        _accolade.Visible = true;
                        y += _accolade.Height + E(6);
                    }
                }

                var h = Math.Max(E(360), xs.Max(t => t.Colonne.Mesurer(t.L)));
                foreach (var (colonne, xc, l) in xs)
                {
                    colonne.Bounds = new Rectangle(xc, y, l, h);
                }

                y += h + ecart;
            }

            var hNote = E(nb == 5 ? 70 : 96);
            _note.Bounds = new Rectangle(0, y + E(4), largeur, hNote);
            return y + hNote + E(12);
        }
    }

    /// <summary>
    /// Deux compteurs côte à côte (maquette) : vert « 8 au club, en attente » (maison) et violet « 3 hors du club, chez un
    /// prestataire » (camion), avec leur détail. Dessinés : la couleur n'est pas seule, le pictogramme et le texte le disent.
    /// </summary>
    private sealed class CompteursLieu : ControleDessine
    {
        public const int Hauteur = 76;

        private (string Titre, string Detail) _club = ("", "");
        private (string Titre, string Detail) _dehors = ("", "");

        public CompteursLieu() => AccessibleRole = AccessibleRole.StaticText;

        public void Afficher(TableauRevisions t)
        {
            // En sortie ou manquant : à récupérer, ni au club, ni chez un prestataire (dans le titre : la phrase du dessous est déjà longue).
            _club = ($"{t.AuClub} au club, en attente{(t.EnSortie == 0 ? "" : $" · {t.EnSortie} en sortie")}",
                "Rangés au local. " + (t.AEnvoyer == 0 ? "Rien à envoyer." : $"Dont {t.AEnvoyer} à envoyer chez un prestataire."));
            var parPrestataire = string.Join(", ", t.ParPrestataire.Select(p => $"{p.Nombre} chez {p.Prestataire}"));
            _dehors = ($"{t.HorsClub} hors du club, chez un prestataire",
                t.HorsClub == 0 ? "Aucun matériel chez un prestataire."
                : parPrestataire + (t.RetoursEnRetard == 0 ? "" : $" · {Libelles.Pluriel(t.RetoursEnRetard, "retour en retard", "retours en retard")}"));
            AccessibleName = $"{_club.Titre}. {_club.Detail} {_dehors.Titre}. {_dehors.Detail}";
            Invalidate();
        }

        protected override void Peindre(Graphics g)
        {
            var ecart = E(14);
            var l = (Width - ecart) / 2;
            Tuile(g, new RectangleF(0.5f, 0.5f, l - 1, Height - 1.5f), Theme.Retour, Theme.Retour, false, Icones.Maison, _club);
            Tuile(g, new RectangleF(l + ecart + 0.5f, 0.5f, Width - l - ecart - 1.5f, Height - 1.5f), Theme.Prestataire, Theme.PrestataireClair, true, Icones.Camion, _dehors);
        }

        private void Tuile(Graphics g, RectangleF r, Color couleur, Color texte, bool plein, string icone, (string Titre, string Detail) contenu)
        {
            var parent = Dessin.CouleurParent(this);
            var fond = Theme.Sur(parent, couleur, 0.09f);
            using (var chemin = Dessin.Arrondi(r, E(18)))
            using (var pinceau = new SolidBrush(fond))
            using (var bord = new Pen(Theme.Sur(parent, couleur, 0.42f), E(1)))
            {
                g.FillPath(pinceau, chemin);
                g.DrawPath(bord, chemin);
            }

            var c = E(44);
            var carre = new RectangleF(r.Left + E(18), r.Top + ((r.Height - c) / 2), c, c);
            using (var chemin = Dessin.Arrondi(carre, E(14)))
            using (var pinceau = new SolidBrush(plein ? couleur : Theme.Sur(fond, couleur, 0.18f)))
            {
                g.FillPath(pinceau, chemin);
            }

            var t = E(22);
            Icones.Dessiner(g, icone, new RectangleF(carre.Left + ((c - t) / 2), carre.Top + ((c - t) / 2), t, t), plein ? Theme.TexteSurPrestataire : couleur, 2f);
            var x = (int)(carre.Right + E(14));
            var largeur = (int)(r.Right - x - E(12));
            var milieu = (int)(r.Top + (r.Height / 2));
            Dessin.Texte(g, contenu.Titre, Theme.PoliceGrasse(11.25f), new Rectangle(x, milieu - (int)E(23), largeur, (int)E(24)), texte);
            Dessin.Texte(g, contenu.Detail, Theme.Police(9.75f), new Rectangle(x, milieu + (int)E(1), largeur, (int)E(20)), Theme.TexteSecondaireClair);
        }
    }

    /// <summary>Accolade violette « RÉPARATION EXTERNE » au-dessus des colonnes « À envoyer » et « Chez le prestataire ».</summary>
    private sealed class AccoladeExterne : ControleDessine
    {
        public const int Hauteur = 22;
        private const string Texte = "RÉPARATION EXTERNE";

        public AccoladeExterne()
        {
            AccessibleRole = AccessibleRole.StaticText;
            AccessibleName = "Réparation externe : colonnes À envoyer et Chez le prestataire";
        }

        protected override void Peindre(Graphics g)
        {
            var police = Theme.PoliceGrasse(8.25f);
            var lt = TextRenderer.MeasureText(g, Texte, police).Width + (int)E(16);
            var milieu = Height / 2f;
            var gauche = (Width - lt) / 2f;
            var bas = Height - E(1);
            using var stylo = new Pen(Theme.Sur(Dessin.CouleurParent(this), Theme.Prestataire, 0.6f), E(2));
            var rayon = E(10);
            using (var chemin = new GraphicsPath())
            {
                chemin.AddLine(E(1), bas, E(1), milieu + rayon);
                chemin.AddArc(E(1), milieu, rayon * 2, rayon * 2, 180, 90);
                chemin.AddLine(E(1) + rayon, milieu, gauche, milieu);
                g.DrawPath(stylo, chemin);
            }

            using (var chemin = new GraphicsPath())
            {
                var droite = Width - E(2);
                chemin.AddLine(gauche + lt, milieu, droite - rayon, milieu);
                chemin.AddArc(droite - (rayon * 2), milieu, rayon * 2, rayon * 2, 270, 90);
                chemin.AddLine(droite, milieu + rayon, droite, bas);
                g.DrawPath(stylo, chemin);
            }

            Dessin.Texte(g, Texte, police, new Rectangle((int)gauche, 0, lt, Height), Theme.PrestataireClair, centre: true);
        }
    }

    /// <summary>Contrôle d'en-tête dont la hauteur dépend de la largeur.</summary>
    private interface IHauteurVariable
    {
        int HauteurPour(int largeur);
    }

    /// <summary>
    /// Colonne « Fait » vidée : « 2 interventions archivées. Toujours visibles dans » + lien « Historique » + « Annuler »
    /// (tant qu'on reste sur la page).
    /// </summary>
    private sealed class EtatArchivage : Panel, IHauteurVariable
    {
        private readonly Label _texte;

        public EtatArchivage(int nombre)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Sur(Theme.Surface, Theme.Retour, 0.08f);
            _texte = Ui.Libelle($"{Libelles.Pluriel(nombre, "intervention archivée", "interventions archivées")}. Toujours visibles dans l'Historique et la fiche EPI.",
                Theme.Police(8.25f), Theme.TexteSecondaireClair);
            _texte.AutoSize = false;
            Historique = new BoutonArrondi("Historique", StyleBouton.Lien) { AutoSize = false, Font = Theme.PoliceTitre(8.25f), Margin = Padding.Empty };
            Annuler = new BoutonArrondi("Annuler", StyleBouton.Lien) { AutoSize = false, Font = Theme.PoliceTitre(8.25f), Margin = Padding.Empty, AccessibleName = "Annuler l'archivage" };
            Controls.AddRange([_texte, Historique, Annuler]);
            AccessibleRole = AccessibleRole.Grouping;
            AccessibleName = _texte.Text;
        }

        public BoutonArrondi Historique { get; }

        public BoutonArrondi Annuler { get; }

        private int E(float v) => (int)Dessin.Echelle(this, v);

        private int HauteurTexte(int largeur) =>
            TextRenderer.MeasureText(_texte.Text, _texte.Font, new Size(Math.Max(1, largeur - E(20)), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;

        public int HauteurPour(int largeur) => E(8) + HauteurTexte(largeur) + E(Theme.HauteurCliquable) + E(2);

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            var h = HauteurTexte(Width);
            _texte.Bounds = new Rectangle(E(10), E(8), Width - E(20), h);
            var lh = TextRenderer.MeasureText(Historique.Text, Historique.Font).Width + E(20);
            var la = TextRenderer.MeasureText(Annuler.Text, Annuler.Font).Width + E(20);
            Historique.Bounds = new Rectangle(E(2), E(8) + h, lh, E(Theme.HauteurCliquable));
            Annuler.Bounds = new Rectangle(Historique.Right, E(8) + h, la, E(Theme.HauteurCliquable));
        }

        protected override void OnPaintBackground(PaintEventArgs e) =>
            Dessin.PeindreFondArrondi(this, e.Graphics, BackColor, null, 10);
    }

    /// <summary>
    /// Colonne du tableau : bandeau coloré, numéro, titre, nombre de cartes, aide, en-tête optionnel (séance, archivage) et cartes.
    /// Bouton œil en haut à droite : la colonne se replie en bande étroite (numéro, nombre, titre vertical) ; un clic la rouvre.
    /// </summary>
    private sealed class ColonneRevision : Panel
    {
        /// <summary>Largeur d'une colonne repliée (pixels logiques).</summary>
        public const int LargeurRepliee = 56;

        private const int TailleBouton = 30;
        private readonly int _numero;
        private readonly string _titre;
        private readonly string _aide;
        private readonly Color _couleur;
        private Control? _entete;
        private readonly List<CarteEquipement> _cartes = [];
        private bool _repliee;

        public ColonneRevision(int numero, string titre, string aide, Color couleur)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            (_numero, _titre, _aide, _couleur) = (numero, titre, aide, couleur);
            BackColor = Theme.Surface;
            AccessibleRole = AccessibleRole.Grouping;
            AccessibleName = titre;
            Bascule = new BoutonArrondi("", StyleBouton.Icone, Icones.OeilBarre) { AutoSize = false, Margin = Padding.Empty, AccessibleName = $"Replier la colonne {titre}" };
            Bascule.Click += (_, _) => Basculee?.Invoke(this, EventArgs.Empty);
            Controls.Add(Bascule);
        }

        /// <summary>Bouton œil : replier / rouvrir la colonne.</summary>
        public BoutonArrondi Bascule { get; }

        public string Titre => _titre;

        /// <summary>L'utilisateur demande de replier ou de rouvrir la colonne (l'écran enregistre le choix).</summary>
        public event EventHandler? Basculee;

        /// <summary>Repliée : bande étroite, cartes cachées ; un clic n'importe où la rouvre.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Repliee
        {
            get => _repliee;
            set
            {
                _repliee = value;
                foreach (var c in _cartes)
                {
                    c.Visible = !value;
                }

                if (_entete is not null)
                {
                    _entete.Visible = !value;
                }

                Bascule.Icone = value ? Icones.Oeil : Icones.OeilBarre;
                Bascule.AccessibleName = value ? $"Afficher la colonne {_titre}" : $"Replier la colonne {_titre}";
                Cursor = value ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (_repliee)
            {
                Basculee?.Invoke(this, EventArgs.Empty);
            }
        }

        private float E(float v) => Dessin.Echelle(this, v);

        /// <summary>Bord droit de l'en-tête (nombre de cartes), à gauche du bouton œil.</summary>
        private float DroiteEntete(int largeur) => largeur - E(12) - E(TailleBouton) - E(6);

        private Font PoliceAide => Theme.Police(8.25f);

        private Font PoliceTitre => Theme.PoliceTitre(11.25f);

        private int Gauche => (int)(E(14) + E(26) + E(10));

        private int LargeurNombre(int n) => TextRenderer.MeasureText(n.ToString(Libelles.Culture), Theme.PoliceGrasse(9f)).Width + (int)E(14);

        /// <summary>Titre sur une ou deux lignes (« Chez le prestataire » dans une colonne étroite).</summary>
        private int HauteurTitre(int largeur) => Math.Max((int)E(30), TextRenderer.MeasureText(_titre, PoliceTitre,
            new Size(Math.Max(1, (int)DroiteEntete(largeur) - Gauche - LargeurNombre(_cartes.Count) - (int)E(8)), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height);

        private int HauteurAide(int largeur) =>
            TextRenderer.MeasureText(_aide, PoliceAide, new Size(Math.Max(1, largeur - (int)E(28)), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;

        public void Remplir(IReadOnlyList<CarteEquipement> cartes, Control? entete)
        {
            SuspendLayout();
            foreach (var c in _cartes)
            {
                Controls.Remove(c);
                c.Dispose();
            }

            if (_entete is not null)
            {
                Controls.Remove(_entete);
                _entete.Dispose();
            }

            _cartes.Clear();
            _cartes.AddRange(cartes);
            _entete = entete;
            if (entete is not null)
            {
                entete.Visible = !_repliee;
                Controls.Add(entete);
            }

            foreach (var c in cartes)
            {
                c.Visible = !_repliee;
            }

            Controls.AddRange([.. cartes]);
            AccessibleDescription = $"{Libelles.Pluriel(cartes.Count, "équipement")}";
            ResumeLayout(false);
            Invalidate();
        }

        /// <summary>Place l'en-tête et les cartes pour cette largeur ; retourne la hauteur nécessaire.</summary>
        public int Mesurer(int largeur)
        {
            var marge = (int)E(12);
            var tb = (int)E(TailleBouton);
            if (_repliee)
            {
                Bascule.Bounds = new Rectangle((largeur - tb) / 2, marge, tb, tb);
                return (int)E(360);
            }

            Bascule.Bounds = new Rectangle(largeur - marge - tb + (int)E(4), (int)E(12), tb, tb);
            var interieur = largeur - (2 * marge);
            var y = (int)E(14) + HauteurTitre(largeur) + (int)E(4) + HauteurAide(largeur) + (int)E(12);
            if (_entete is not null)
            {
                var h = _entete switch
                {
                    CarteSeance => (int)E(58),
                    IHauteurVariable variable => variable.HauteurPour(interieur),
                    _ => (int)E(Theme.HauteurCliquable),
                };
                var l = _entete is BoutonArrondi bouton
                    ? Math.Min(interieur, TextRenderer.MeasureText(bouton.Text, bouton.Font).Width + (int)E(52))
                    : interieur;
                _entete.Bounds = new Rectangle(marge, y, l, h);
                y += h + (int)E(10);
            }

            foreach (var c in _cartes)
            {
                var h = c.Mesurer(interieur);
                c.Bounds = new Rectangle(marge, y, interieur, h);
                y += h + (int)E(10);
            }

            if (_cartes.Count == 0)
            {
                y += (int)E(50); // « Rien ici ».
            }

            return y + marge;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Dessin.EffacerAvecParent(this, g);
            Dessin.Qualite(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            using (var chemin = Dessin.Arrondi(r, E(22)))
            using (var fond = new SolidBrush(Theme.Surface))
            using (var bord = new Pen(Theme.Bordure, E(1)))
            {
                g.FillPath(fond, chemin);
                g.DrawPath(bord, chemin);
                // Bandeau de couleur en haut, dans l'arrondi.
                var ancien = g.Clip;
                g.SetClip(chemin);
                using (var bandeau = new SolidBrush(_couleur))
                {
                    g.FillRectangle(bandeau, 0, 0, Width, E(3));
                }

                g.Clip = ancien;
            }

            if (_repliee)
            {
                PeindreRepliee(g);
                return;
            }

            var x = E(14);
            var y = E(14);
            var d = E(26);
            using (var pastille = new SolidBrush(_couleur))
            {
                g.FillEllipse(pastille, x, y + E(2), d, d);
            }

            Dessin.Texte(g, _numero.ToString(Libelles.Culture), Theme.PoliceGrasse(9.75f), Rectangle.Round(new RectangleF(x, y + E(2), d, d)), Theme.Fond, centre: true);
            var nombre = _cartes.Count.ToString(Libelles.Culture);
            var lNombre = LargeurNombre(_cartes.Count);
            var rNombre = new RectangleF(DroiteEntete(Width) - lNombre, y + E(4), lNombre, E(22));
            using (var chemin = Dessin.Arrondi(rNombre, rNombre.Height / 2))
            using (var fond = new SolidBrush(Theme.Teinte(Theme.AccentClair, 0.12f)))
            {
                g.FillPath(fond, chemin);
            }

            Dessin.Texte(g, nombre, Theme.PoliceGrasse(9f), Rectangle.Round(rNombre), Theme.TexteDoux, centre: true);
            var hTitre = HauteurTitre(Width);
            TextRenderer.DrawText(g, _titre, PoliceTitre, new Rectangle(Gauche, (int)y, (int)(rNombre.Left - Gauche - E(6)), hTitre), Theme.Texte,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
            var yAide = (int)(y + hTitre + E(4));
            TextRenderer.DrawText(g, _aide, PoliceAide, new Rectangle((int)E(14), yAide, Width - (int)E(28), HauteurAide(Width)), Theme.TexteSecondaire,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

            if (_cartes.Count == 0)
            {
                var yVide = _entete is null ? yAide + HauteurAide(Width) + (int)E(20) : _entete.Bottom + (int)E(18);
                Dessin.Texte(g, "Rien ici", Theme.Police(9.75f), new Rectangle(0, yVide, Width, (int)E(24)), Theme.TexteSecondaire, centre: true);
            }
        }

        /// <summary>Bande repliée : bouton œil (en haut), numéro, nombre de cartes, titre écrit de haut en bas.</summary>
        private void PeindreRepliee(Graphics g)
        {
            var cx = Width / 2f;
            // Sous le bouton œil réel (sa taille peut avoir été mise à l'échelle après coup) : sinon, en se repeignant,
            // le bouton recouvrait le haut du cercle.
            var y = Math.Max(E(12) + E(TailleBouton), Bascule.Bottom) + E(10);
            var d = E(26);
            using (var pastille = new SolidBrush(_couleur))
            {
                g.FillEllipse(pastille, cx - (d / 2), y, d, d);
            }

            Dessin.Texte(g, _numero.ToString(Libelles.Culture), Theme.PoliceGrasse(9.75f), Rectangle.Round(new RectangleF(cx - (d / 2), y, d, d)), Theme.Fond, centre: true);
            y += d + E(8);

            var lNombre = LargeurNombre(_cartes.Count);
            var rNombre = new RectangleF(cx - (lNombre / 2f), y, lNombre, E(22));
            using (var chemin = Dessin.Arrondi(rNombre, rNombre.Height / 2))
            using (var fond = new SolidBrush(Theme.Teinte(Theme.AccentClair, 0.12f)))
            {
                g.FillPath(fond, chemin);
            }

            Dessin.Texte(g, _cartes.Count.ToString(Libelles.Culture), Theme.PoliceGrasse(9f), Rectangle.Round(rNombre), Theme.TexteDoux, centre: true);
            y += rNombre.Height + E(14);

            // Titre vertical (GDI+ : TextRenderer ignore les rotations).
            var etat = g.Save();
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var police = PoliceTitre;
            var taille = g.MeasureString(_titre, police);
            g.TranslateTransform(cx + (taille.Height / 2), y);
            g.RotateTransform(90);
            using (var pinceau = new SolidBrush(Theme.Texte))
            {
                g.DrawString(_titre, police, pinceau, 0, 0);
            }

            g.Restore(etat);
        }
    }

    /// <summary>
    /// Carte d'un équipement : nom, code, pastille d'état ; « Signalé au retour », « Au club » ; encadré « Encore au club ·
    /// À envoyer chez … » (pointillés violets) ou « Chez … · parti le … · retour prévu » (fond violet, bordure gauche) ;
    /// contrôle, détail, barre d'utilisations, « Bloqué pour les sorties », boutons d'action (ou « Archiver » dans « Fait »).
    /// Le texte et les pictogrammes portent l'information, pas seulement la couleur.
    /// </summary>
    private sealed class CarteEquipement : Panel
    {
        private const TextFormatFlags Paragraphe = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;

        private readonly CarteRevision _c;
        private readonly Color _couleur;
        private readonly List<BoutonArrondi> _actions = [];
        private BoutonArrondi? _archive;

        public CarteEquipement(CarteRevision c, Color couleur)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            (_c, _couleur) = (c, couleur);
            BackColor = c.Etape == EtapeRevision.ChezPrestataire
                ? Theme.Sur(Theme.Sur(Theme.Surface, Theme.Fond, 0.55f), Theme.Prestataire, 0.10f)
                : Theme.Sur(Theme.Surface, Theme.Fond, 0.55f);
            AccessibleRole = AccessibleRole.Grouping;
            AccessibleName = string.Join(". ", new[]
            {
                $"{c.Libelle} {c.Code}, {c.Badge}",
                c.Signalement ? "Signalé au retour" : "",
                c.HorsClub.Length > 0 ? c.HorsClub : c.Etape is EtapeRevision.APrevoir or EtapeRevision.Interne ? "Au club" : "",
                c.Etape == EtapeRevision.AEnvoyer ? $"Encore au club, à envoyer chez {c.Prestataire}. {c.NoteEnvoi}" : "",
                c.Etape == EtapeRevision.ChezPrestataire ? $"Chez {c.Prestataire}. {LigneDepart} {LigneRetour}" : "",
                c.Controle, c.Detail, c.Precision, c.Description, c.Bloque ? "Bloqué pour les sorties" : "",
            }.Where(t => !string.IsNullOrWhiteSpace(t)));
        }

        public CarteRevision Revision => _c;

        private float E(float v) => Dessin.Echelle(this, v);

        private int Ei(float v) => (int)Dessin.Echelle(this, v);

        private static Font PoliceDetail => Theme.Police(8.25f);

        private static void Si(Graphics? g, Action<Graphics> dessin)
        {
            if (g is not null)
            {
                dessin(g);
            }
        }

        private static Font PoliceForte => Theme.PoliceGrasse(8.25f);

        public BoutonArrondi AjouterAction(string texte, StyleBouton style)
        {
            var bouton = new BoutonArrondi(texte, style) { AutoSize = false, Font = Theme.PoliceTitre(9f), Margin = Padding.Empty, AccessibleName = $"{texte} {_c.Code}" };
            _actions.Add(bouton);
            Controls.Add(bouton);
            return bouton;
        }

        /// <summary>Petit bouton « Archiver A07 » (boîte d'archive, 30 px) en haut à droite d'une carte « Fait ».</summary>
        public BoutonArrondi AjouterArchive() => AjouterCoin(Icones.Archive, $"Archiver {_c.Code}");

        /// <summary>Petit bouton à pictogramme (30 px) en haut à droite : archiver (« Fait »), retirer l'intervention prévue.</summary>
        public BoutonArrondi AjouterCoin(string icone, string nom)
        {
            _archive = new BoutonArrondi("", StyleBouton.Icone, icone) { AutoSize = false, Margin = Padding.Empty, AccessibleName = nom };
            Controls.Add(_archive);
            return _archive;
        }

        private string LigneDepart => _c.DateEnvoi is { } envoi
            ? $"Parti le {envoi.ToString("dd/MM", Libelles.Culture)} · {(_c.JoursDehors is null or 0 ? "aujourd'hui" : $"dehors depuis {Libelles.Pluriel(_c.JoursDehors.Value, "jour")}")}"
            : "";

        private string LigneRetour => _c.DateRetourPrevue is not { } prevu
            ? "Pas de date de retour"
            : _c.RetourEnRetard
                ? $"Retour en retard de {Libelles.Pluriel(DateOnly.FromDateTime(DateTime.Today).DayNumber - prevu.DayNumber, "jour")} (prévu {prevu.ToString("dd/MM", Libelles.Culture)})"
                : $"Retour prévu le {prevu.ToString("dd/MM", Libelles.Culture)}";

        private int LargeurBadge(Graphics? g) => _c.Badge.Length == 0 ? 0
            : (g is null ? TextRenderer.MeasureText(_c.Badge, Theme.PoliceGrasse(7.5f)).Width : TextRenderer.MeasureText(g, _c.Badge, Theme.PoliceGrasse(7.5f)).Width) + Ei(12);

        private static int Hauteur(string texte, Font police, int largeur) => texte.Length == 0 ? 0
            : TextRenderer.MeasureText(texte, police, new Size(Math.Max(1, largeur), int.MaxValue), Paragraphe).Height;

        /// <summary>Hauteur pour cette largeur, boutons placés (passés à la ligne s'ils ne tiennent pas).</summary>
        public int Mesurer(int largeur)
        {
            var p = Ei(12);
            var y = Parcourir(null, largeur);
            if (_archive is not null)
            {
                var t = Ei(30);
                _archive.Bounds = new Rectangle(largeur - p - t + Ei(4), p - Ei(4), t, t);
            }

            if (_actions.Count > 0)
            {
                // Côte à côte (largeur partagée) si tous les textes tiennent, sinon empilés sur toute la largeur.
                y += Ei(8);
                var h = Ei(Theme.HauteurCliquable);
                var ecart = Ei(6);
                var interieur = largeur - (2 * p);
                foreach (var b in _actions)
                {
                    // Colonne étroite (5 colonnes) : « Envoyer chez un prestataire » tient en un peu plus petit.
                    var police = TextRenderer.MeasureText(b.Text, Theme.PoliceTitre(9f)).Width + Ei(24) > interieur ? Theme.PoliceTitre(8.25f) : Theme.PoliceTitre(9f);
                    if (!Equals(b.Font, police))
                    {
                        b.Font = police;
                    }
                }

                var part = (interieur - ((_actions.Count - 1) * ecart)) / _actions.Count;
                var cote = _actions.All(b => TextRenderer.MeasureText(b.Text, b.Font).Width + Ei(20) <= part);
                for (var i = 0; i < _actions.Count; i++)
                {
                    _actions[i].Bounds = cote
                        ? new Rectangle(p + (i * (part + ecart)), y, part, h)
                        : new Rectangle(p, y + (i * (h + ecart)), interieur, h);
                }

                y += cote ? h : (_actions.Count * h) + ((_actions.Count - 1) * ecart);
            }

            return y + p;
        }

        /// <summary>
        /// Contenu de la carte, de haut en bas, jusqu'aux boutons : dessiné si <paramref name="g"/> est donné, sinon seulement
        /// mesuré (les deux passent par le même code : la hauteur suit toujours le dessin).
        /// </summary>
        private int Parcourir(Graphics? g, int largeurCarte)
        {
            var p = Ei(12);
            var largeur = largeurCarte - (2 * p);
            var y = p;

            // Pastille d'état à droite du nom (à gauche du bouton « Archiver » dans « Fait ») ; sous le code si la colonne
            // est trop étroite pour laisser la place au nom.
            var lBadge = LargeurBadge(g);
            var reserve = _archive is null ? 0 : Ei(32);
            var lNom = largeur - lBadge - reserve - (lBadge + reserve > 0 ? Ei(8) : 0);
            var badgeDessous = lBadge > 0 && lNom < Ei(120);
            if (badgeDessous)
            {
                lNom = largeur - reserve - (reserve > 0 ? Ei(8) : 0);
            }
            else if (g is not null && lBadge > 0)
            {
                Badge(g, largeurCarte - p - reserve - lBadge, p, lBadge);
            }

            // Nom (deux lignes au plus) et code.
            var police = Theme.PoliceTitre(10.5f);
            var hNom = Math.Min(Hauteur(_c.Libelle, police, lNom), Ei(42));
            Si(g, gr => TextRenderer.DrawText(gr, _c.Libelle, police, new Rectangle(p, y, lNom, hNom), Theme.Texte, Paragraphe | TextFormatFlags.EndEllipsis));
            y += Math.Max(hNom, Ei(22));
            Si(g, gr => Dessin.Texte(gr, _c.Code, Theme.PoliceCode(9f), new Rectangle(p, y, lNom, Ei(18)), Theme.Accent));
            y += Ei(20);
            if (badgeDessous)
            {
                y += Ei(4);
                Si(g, gr => Badge(gr, p, y, lBadge));
                y += Ei(22);
            }

            // Pastilles : signalé au retour, au club.
            if (_c.Signalement && _c.Etape != EtapeRevision.Fait)
            {
                y += Ei(4);
                Pastille(g, p, y, Icones.Alerte, "Signalé au retour", Theme.Avertissement);
                y += Ei(22);
            }

            if (_c.HorsClub.Length > 0 && _c.Etape != EtapeRevision.Fait)
            {
                // En sortie (avec son intitulé) ou manquant : pas au club, il faut d'abord le récupérer.
                y += Ei(4);
                Pastille(g, p, y, Icones.Depart, _c.HorsClub, Theme.Depart, largeur);
                y += Ei(22);
            }
            else if (_c.Etape is EtapeRevision.APrevoir or EtapeRevision.Interne)
            {
                y += Ei(4);
                Pastille(g, p, y, Icones.Maison, "Au club", Theme.Retour);
                y += Ei(22);
            }

            // Encadrés de la réparation externe.
            if (_c.Etape is EtapeRevision.AEnvoyer or EtapeRevision.ChezPrestataire)
            {
                y += Ei(6);
                y += Encadre(g, p, y, largeur);
            }

            // Contrôle, détail.
            y += Ei(6);
            // Deux lignes au plus : le motif d'une intervention prévue ou d'un signalement doit se lire en entier.
            var hControle = Math.Max(Ei(18), Math.Min(Hauteur(_c.Controle, PoliceDetail, largeur), Ei(34)));
            Si(g, gr => TextRenderer.DrawText(gr, _c.Controle, PoliceDetail, new Rectangle(p, y, largeur, hControle), Theme.TexteDoux, Paragraphe | TextFormatFlags.EndEllipsis));
            y += hControle;
            var hDetail = Hauteur(_c.Detail, PoliceDetail, largeur);
            if (hDetail > 0)
            {
                Si(g, gr => TextRenderer.DrawText(gr, _c.Detail, PoliceDetail, new Rectangle(p, y, largeur, hDetail), Theme.TexteSecondaire, Paragraphe));
                y += hDetail;
            }

            // Précision du problème signalé, description de l'intervention : en citation, avec un trait de couleur.
            if (_c.Precision.Length > 0)
            {
                y += Ei(6);
                y += Citation(g, p, y, largeur, $"« {_c.Precision} »", Theme.Avertissement);
            }

            if (_c.Description.Length > 0)
            {
                y += Ei(6);
                y += Citation(g, p, y, largeur, _c.Description, _couleur);
            }

            if (_c.Usage is { } u && _c.Etape != EtapeRevision.Fait)
            {
                if (g is not null)
                {
                    BarreUsage(g, u, p, y, largeur);
                }

                y += Ei(18);
            }

            if (_c.Bloque)
            {
                if (g is not null)
                {
                    var t = E(13);
                    Icones.Dessiner(g, "M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM5.6 5.6l12.8 12.8", new RectangleF(p, y + E(4), t, t), Theme.Erreur, 2.4f);
                    Dessin.Texte(g, "Bloqué pour les sorties", Theme.PoliceGrasse(7.5f), new Rectangle(p + (int)(t + E(6)), y + Ei(1), largeur, Ei(20)), Theme.Erreur);
                }

                y += Ei(20);
            }

            return y;
        }

        /// <summary>Texte cité (précision d'un signalement, description) : trait vertical de couleur à gauche. Retourne sa hauteur.</summary>
        private int Citation(Graphics? g, int x, int y, int largeur, string texte, Color couleur)
        {
            var retrait = Ei(10);
            var h = Hauteur(texte, PoliceDetail, largeur - retrait);
            if (g is not null)
            {
                using (var trait = new SolidBrush(Theme.Sur(BackColor, couleur, 0.7f)))
                {
                    g.FillRectangle(trait, x, y + E(1), E(3), h - E(2));
                }

                TextRenderer.DrawText(g, texte, PoliceDetail, new Rectangle(x + retrait, y, largeur - retrait, h), Theme.TexteDoux, Paragraphe);
            }

            return h;
        }

        private void Badge(Graphics g, float x, float y, int largeur)
        {
            var rb = new RectangleF(x, y, largeur, E(22));
            using var chemin = Dessin.Arrondi(rb, rb.Height / 2);
            using var fond = new SolidBrush(Theme.Sur(BackColor, _couleur, 0.16f));
            g.FillPath(fond, chemin);
            Dessin.Texte(g, _c.Badge, Theme.PoliceGrasse(7.5f), Rectangle.Round(rb), _couleur, centre: true);
        }

        private void Pastille(Graphics? g, int x, int y, string icone, string texte, Color couleur, int largeurMax = int.MaxValue)
        {
            if (g is null)
            {
                return;
            }

            var police = Theme.PoliceGrasse(7.5f);
            var t = E(12);
            var naturelle = TextRenderer.MeasureText(g, texte, police).Width + (int)t + Ei(18);
            var coupee = naturelle > largeurMax; // Intitulé de sortie long : coupé à la largeur de la carte.
            var l = coupee ? largeurMax : naturelle;
            var r = new RectangleF(x, y, l, E(20));
            using (var chemin = Dessin.Arrondi(r, r.Height / 2))
            using (var fond = new SolidBrush(Theme.Sur(BackColor, couleur, 0.14f)))
            {
                g.FillPath(fond, chemin);
            }

            Icones.Dessiner(g, icone, new RectangleF(x + E(8), y + ((r.Height - t) / 2), t, t), couleur, 2.4f);
            var xTexte = (int)(x + E(8) + t + E(4));
            Dessin.Texte(g, texte, police, new Rectangle(xTexte, y, coupee ? (int)(x + l - E(6)) - xTexte : l, Ei(20)), couleur);
        }

        /// <summary>« Encore au club / À envoyer chez … / note » ou « Chez … / Parti le … / retour ». Retourne sa hauteur.</summary>
        private int Encadre(Graphics? g, int x, int y, int largeur)
        {
            var pad = Ei(10);
            var t = E(14);
            var interieur = largeur - (2 * pad) - (int)(t + E(6));
            var lignes = new List<(string? Icone, string Texte, Font Police, Color Couleur)>();
            if (_c.Etape == EtapeRevision.AEnvoyer)
            {
                lignes.Add((Icones.Maison, "Encore au club", PoliceForte, Theme.Retour));
                lignes.Add((Icones.Fleche, $"À envoyer chez {_c.Prestataire ?? "un prestataire"}", PoliceForte, Theme.PrestataireClair));
                if (_c.NoteEnvoi.Length > 0)
                {
                    lignes.Add((null, _c.NoteEnvoi, PoliceDetail, Theme.TexteSecondaireClair));
                }
            }
            else
            {
                lignes.Add((Icones.Camion, $"Chez {_c.Prestataire ?? "le prestataire"}", PoliceForte, Theme.PrestataireClair));
                if (LigneDepart.Length > 0)
                {
                    lignes.Add((null, LigneDepart, PoliceDetail, Theme.Texte));
                }

                lignes.Add((null, LigneRetour, PoliceForte, _c.RetourEnRetard ? Theme.Erreur : Theme.PrestataireClair));
                if (_c.NoteEnvoi.Length > 0)
                {
                    lignes.Add((null, _c.NoteEnvoi, PoliceDetail, Theme.TexteSecondaireClair));
                }
            }

            var hauteurs = lignes.Select(l => Math.Max(Ei(18), Hauteur(l.Texte, l.Police, interieur))).ToList();
            var total = pad + hauteurs.Sum() + ((lignes.Count - 1) * Ei(3)) + pad - Ei(2);
            if (g is null)
            {
                return total;
            }

            var r = new RectangleF(x + 0.5f, y + 0.5f, largeur - 1, total - 1);
            var fond = _c.Etape == EtapeRevision.AEnvoyer ? Theme.Sur(BackColor, Theme.Fond, 0.45f) : Theme.Sur(BackColor, Theme.Prestataire, 0.12f);
            using (var chemin = Dessin.Arrondi(r, E(12)))
            using (var pinceau = new SolidBrush(fond))
            using (var bord = new Pen(Theme.Sur(BackColor, Theme.Prestataire, _c.Etape == EtapeRevision.AEnvoyer ? 0.6f : 0.42f), E(_c.Etape == EtapeRevision.AEnvoyer ? 1.5f : 1f)))
            {
                if (_c.Etape == EtapeRevision.AEnvoyer)
                {
                    bord.DashStyle = DashStyle.Dash;
                }

                g.FillPath(pinceau, chemin);
                g.DrawPath(bord, chemin);
            }

            var yl = y + pad - Ei(1);
            for (var i = 0; i < lignes.Count; i++)
            {
                var (icone, texte, police, couleur) = lignes[i];
                var xt = x + pad;
                if (icone is not null)
                {
                    Icones.Dessiner(g, icone, new RectangleF(xt, yl + E(2), t, t), couleur, 2.2f);
                }

                xt += (int)(t + E(6));
                TextRenderer.DrawText(g, texte, police, new Rectangle(xt, yl, interieur, hauteurs[i]), couleur, Paragraphe);
                yl += hauteurs[i] + Ei(3);
            }

            return total;
        }

        private void BarreUsage(Graphics g, UsageEquipement u, int x, int y, int largeur)
        {
            // Barre d'usage : jaune, rouge au seuil.
            var lTexte = TextRenderer.MeasureText(g, $"{u.Utilisations} / {u.Seuil}", Theme.PoliceTitre(7.5f)).Width;
            var piste = new RectangleF(x, y + E(6), largeur - lTexte - E(8), E(6));
            using (var chemin = Dessin.Arrondi(piste, piste.Height / 2))
            using (var fond = new SolidBrush(Theme.Teinte(Theme.AccentClair, 0.12f)))
            {
                g.FillPath(fond, chemin);
            }

            var part = Math.Min(1f, (float)u.Utilisations / Math.Max(1, u.Seuil));
            if (part > 0)
            {
                var plein = piste with { Width = Math.Max(piste.Height, piste.Width * part) };
                using var chemin = Dessin.Arrondi(plein, piste.Height / 2);
                using var pinceau = new SolidBrush(u.Etat == EtatCompteur.Atteint ? Theme.Erreur : Theme.Avertissement);
                g.FillPath(pinceau, chemin);
            }

            Dessin.Texte(g, $"{u.Utilisations} / {u.Seuil}", Theme.PoliceTitre(7.5f), new Rectangle((int)(piste.Right + E(8)), y, lTexte + 4, Ei(18)), Theme.TexteSecondaire);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Dessin.EffacerAvecParent(this, g);
            Dessin.Qualite(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            var parent = Dessin.CouleurParent(this);
            using (var chemin = Dessin.Arrondi(r, E(16)))
            using (var fond = new SolidBrush(BackColor))
            {
                g.FillPath(fond, chemin);
                switch (_c.Etape)
                {
                    case EtapeRevision.AEnvoyer:
                        // À envoyer : bordure violette en pointillés.
                        using (var bord = new Pen(Theme.Sur(parent, Theme.Prestataire, 0.55f), E(1.5f)) { DashStyle = DashStyle.Dash })
                        {
                            g.DrawPath(bord, chemin);
                        }

                        break;
                    case EtapeRevision.ChezPrestataire:
                        // Chez le prestataire : bordure violette et bordure gauche pleine de 5 px.
                        using (var bord = new Pen(Theme.Sur(parent, Theme.Prestataire, 0.5f), E(1)))
                        {
                            g.DrawPath(bord, chemin);
                        }

                        var ancien = g.Clip;
                        g.SetClip(chemin);
                        using (var gauche = new SolidBrush(Theme.Prestataire))
                        {
                            g.FillRectangle(gauche, 0, 0, E(5), Height);
                        }

                        g.Clip = ancien;
                        break;
                    default:
                        using (var bord = new Pen(Theme.Bordure, E(1)))
                        {
                            g.DrawPath(bord, chemin);
                        }

                        break;
                }
            }

            Parcourir(g, Width);
        }
    }

    /// <summary>Prochaine séance TIV (en tête de « Réparation interne ») : jour et mois, titre, lieu et inspecteurs.</summary>
    private sealed class CarteSeance : ControleDessine
    {
        private readonly DateOnly _date;
        private readonly string _titre;
        private readonly string _detail;

        public CarteSeance(DateOnly date, string titre, string detail)
        {
            (_date, _titre, _detail) = (date, titre, detail);
            AccessibleRole = AccessibleRole.StaticText;
            AccessibleName = $"{titre}, {Libelles.DateLongue(date)}. {detail}";
        }

        protected override void Peindre(Graphics g)
        {
            var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            using (var chemin = Dessin.Arrondi(r, E(14)))
            using (var fond = new SolidBrush(Theme.Teinte(Theme.Accent, 0.10f)))
            using (var bord = new Pen(Theme.Teinte(Theme.Accent, 0.25f), E(1)))
            {
                g.FillPath(fond, chemin);
                g.DrawPath(bord, chemin);
            }

            var l = (int)E(44);
            Dessin.Texte(g, _date.Day.ToString(Libelles.Culture), Theme.PoliceGrasse(13.5f), new Rectangle((int)E(6), (int)E(6), l, (int)E(26)), Theme.Texte, centre: true);
            Dessin.Texte(g, _date.ToString("MMM", Libelles.Culture).ToUpperInvariant(), Theme.PoliceGrasse(7.5f), new Rectangle((int)E(6), (int)E(31), l, (int)E(16)), Theme.AccentClair, centre: true);
            var x = (int)E(54);
            Dessin.Texte(g, _titre, Theme.PoliceTitre(9f), new Rectangle(x, (int)E(8), Width - x - (int)E(8), (int)E(22)), Theme.Texte);
            Dessin.Texte(g, _detail, Theme.Police(8.25f), new Rectangle(x, (int)E(30), Width - x - (int)E(8), (int)E(18)), Theme.TexteSecondaireClair);
        }
    }

    // ================================================================ Panneau « Ajouter une intervention »

    /// <summary>
    /// Panneau en haut de la page : l'équipement (lien vers sa fiche), nature, intervenant (inspecteur pour une TIV),
    /// date, résultat et conséquence, Annuler / Enregistrer l'intervention.
    /// </summary>
    private sealed class PanneauIntervention : Carte
    {
        private readonly ServiceRevisions _revisions;
        private readonly Label _code = Ui.Libelle("", Theme.PoliceCode(10f), Theme.Accent);
        private readonly Label _nom = Ui.Libelle("", Theme.PoliceTitre(10.5f));
        private readonly Label _controle = Ui.Libelle("", Theme.Police(9f), Theme.TexteSecondaire);
        private readonly ListeDeroulante _nature = new() { AccessibleName = "Nature" };
        private readonly ListeDeroulante _inspecteur = new() { AccessibleName = "Inspecteur TIV" };
        private readonly ChampSaisie _intervenant = new() { TexteIndicatif = "Magasin, organisme, personne…" };
        private readonly Label _libelleIntervenant = Ui.LibelleChamp("Intervenant");
        private readonly ChampDate _date = new() { AccessibleName = "Date de l'intervention" };
        private readonly ChampSaisie _description = new() { TexteIndicatif = "Ce qui a été fait : membrane changée, joints, nettoyage…" };
        private readonly List<ChoixResultat> _choix = [];
        private readonly BandeauMessage _consequence = new() { Margin = Padding.Empty };
        private ResultatControle _resultat = ResultatControle.Conforme;
        private CarteRevision? _carte;

        public PanneauIntervention(ServiceRevisions revisions)
        {
            _revisions = revisions;
            Padding = new Padding(22);
            Rayon = 24;
            CouleurBordure = Theme.Sur(Theme.Surface, Theme.Accent, 0.50f);
            BackColor = Theme.Teinte(Theme.Accent, 0.06f);
            _intervenant.Champ.AccessibleName = "Intervenant";

            // Gauche : étape, titre, équipement, lien vers la fiche, aide.
            var etape = Ui.Libelle("ENREGISTRER LE RÉSULTAT", Theme.PoliceGrasse(8.25f), Theme.AccentClair);
            var titre = Ui.Libelle("Ajouter une intervention", Theme.PoliceGrasse(15f));
            titre.Margin = new Padding(0, 2, 0, 10);
            _code.Margin = new Padding(0, 0, 0, 2);
            _nom.Margin = Padding.Empty;
            _controle.Margin = new Padding(0, 0, 0, 6);
            _controle.MaximumSize = new Size(320, 0); // Précision d'un signalement : sur plusieurs lignes.
            var fiche = new BoutonArrondi("Fiche ›", StyleBouton.Lien) { Height = Theme.HauteurCliquable, Margin = Padding.Empty };
            fiche.Click += (_, _) =>
            {
                if (_carte is { } c)
                {
                    FicheDemandee?.Invoke(this, c.Code);
                }
            };
            var aide = Ui.Libelle("L'intervention s'ajoute à l'historique de l'équipement.", Theme.Police(8.25f), Theme.TexteSecondaire);
            aide.MaximumSize = new Size(300, 0);
            var gauche = Ui.Pile(etape, titre, _code, _nom, _controle, fiche, aide);

            // Droite : nature / intervenant, date, résultat, conséquence, boutons.
            _nature.SelectedIndexChanged += (_, _) => MettreAJour();
            _date.Leave += (_, _) => MettreAJour();
            var hoteIntervenant = new Panel { BackColor = Color.Transparent, Margin = Padding.Empty };
            _inspecteur.Dock = _intervenant.Dock = DockStyle.Fill;
            hoteIntervenant.Controls.Add(_inspecteur);
            hoteIntervenant.Controls.Add(_intervenant);
            var champs = new TableLayoutPanel { ColumnCount = 3, RowCount = 2, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 12), Height = 34 + Theme.HauteurChamp };
            for (var i = 0; i < 3; i++)
            {
                champs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            }

            champs.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            champs.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.HauteurChamp));
            Control[] valeurs = [_nature, hoteIntervenant, _date];
            Label[] libelles = [Ui.LibelleChamp("Nature"), _libelleIntervenant, Ui.LibelleChamp("Date")];
            for (var i = 0; i < 3; i++)
            {
                var m = new Padding(i == 0 ? 0 : 5, 0, i == 2 ? 0 : 5, 0);
                libelles[i].Margin = m;
                valeurs[i].Margin = m;
                valeurs[i].Dock = DockStyle.Fill;
                champs.Controls.Add(libelles[i], i, 0);
                champs.Controls.Add(valeurs[i], i, 1);
            }

            var resultats = new TableLayoutPanel { ColumnCount = 3, Height = Theme.HauteurCliquable, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 10) };
            foreach (var (resultat, i) in new[] { ResultatControle.Conforme, ResultatControle.ASurveiller, ResultatControle.Refuse }.Select((r, i) => (r, i)))
            {
                resultats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
                var bouton = new ChoixResultat(ServiceRevisions.LibelleResultat(resultat), ChoixResultat.Couleur(resultat))
                {
                    Selectionne = resultat == _resultat,
                    Margin = new Padding(i == 0 ? 0 : 4, 0, i == 2 ? 0 : 4, 0),
                    Dock = DockStyle.Fill,
                };
                bouton.Click += (_, _) =>
                {
                    _resultat = resultat;
                    foreach (var b in _choix)
                    {
                        b.Selectionne = b == bouton;
                    }

                    MettreAJour();
                };
                _choix.Add(bouton);
                resultats.Controls.Add(bouton, i, 0);
            }

            var annuler = new BoutonArrondi("Annuler", StyleBouton.Secondaire) { AutoSize = false, Margin = new Padding(0, 0, 6, 0) };
            annuler.Click += (_, _) => Annule?.Invoke(this, EventArgs.Empty);
            Valider = new BoutonArrondi("Enregistrer l'intervention", StyleBouton.Principal) { AutoSize = false, Margin = new Padding(6, 0, 0, 0) };
            Valider.Click += async (_, _) => await EnregistrerAsync();
            var boutons = Ui.Colonnes((annuler, SizeType.Percent, 34), (Valider, SizeType.Percent, 66));

            _description.Champ.AccessibleName = "Description de l'intervention";
            _description.Champ.MaxLength = ServiceRevisions.LongueurDescription;
            _description.Margin = new Padding(0, 0, 0, 12);
            var libelleDescription = Ui.LibelleChamp("Description (facultatif)");
            libelleDescription.Margin = new Padding(0, 0, 0, 8);
            var droite = Ui.Colonne(
                (champs, SizeType.Absolute, 34 + Theme.HauteurChamp + 12),
                (libelleDescription, SizeType.Absolute, 30),
                (_description, SizeType.Absolute, Theme.HauteurChamp + 12),
                (Ui.LibelleChamp("Résultat"), SizeType.AutoSize, 0),
                (resultats, SizeType.Absolute, Theme.HauteurCliquable + 10),
                (_consequence, SizeType.Absolute, 62),
                (boutons, SizeType.Absolute, Theme.HauteurBouton + 12));
            _consequence.Margin = new Padding(0, 0, 0, 12);

            var corps = Ui.Colonnes((gauche, SizeType.Percent, 34), (droite, SizeType.Percent, 66));
            gauche.Margin = new Padding(0, 0, 22, 0);
            Controls.Add(corps);
            AccessibleName = "Ajouter une intervention";
        }

        public BoutonArrondi Valider { get; }

        public event EventHandler? Annule;

        public event EventHandler? Enregistre;

        public event EventHandler<string>? FicheDemandee;

        public int HauteurPour(int largeur) => (int)Dessin.Echelle(this, 22 + 34 + Theme.HauteurChamp + 12 + 30 + Theme.HauteurChamp + 12 + 30 + Theme.HauteurCliquable + 10 + 62 + Theme.HauteurBouton + 12 + 22);

        public void PrendreFocus() => _nature.Focus();

        /// <summary>Remplit le panneau pour une carte : natures du contrôle, intervenant (prestataire de l'atelier, inspecteur pour une TIV).</summary>
        public void Preparer(CarteRevision carte, IReadOnlyList<Moniteur> inspecteurs)
        {
            _carte = carte;
            _code.Text = carte.Code;
            _nom.Text = carte.Libelle;
            // Problème signalé : la cause et la précision du moniteur, pour savoir quoi réparer.
            _controle.Text = carte.Precision.Length > 0 ? $"{carte.Controle} · « {carte.Precision} »" : carte.Controle;

            // Bloc : TIV ou requalification ; autre matériel : les natures de révision.
            var natures = ServiceRevisions.EstBloc(carte.Categorie)
                ? new[] { "TIV", "Requalification" }.OrderBy(n => n == (carte.Type == TypeControle.Requalification ? "Requalification" : "TIV") ? 0 : 1).ToList()
                : ServiceRevisions.NaturesPour(TypeControle.Revision, carte.Categorie).ToList();
            _nature.Items.Clear();
            foreach (var n in natures)
            {
                _nature.Items.Add(n);
            }

            _inspecteur.Items.Clear();
            foreach (var m in inspecteurs)
            {
                _inspecteur.Items.Add(new Choix<long>(m.Id, m.NomComplet));
            }

            if (_inspecteur.Items.Count > 0)
            {
                _inspecteur.SelectedIndex = 0;
            }

            _intervenant.Valeur = carte.Prestataire ?? "";
            _description.Valeur = "";
            _date.Date = DateOnly.FromDateTime(DateTime.Today);
            _resultat = ResultatControle.Conforme;
            foreach (var b in _choix)
            {
                b.Selectionne = b == _choix[0];
            }

            _nature.SelectedIndex = 0;
            MettreAJour();
        }

        private TypeControle Type => (_nature.SelectedItem as string) switch
        {
            "TIV" => TypeControle.Tiv,
            "Requalification" => TypeControle.Requalification,
            _ => TypeControle.Revision,
        };

        private void MettreAJour()
        {
            if (_carte is null)
            {
                return;
            }

            var tiv = Type == TypeControle.Tiv;
            _inspecteur.Visible = tiv;
            _intervenant.Visible = !tiv;
            _libelleIntervenant.Text = tiv ? "Inspecteur TIV" : "Intervenant";
            var niveau = _resultat switch
            {
                ResultatControle.Conforme => NiveauResultat.Succes,
                ResultatControle.ASurveiller => NiveauResultat.Avertissement,
                _ => NiveauResultat.Erreur,
            };
            if (tiv && _inspecteur.Items.Count == 0)
            {
                _consequence.Afficher(NiveauResultat.Avertissement, "Aucun inspecteur TIV : cochez « Inspecteur TIV » sur la fiche d'un moniteur.");
                return;
            }

            var texte = _revisions.Consequence(Type, _resultat, _date.Date ?? DateOnly.FromDateTime(DateTime.Today), _carte.Categorie);
            if (_resultat == ResultatControle.Conforme)
            {
                texte = "Retour en service. " + texte;
            }

            _consequence.Afficher(niveau, texte);
        }

        private async Task EnregistrerAsync()
        {
            if (_carte is not { } c)
            {
                return;
            }

            if (_date.Date is not { } date)
            {
                _consequence.Afficher(NiveauResultat.Erreur, "Date invalide : écrivez-la comme 08/11/2026.");
                return;
            }

            var tiv = Type == TypeControle.Tiv;
            var inspecteur = tiv ? _inspecteur.SelectedItem as Choix<long> : null;
            Valider.Enabled = false;
            try
            {
                var resultat = await _revisions.EnregistrerInterventionAsync(new Intervention(
                    c.EquipementId, Type, _resultat, date, _nature.SelectedItem as string ?? "",
                    tiv ? inspecteur?.Texte ?? "" : _intervenant.Valeur, inspecteur?.Valeur, c.PassageAtelierId, Notes: _description.Valeur));
                if (!resultat.Reussi)
                {
                    _consequence.Afficher(NiveauResultat.Erreur, resultat.Message);
                    return;
                }

                Enregistre?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                if (!IsDisposed)
                {
                    Valider.Enabled = true;
                }
            }
        }
    }

    /// <summary>Note d'information (pictogramme i + texte), sous le tableau.</summary>
    private sealed class NoteInfo : ControleDessine
    {
        public NoteInfo(string texte)
        {
            Text = texte;
            AccessibleName = texte;
        }

        protected override void Peindre(Graphics g)
        {
            var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            using (var chemin = Dessin.Arrondi(r, E(18)))
            {
                using var fond = new SolidBrush(Theme.Teinte(Theme.Accent, 0.06f));
                g.FillPath(fond, chemin);
                using var bord = new Pen(Theme.Teinte(Theme.Accent, 0.16f), E(1));
                g.DrawPath(bord, chemin);
            }

            var t = E(20);
            Icones.Dessiner(g, Icones.Info, new RectangleF(E(18), E(14), t, t), Theme.AccentClair, 1.9f);
            var gauche = (int)(E(18) + t + E(12));
            TextRenderer.DrawText(g, Text, Theme.Police(9.75f), new Rectangle(gauche, (int)E(12), Width - gauche - (int)E(16), Height - (int)E(16)),
                Theme.TexteSecondaireClair, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }
}
