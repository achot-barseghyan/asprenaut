using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;
using Saisie = Asprenaut.Materiel.Core.Saisie;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Paramètres : pistolet, règles des sorties, données et sauvegardes, club, à propos.
/// Chaque changement est enregistré tout de suite (pas de bouton « Enregistrer »).
/// </summary>
public sealed class EcranParametres : EcranBase
{
    private readonly ServiceReglages _reglages;
    private readonly IServiceSauvegarde _sauvegardes;
    private readonly INavigation _navigation;
    private readonly ParametresApplication _parametres;
    private readonly BandeauMessage _etatSauvegarde = new() { Margin = new Padding(0, 0, 0, 18) };
    private readonly ChampSaisie _dossier = new();
    private readonly Label _dernierCode = Ui.Libelle("", Theme.Police(9.75f), Theme.RetourClair);
    private readonly Panel _cadre = new() { BackColor = Theme.Fond, Margin = new Padding(0, 4, 0, 0) };
    private CarteSection? _carteAffichage;

    /// <summary>Paramètre de navigation : rouvrir l'écran sur la carte « Affichage » (après un changement de thème).</summary>
    public const string VersAffichage = "affichage";

    private readonly ServiceGestion _gestion;
    private readonly GardeGestion _garde;
    private readonly ServiceExport _export;
    private readonly ServiceCompteur _compteur;
    private readonly ServiceTypesSortie _typesSortie;

    public EcranParametres(ServiceReglages reglages, IServiceSauvegarde sauvegardes, INavigation navigation, ParametresApplication parametres, ServiceGestion gestion, GardeGestion garde, ServiceExport export, ServiceCompteur compteur, ServiceTypesSortie typesSortie)
    {
        _export = export;
        _compteur = compteur;
        _typesSortie = typesSortie;
        _gestion = gestion;
        _garde = garde;
        _reglages = reglages;
        _sauvegardes = sauvegardes;
        _navigation = navigation;
        _parametres = parametres;

        // Deux colonnes de cartes ; chaque rangée prend la hauteur de sa carte la plus haute.
        var grille = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };
        grille.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grille.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var cartes = new[] { CarteSecurite(), CartePistolet(), CarteRegles(), CarteCompteur(), CarteDonnees(), CarteClub(), CarteTypesSortie(), CarteAffichage(), CarteAPropos() };
        foreach (var carte in cartes[..^1]) // Toutes sauf « À propos » : leurs réglages demandent le code.
        {
            _garde.Lier(carte, ActionProtegee.Parametres);
        }

        for (var i = 0; i < cartes.Length; i++)
        {
            if (i % 2 == 0)
            {
                grille.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            cartes[i].AutoSize = true;
            cartes[i].Dock = DockStyle.Fill;
            cartes[i].Margin = new Padding(i % 2 == 0 ? 0 : 10, 0, i % 2 == 0 ? 10 : 0, 20);
            grille.Controls.Add(cartes[i], i % 2, i / 2);
        }

        // Cadre défilant vertical (la page est plus haute que l'écran).
        var cadre = _cadre;
        cadre.HorizontalScroll.Maximum = 0;
        cadre.AutoScroll = false;
        cadre.VerticalScroll.Visible = false;
        cadre.AutoScroll = true;
        Natif.ScrollbarsDuTheme(cadre);
        cadre.Controls.Add(grille);

        var consultation = new BandeauConsultation(_garde, "Consultez librement. Toute modification des paramètres demande le code gestionnaire.");
        _garde.Lier(consultation, ActionProtegee.Parametres);

        Controls.Add(Ui.Colonne(
            (Ui.Entete(Ui.Pile(Ui.Titre(Titre), Ui.SousTitre("Pistolet, règles des sorties et sauvegardes."))), SizeType.AutoSize, 0),
            (consultation, SizeType.AutoSize, 0),
            (cadre, SizeType.Percent, 100)));
    }

    public override string Titre => "Paramètres";

    public override EntreeMenu Menu => EntreeMenu.Parametres;

    protected override async Task ChargerAsync(object? parametre)
    {
        AfficherEtatSauvegarde();
        await AfficherSeuilsAsync();
        await AfficherTypesAsync();
        if (parametre is VersAffichage && _carteAffichage is { } carte)
        {
            // L'écran vient d'être recréé dans le nouveau thème : on revient sur la carte du choix, une fois que
            // les cartes ont pris leur hauteur (ajustements différés), et avant que l'écran soit montré.
            _versAffichage = true;
            for (var i = 0; i < 4 && !IsDisposed; i++)
            {
                await Task.Yield();
                PerformLayout();
            }

            AllerALaCarteAffichage(carte);
        }
    }

    private bool _versAffichage;

    /// <summary>Une fois l'écran en place (Dock = Fill), on vérifie qu'il est bien positionné sur la carte « Affichage ».</summary>
    protected override void OnDockChanged(EventArgs e)
    {
        base.OnDockChanged(e);
        if (_versAffichage && Dock == DockStyle.Fill && _carteAffichage is { } carte && IsHandleCreated)
        {
            _versAffichage = false;
            BeginInvoke(() => AllerALaCarteAffichage(carte));
        }
    }

    private void AllerALaCarteAffichage(Control carte)
    {
        if (IsDisposed || carte.Parent is not { } grille)
        {
            return;
        }

        // Position de la carte dans la page qui défile (la grille est décalée de la position de défilement).
        var haut = grille.Top - _cadre.AutoScrollPosition.Y + carte.Top;
        _cadre.AutoScrollPosition = new Point(0, Math.Max(0, haut - (int)Dessin.Echelle(this, 8)));
    }

    // ---------------------------------------------------------------- Affichage

    private CarteSection CarteAffichage()
    {
        var carte = new CarteSection("Affichage", Theme.EstSombre ? Icones.Lune : Icones.Soleil, Theme.AccentClair);
        carte.Ajouter(Ui.LibelleChamp("Thème de l'écran"));
        var aide = Ui.Libelle("Le thème clair se lit mieux en plein jour ou près d'une fenêtre.", Theme.Police(9.75f), Theme.TexteSecondaire);
        aide.Margin = new Padding(0, 0, 0, 14);
        carte.Ajouter(aide);

        var sombre = new ApercuTheme(ModeAffichage.Sombre) { Margin = new Padding(0, 0, 8, 0) };
        var clair = new ApercuTheme(ModeAffichage.Clair) { Margin = new Padding(8, 0, 0, 0) };
        foreach (var apercu in new[] { sombre, clair })
        {
            var mode = apercu.Mode;
            apercu.Click += (_, _) => ChoisirTheme(mode);
        }

        var choix = Ui.Colonnes((sombre, SizeType.Percent, 50), (clair, SizeType.Percent, 50));
        choix.Height = ApercuTheme.Hauteur;
        carte.Ajouter(choix);
        _carteAffichage = carte;
        return carte;
    }

    private void ChoisirTheme(ModeAffichage mode)
    {
        if (mode == Theme.Mode || !Autoriser())
        {
            return;
        }

        var resultat = _reglages.ChangerAffichage(mode);
        if (!resultat.Reussi)
        {
            Dialogues.Erreur(FindForm(), resultat.Message);
            return;
        }

        // Le menu se refait tout de suite (MainForm) ; cet écran est recréé dans le nouveau thème.
        Theme.Appliquer(mode);
        _navigation.AllerA<EcranParametres>(VersAffichage);
    }

    // ---------------------------------------------------------------- Sécurité · mode gestion

    private CarteSection CarteSecurite()
    {
        var r = _reglages.Actuels;
        var carte = new CarteSection("Sécurité · mode gestion", Icones.Cadenas, Theme.Avertissement)
        {
            CouleurBordure = Theme.Teinte(Theme.Avertissement, 0.35f),
        };
        var intro = Ui.Libelle("Scanner les départs et retours reste libre pour tous. Ajouter, modifier ou supprimer demande le mode gestion.",
            Theme.Police(9.75f), Theme.TexteSecondaire);
        intro.Margin = new Padding(0, 0, 0, 6);
        carte.Ajouter(intro);

        if (_gestion.ProtectionActive)
        {
            var points = Ui.Libelle("••••••", Theme.PoliceGrasse(13f), Theme.TexteDoux);
            points.Margin = new Padding(0, 8, 14, 0);
            points.AccessibleName = "Code masqué";
            var changer = new BoutonArrondi("Changer", StyleBouton.Secondaire) { Height = Theme.HauteurCliquable, Margin = Padding.Empty };
            changer.Click += (_, _) => ChangerCode();
            carte.Ajouter(new LigneReglage("Code gestionnaire", "4 à 6 chiffres, partagé avec le bureau", Ui.Rangee(true, points, changer), filet: false));
        }
        else
        {
            var definir = new BoutonArrondi("Définir un code", StyleBouton.Principal, Icones.Cadenas) { Height = Theme.HauteurCliquable, Margin = Padding.Empty };
            definir.Click += (_, _) => ChangerCode();
            carte.Ajouter(new LigneReglage("Code gestionnaire", "Aucun code : tout le monde peut tout modifier", definir, filet: false));
        }

        var duree = new ListeDeroulante { Width = 150, AccessibleName = "Rester déverrouillé" };
        foreach (var m in Reglages.MinutesDeverrouillagePossibles)
        {
            duree.Items.Add(new Choix<int>(m, $"{m} min"));
        }

        duree.SelectedIndex = Math.Max(0, Array.IndexOf(Reglages.MinutesDeverrouillagePossibles, r.MinutesDeverrouillage));
        ProtegerListe(duree, () => Array.IndexOf(Reglages.MinutesDeverrouillagePossibles, _reglages.Actuels.MinutesDeverrouillage), choix =>
        {
            if (choix is Choix<int> c)
            {
                _reglages.ChangerMinutesDeverrouillage(c.Valeur);
            }
        });
        carte.Ajouter(new LigneReglage("Rester déverrouillé", "Sans activité, l'appli repasse seule en consultation", duree));
        carte.Ajouter(Interrupteur("Verrouiller à la fermeture", "Au prochain lancement, l'appli démarre en consultation", r.VerrouillerALaFermeture,
            v => _reglages.Modifier(x => x.VerrouillerALaFermeture = v)));
        carte.Ajouter(Interrupteur("Code pour clôturer avec des manquants", "Évite de clôturer trop vite une sortie incomplète", r.CodePourCloturerAvecManquants,
            v => _reglages.Modifier(x => x.CodePourCloturerAvecManquants = v)));

        var titreActions = Ui.LibelleChamp("Actions protégées");
        titreActions.Margin = new Padding(0, 14, 0, 8);
        carte.Ajouter(titreActions);
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Color.Transparent, Margin = Padding.Empty };
        foreach (var a in new[] { ActionProtegee.Materiel, ActionProtegee.Moniteurs, ActionProtegee.SupprimerSortie, ActionProtegee.Revisions, ActionProtegee.CompteurSortie, ActionProtegee.Parametres, ActionProtegee.RestaurerSauvegarde })
        {
            actions.Controls.Add(new Pastille(ServiceGestion.Libelle(a), Theme.Teinte(Theme.Avertissement, 0.10f), Theme.Avertissement, police: Theme.PoliceTitre(9f), hauteur: 30)
            {
                Margin = new Padding(0, 0, 6, 6),
            });
        }

        carte.Ajouter(actions);
        var libres = Ui.Libelle("Libres pour tous : créer ou modifier une sortie en préparation, scanner départ et retour, consulter. Tous les paramètres demandent le code.",
            Theme.Police(9f), Theme.TexteSecondaire);
        libres.Margin = new Padding(0, 2, 0, 0);
        carte.Ajouter(libres);

        if (_gestion.ProtectionActive)
        {
            var retirer = new BoutonArrondi("Retirer le code (plus rien de verrouillé)", StyleBouton.Lien) { Height = Theme.HauteurCliquable, Margin = new Padding(0, 8, 0, 0) };
            retirer.Click += (_, _) => RetirerCode();
            carte.Ajouter(retirer, pleineLargeur: false);
        }

        return carte;
    }

    /// <summary>Champ de code : chiffres seulement, masqués.</summary>
    private static ChampSaisie ChampCode(string nom)
    {
        var champ = new ChampSaisie();
        champ.Champ.UseSystemPasswordChar = true;
        champ.Champ.MaxLength = ServiceGestion.LongueurMax;
        champ.Champ.AccessibleName = nom;
        champ.Champ.KeyPress += (_, e) => e.Handled = !char.IsAsciiDigit(e.KeyChar) && !char.IsControl(e.KeyChar);
        return champ;
    }

    /// <summary>Premier code, ou changement : l'ancien code est alors demandé.</summary>
    private void ChangerCode()
    {
        var premier = !_gestion.ProtectionActive;
        var ancien = ChampCode("Code actuel");
        var nouveau = ChampCode("Nouveau code");
        var confirmation = ChampCode("Nouveau code, encore une fois");
        using var fenetre = new FenetreFormulaire(premier ? "Définir le code gestionnaire" : "Changer le code gestionnaire", "Enregistrer",
            () => Task.FromResult(_gestion.DefinirCode(premier ? null : ancien.Valeur, nouveau.Valeur, confirmation.Valeur)));
        if (!premier)
        {
            fenetre.Champ("Code actuel", ancien);
        }

        fenetre.DeuxChamps("Nouveau code (4 à 6 chiffres)", nouveau, "Encore une fois", confirmation);
        var aide = Ui.Libelle(premier
                ? "Dès maintenant, ajouter, modifier ou supprimer demandera ce code. Notez-le et donnez-le au bureau."
                : "Le nouveau code remplace l'ancien pour tout le monde.",
            Theme.Police(9.75f), Theme.TexteSecondaire);
        aide.MaximumSize = new Size(460, 0);
        fenetre.Ajouter(aide);
        if (fenetre.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Dialogues.Information(FindForm(), premier ? "Code gestionnaire défini." : "Code gestionnaire changé.");
            _navigation.AllerA<EcranParametres>(); // La carte Sécurité change d'aspect.
        }
    }

    private void RetirerCode()
    {
        var ancien = ChampCode("Code actuel");
        using var fenetre = new FenetreFormulaire("Retirer le code gestionnaire", "Retirer le code",
            () => Task.FromResult(_gestion.RetirerCode(ancien.Valeur)));
        fenetre.Champ("Code actuel", ancien);
        var aide = Ui.Libelle("Sans code, tout le monde peut ajouter, modifier ou supprimer.", Theme.Police(9.75f), Theme.TexteSecondaire);
        fenetre.Ajouter(aide);
        if (fenetre.ShowDialog(FindForm()) == DialogResult.OK)
        {
            _navigation.AllerA<EcranParametres>();
        }
    }

    // ---------------------------------------------------------------- Pistolet

    private CarteSection CartePistolet()
    {
        var r = _reglages.Actuels;
        var carte = new CarteSection("Pistolet scanner", Icones.Scanner, Theme.AccentClair);

        // Zone d'essai : on scanne une étiquette, on voit ce que l'application en lit.
        var essai = new ChampSaisie { TexteIndicatif = "Scannez une étiquette ici…" };
        essai.Champ.Font = Theme.PoliceCode(11.25f);
        essai.Champ.AccessibleName = "Tester le pistolet";
        essai.Margin = new Padding(0, 0, 0, 10);
        essai.Champ.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.SuppressKeyPress = true;
            var brut = essai.Valeur.Trim();
            var code = Saisie.CodeScanne.Nettoyer(brut, _reglages.Actuels.PrefixeEtiquette);
            _dernierCode.Text = code.Length == 0
                ? "Rien n'a été lu. Visez l'étiquette et réessayez."
                : brut == code ? $"✓ Dernier code lu : {code}" : $"✓ Dernier code lu : {brut} → {code}";
            _dernierCode.ForeColor = code.Length == 0 ? Theme.Erreur : Theme.RetourClair;
            essai.Valeur = "";
        };
        _dernierCode.Text = "Le code lu s'affichera ici.";
        _dernierCode.ForeColor = Theme.TexteSecondaire;
        _dernierCode.Margin = Padding.Empty;

        var zone = new CadrePointille { Margin = new Padding(0, 0, 0, 6) };
        zone.Ajouter(Ui.LibelleChamp("Tester le pistolet"));
        zone.Ajouter(essai);
        zone.Ajouter(_dernierCode);
        carte.Ajouter(zone);

        carte.Ajouter(Interrupteur("Sons de confirmation", "Bip au succès, son grave en cas d'erreur", r.SonsActifs, v => _reglages.Modifier(x => x.SonsActifs = v), filet: false));
        carte.Ajouter(Interrupteur("Garder le champ de scan actif", "Le curseur revient seul dans la zone de scan", r.GarderFocusScan, v => _reglages.Modifier(x => x.GarderFocusScan = v)));
        carte.Ajouter(Interrupteur("Ignorer les doubles lectures", "Même code lu deux fois en moins d'une seconde", r.IgnorerDoublesLectures, v => _reglages.Modifier(x => x.IgnorerDoublesLectures = v)));

        var prefixe = new ChampSaisie { Size = new Size(110, Theme.HauteurCliquable), Valeur = r.PrefixeEtiquette };
        prefixe.Champ.Font = Theme.PoliceCode(11.25f);
        prefixe.Champ.TextAlign = HorizontalAlignment.Center;
        prefixe.Champ.CharacterCasing = CharacterCasing.Upper;
        prefixe.Champ.MaxLength = ServiceReglages.LongueurMaxPrefixe;
        prefixe.Champ.AccessibleName = "Préfixe des étiquettes";
        prefixe.Champ.Leave += (_, _) =>
        {
            if (Saisie.CodeScanne.NormaliserPrefixe(prefixe.Valeur) == _reglages.Actuels.PrefixeEtiquette)
            {
                prefixe.Valeur = _reglages.Actuels.PrefixeEtiquette;
                return;
            }

            // Différé : pas de fenêtre (code gestionnaire, erreur) pendant que le focus quitte le champ.
            BeginInvoke(() =>
            {
                var resultat = Autoriser() ? _reglages.ChangerPrefixe(prefixe.Valeur) : null;
                prefixe.Valeur = _reglages.Actuels.PrefixeEtiquette;
                if (resultat is { Reussi: false })
                {
                    Dialogues.Information(FindForm(), resultat.Message);
                }
            });
        };
        carte.Ajouter(new LigneReglage("Préfixe des étiquettes", "Retiré automatiquement à la lecture", prefixe));
        return carte;
    }

    // ---------------------------------------------------------------- Règles

    private CarteSection CarteRegles()
    {
        var r = _reglages.Actuels;
        var carte = new CarteSection("Règles des sorties", Icones.Bouclier, Theme.DepartClair);

        var jours = new SelecteurNombre("Jours avant alerte", n => $"{n} j") { Nombre = r.JoursAvantAlerteRetour };
        jours.Demande += (_, n) =>
        {
            if (Autoriser())
            {
                jours.Nombre = _reglages.ChangerJoursAlerte(n);
            }
        };
        carte.Ajouter(new LigneReglage("Alerte si non rendu après", "La sortie passe en orange dans la liste", jours, filet: false));

        var revision = new ListeDeroulante { Width = 150, AccessibleName = "Prévenir avant une révision" };
        foreach (var j in Reglages.JoursRevisionPossibles)
        {
            revision.Items.Add(new Choix<int>(j, $"{j} jours"));
        }

        revision.SelectedIndex = Math.Max(0, Array.IndexOf(Reglages.JoursRevisionPossibles, r.JoursAvantRevision));
        ProtegerListe(revision, () => Array.IndexOf(Reglages.JoursRevisionPossibles, _reglages.Actuels.JoursAvantRevision), choix =>
        {
            if (choix is Choix<int> c)
            {
                _reglages.ChangerJoursRevision(c.Valeur);
            }
        });
        carte.Ajouter(new LigneReglage("Prévenir avant une révision", "Badge jaune dans l'inventaire", revision));

        carte.Ajouter(Interrupteur("Autoriser le départ si révision dépassée", "Le matériel part avec un avertissement jaune", r.DepartSiRevisionDepassee, v => _reglages.Modifier(x => x.DepartSiRevisionDepassee = v)));
        carte.Ajouter(Interrupteur("Confirmer avant de clôturer", "Demande validation s'il reste du matériel dehors", r.ConfirmerCloture, v => _reglages.Modifier(x => x.ConfirmerCloture = v)));
        return carte;
    }

    // ---------------------------------------------------------------- Compteur d'utilisation

    private readonly TableLayoutPanel _seuilsSpecifiques = new()
    {
        ColumnCount = 1,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        BackColor = Color.Transparent,
        Margin = Padding.Empty,
    };

    /// <summary>
    /// Révision au bout d'un nombre de sorties (retours scannés) depuis le dernier contrôle : seuil par catégorie,
    /// seuils propres à un modèle ou à un équipement, prévenir à 90 %, bloquer au seuil.
    /// </summary>
    private CarteSection CarteCompteur()
    {
        var r = _reglages.Actuels;
        var carte = new CarteSection("Compteur d'utilisation", Icones.Revisions, Theme.Avertissement);
        var intro = Ui.Libelle("Chaque retour scanné ajoute les utilisations de la sortie (réglées par type dans « Types de sortie et utilisations »). La révision tombe au seuil ou à la date, le premier atteint.", Theme.Police(9.75f), Theme.TexteSecondaire);
        intro.MaximumSize = new Size(560, 0);
        intro.Margin = new Padding(0, 0, 0, 6);
        carte.Ajouter(intro);

        (string Categorie, string Libelle, string Aide)[] types =
        [
            ("Détendeurs", "Détendeurs", "Conseil : suivre la notice du fabricant"),
            ("Gilets", "Gilets stabilisateurs", "Inflateur, purges et sangles"),
            ("Ordinateurs", "Ordinateurs", "Pile et joints"),
            ("Blocs", "Blocs", "Robinetterie, en plus de la TIV"),
        ];
        for (var i = 0; i < types.Length; i++)
        {
            var (categorie, libelle, aide) = types[i];
            var seuil = new SelecteurNombre($"Seuil {libelle}", n => n == 0 ? "Désactivé" : $"{n} utilis.") { Width = 210, Nombre = r.SeuilsSorties.GetValueOrDefault(categorie) };
            seuil.Demande += (_, n) =>
            {
                if (Autoriser())
                {
                    var voulu = seuil.Nombre + (Math.Sign(n - seuil.Nombre) * ServiceCompteur.Pas);
                    if (_compteur.ChangerSeuilCategorie(categorie, voulu) is { Reussi: true, Id: { } retenu })
                    {
                        seuil.Nombre = (int)retenu;
                    }
                }
            };
            carte.Ajouter(new LigneReglage(libelle, aide, seuil, filet: i > 0));
        }

        carte.Ajouter(Interrupteur("Prévenir à 90 % du seuil", "L'équipement passe en « À planifier » dans Révisions", r.PrevenirAvantSeuil, v => _reglages.Modifier(x => x.PrevenirAvantSeuil = v)));
        carte.Ajouter(Interrupteur("Bloquer au seuil atteint", "Refus au scan de départ tant que la révision n'est pas faite", r.BloquerAuSeuil, v => _reglages.Modifier(x => x.BloquerAuSeuil = v)));

        // --- Seuils spécifiques ---
        var titre = Ui.Libelle("Seuils spécifiques", Theme.PoliceTitre(11.25f));
        titre.Margin = new Padding(0, 18, 0, 2);
        carte.Ajouter(titre);
        var aideSpecifique = Ui.Libelle("Pour un modèle (nom de l'équipement dans l'inventaire) ou un équipement précis, selon la notice du fabricant. Remplace le seuil du type.",
            Theme.Police(9.75f), Theme.TexteSecondaire);
        aideSpecifique.MaximumSize = new Size(560, 0);
        aideSpecifique.Margin = new Padding(0, 0, 0, 10);
        carte.Ajouter(aideSpecifique);
        _seuilsSpecifiques.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        carte.Ajouter(_seuilsSpecifiques);

        var portee = new ListeDeroulante { Width = 150, AccessibleName = "Appliquer à" };
        portee.Items.Add(new Choix<PorteeSeuil>(PorteeSeuil.Modele, "Modèle"));
        portee.Items.Add(new Choix<PorteeSeuil>(PorteeSeuil.Equipement, "Équipement"));
        portee.SelectedIndex = 0;
        var saisie = new ChampSaisie { TexteIndicatif = "Nom du modèle, ex. Détendeur Mares", Margin = new Padding(8, 0, 8, 0) };
        saisie.Champ.AccessibleName = "Code ou modèle";
        portee.SelectedIndexChanged += (_, _) => saisie.TexteIndicatif = portee.SelectedItem is Choix<PorteeSeuil> { Valeur: PorteeSeuil.Equipement }
            ? "Scannez l'équipement (ex. DET-011)"
            : "Nom du modèle, ex. Détendeur Mares";
        var ajouter = new BoutonArrondi("+ Ajouter", StyleBouton.Principal) { Height = Theme.HauteurChamp, Margin = Padding.Empty };
        async Task AjouterAsync()
        {
            if (!Autoriser() || portee.SelectedItem is not Choix<PorteeSeuil> choix)
            {
                return;
            }

            var resultat = await _compteur.AjouterSeuilSpecifiqueAsync(choix.Valeur, saisie.Valeur);
            if (!resultat.Reussi)
            {
                Dialogues.Erreur(FindForm(), resultat.Message);
                return;
            }

            saisie.Valeur = "";
            await AfficherSeuilsAsync();
            saisie.Champ.Focus();
        }

        ajouter.Click += (_, _) => Executer(AjouterAsync);
        saisie.Champ.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) // Le pistolet tape le code puis Entrée.
            {
                e.SuppressKeyPress = true;
                Executer(AjouterAsync);
            }
        };
        var ligneAjout = Ui.Colonnes((portee, SizeType.Absolute, 150), (saisie, SizeType.Percent, 100), (ajouter, SizeType.AutoSize, 0));
        ligneAjout.Height = Theme.HauteurChamp;
        ligneAjout.Margin = new Padding(0, 8, 0, 0);
        carte.Ajouter(ligneAjout);
        return carte;
    }

    /// <summary>Liste des seuils spécifiques, reconstruite après chaque ajout ou retrait.</summary>
    private async Task AfficherSeuilsAsync()
    {
        var seuils = await _compteur.DecrireSeuilsSpecifiquesAsync();
        if (IsDisposed)
        {
            return;
        }

        _seuilsSpecifiques.SuspendLayout();
        while (_seuilsSpecifiques.Controls.Count > 0)
        {
            var c = _seuilsSpecifiques.Controls[0];
            _seuilsSpecifiques.Controls.RemoveAt(0);
            c.Dispose();
        }

        _seuilsSpecifiques.RowStyles.Clear();
        if (seuils.Count == 0)
        {
            var vide = Ui.Libelle("Aucun seuil spécifique : chaque équipement suit le seuil de son type.", Theme.Police(9.75f), Theme.TexteSecondaire);
            vide.Margin = new Padding(0, 4, 0, 4);
            AjouterSeuil(vide);
        }

        foreach (var s in seuils)
        {
            AjouterSeuil(LigneSeuil(s));
        }

        _seuilsSpecifiques.ResumeLayout(true);
    }

    private void AjouterSeuil(Control ligne)
    {
        if (IsHandleCreated)
        {
            Dessin.MettreALEchelle(ligne, this); // Lignes construites en pixels logiques, ajoutées après l'affichage.
        }

        _seuilsSpecifiques.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        ligne.Dock = DockStyle.Fill;
        _seuilsSpecifiques.Controls.Add(ligne, 0, _seuilsSpecifiques.RowStyles.Count - 1);
    }

    /// <summary>« Modèle | Détendeur Mares · 4 équipements | − 200 sorties + | × ».</summary>
    private TableLayoutPanel LigneSeuil(SeuilSpecifiqueDecrit s)
    {
        var equipement = s.Portee == PorteeSeuil.Equipement;
        var couleur = equipement ? Theme.Avertissement : Theme.AccentClair;
        var portee = new Pastille(equipement ? "Équipement" : "Modèle", Theme.Teinte(couleur, 0.14f), couleur) { Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 12, 0) };
        var nom = Ui.Libelle(s.Cle, equipement ? Theme.PoliceCode(10.5f) : Theme.PoliceTitre(10.5f), equipement ? Theme.Accent : Theme.Texte);
        nom.Margin = Padding.Empty;
        var precision = Ui.Libelle(s.Precision, Theme.Police(9f), Theme.TexteSecondaire);
        precision.Margin = Padding.Empty;
        var textes = Ui.Pile(nom, precision);
        textes.Anchor = AnchorStyles.Left;

        var seuil = new SelecteurNombre($"Seuil {s.Cle}", n => $"{n} utilis.") { Width = 210, Nombre = s.Sorties, Anchor = AnchorStyles.Right, Margin = new Padding(8, 0, 6, 0) };
        seuil.Demande += (_, n) =>
        {
            if (Autoriser())
            {
                var voulu = seuil.Nombre + (Math.Sign(n - seuil.Nombre) * ServiceCompteur.Pas);
                if (_compteur.ChangerSeuilSpecifique(s.Portee, s.Cle, voulu) is { Reussi: true, Id: { } retenu })
                {
                    seuil.Nombre = (int)retenu;
                }
            }
        };
        var retirer = new BoutonArrondi($"Supprimer le seuil de {s.Cle}", StyleBouton.Icone, Icones.Croix) { Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        retirer.Click += (_, _) =>
        {
            if (Autoriser() && _compteur.RetirerSeuilSpecifique(s.Portee, s.Cle).Reussi)
            {
                Executer(AfficherSeuilsAsync);
            }
        };

        var ligne = new TableLayoutPanel
        {
            ColumnCount = 4,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(0, 6, 0, 6),
        };
        ligne.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ligne.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ligne.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ligne.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ligne.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        ligne.Controls.Add(portee, 0, 0);
        ligne.Controls.Add(textes, 1, 0);
        ligne.Controls.Add(seuil, 2, 0);
        ligne.Controls.Add(retirer, 3, 0);
        return ligne;
    }

    // ---------------------------------------------------------------- Données

    private CarteSection CarteDonnees()
    {
        var carte = new CarteSection("Données et sauvegardes", Icones.Base, Theme.Retour);
        _etatSauvegarde.Height = 72;
        carte.Ajouter(_etatSauvegarde);

        carte.Ajouter(Ui.LibelleChamp("Dossier des sauvegardes"));
        _dossier.Valeur = _sauvegardes.Dossier;
        _dossier.Champ.ReadOnly = true;
        _dossier.Champ.Font = Theme.PoliceCode(10f);
        _dossier.Champ.AccessibleName = "Dossier des sauvegardes";
        _dossier.Margin = Padding.Empty;
        var parcourir = new BoutonArrondi("Parcourir", StyleBouton.Secondaire) { Margin = new Padding(8, 0, 0, 0), AfficherPastille = false };
        _garde.Lier(parcourir, ActionProtegee.Parametres);
        parcourir.Click += (_, _) => ChoisirDossier();
        var ligneDossier = Ui.Colonnes((_dossier, SizeType.Percent, 100), (parcourir, SizeType.AutoSize, 0));
        ligneDossier.Height = Theme.HauteurChamp;
        carte.Ajouter(ligneDossier);

        var aide = Ui.Libelle("Choisissez une clé USB ou un dossier synchronisé pour garder une copie hors du PC.", Theme.Police(9.75f), Theme.TexteSecondaire);
        aide.Margin = new Padding(0, 8, 0, 18);
        carte.Ajouter(aide);

        var sauvegarder = new BoutonArrondi("Sauvegarder", StyleBouton.Retour) { AutoSize = false, Margin = new Padding(0, 0, 6, 0) };
        sauvegarder.Click += (_, _) => Executer(SauvegarderAsync);
        var restaurer = new BoutonArrondi("Restaurer…", StyleBouton.Secondaire) { AutoSize = false, Margin = new Padding(4, 0, 4, 0), AfficherPastille = false };
        _garde.Lier(restaurer, ActionProtegee.RestaurerSauvegarde);
        restaurer.Click += (_, _) => Executer(RestaurerAsync);
        var exporter = new BoutonArrondi("Exporter CSV", StyleBouton.Secondaire) { AutoSize = false, Margin = new Padding(6, 0, 0, 0) };
        exporter.Click += (_, _) => Executer(ExporterToutAsync);
        var boutons = Ui.Colonnes((sauvegarder, SizeType.Percent, 33.3f), (restaurer, SizeType.Percent, 33.3f), (exporter, SizeType.Percent, 33.3f));
        boutons.Height = Theme.HauteurBouton;
        carte.Ajouter(boutons);
        return carte;
    }

    private void AfficherEtatSauvegarde()
    {
        var detail = $"Automatique à chaque démarrage · {_sauvegardes.NombreAConserver} copies conservées";
        if (_sauvegardes.DerniereSauvegarde() is { } date)
        {
            var jour = date.Date == DateTime.Today ? "aujourd'hui" : date.Date == DateTime.Today.AddDays(-1) ? "hier" : $"le {date.ToString("dd/MM/yyyy", Libelles.Culture)}";
            _etatSauvegarde.Afficher(NiveauResultat.Succes, $"Dernière sauvegarde : {jour} à {date.ToString("HH:mm", Libelles.Culture)}", detail, detailEnCode: false);
        }
        else
        {
            _etatSauvegarde.Afficher(NiveauResultat.Avertissement, "Aucune sauvegarde pour l'instant", detail, detailEnCode: false);
        }
    }

    /// <summary>
    /// Toutes les données en CSV (un fichier par table) dans un nouveau dossier « Export Asprenaut AAAA-MM-JJ »
    /// créé dans le dossier choisi. Libre en consultation, comme une sauvegarde.
    /// </summary>
    private async Task ExporterToutAsync()
    {
        using var dialogue = new FolderBrowserDialog
        {
            Description = "Où créer le dossier d'export ?",
            UseDescriptionForTitle = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ShowNewFolderButton = true,
        };
        if (dialogue.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        var fichiers = await _export.ExporterAsync();
        var dossier = Path.Combine(dialogue.SelectedPath, _export.NomDossier());
        for (var n = 2; Directory.Exists(dossier); n++)
        {
            dossier = Path.Combine(dialogue.SelectedPath, $"{_export.NomDossier()} ({n})"); // Deuxième export du jour.
        }

        try
        {
            Directory.CreateDirectory(dossier);
            foreach (var f in fichiers)
            {
                await File.WriteAllTextAsync(Path.Combine(dossier, f.Nom), f.Contenu, Fichiers.Utf8Excel);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogues.Erreur(FindForm(), "Impossible d'écrire dans ce dossier. Choisissez-en un autre (Documents, clé USB…).");
            return;
        }

        var materiel = fichiers.FirstOrDefault(f => f.Nom == "materiel.csv")?.Lignes ?? 0;
        var sorties = fichiers.FirstOrDefault(f => f.Nom == "sorties.csv")?.Lignes ?? 0;
        if (Dialogues.Confirmer(FindForm(), $"{Libelles.Pluriel(fichiers.Count, "fichier")} créés ({Libelles.Pluriel(materiel, "équipement")}, {Libelles.Pluriel(sorties, "sortie")}).\nOuvrir le dossier ?"))
        {
            Fichiers.Ouvrir(dossier);
        }
    }

    private void ChoisirDossier()
    {
        if (!Autoriser())
        {
            return;
        }

        using var dialogue = new FolderBrowserDialog
        {
            Description = "Dossier des sauvegardes",
            UseDescriptionForTitle = true,
            InitialDirectory = _sauvegardes.Dossier,
            ShowNewFolderButton = true,
        };
        if (dialogue.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        var resultat = _reglages.ChangerDossierSauvegardes(dialogue.SelectedPath);
        _dossier.Valeur = _sauvegardes.Dossier;
        AfficherEtatSauvegarde();
        if (!resultat.Reussi)
        {
            Dialogues.Erreur(FindForm(), resultat.Message);
        }
    }

    private async Task SauvegarderAsync()
    {
        var chemin = await _sauvegardes.SauvegarderAsync();
        AfficherEtatSauvegarde();
        Dialogues.Information(FindForm(), chemin is null ? "Rien à sauvegarder : la base est vide." : $"Sauvegarde faite : {Path.GetFileName(chemin)}");
    }

    private async Task RestaurerAsync()
    {
        if (!_garde.Exiger(FindForm(), ActionProtegee.RestaurerSauvegarde, "Restaurer une sauvegarde"))
        {
            return;
        }

        using var dialogue = new OpenFileDialog
        {
            Title = "Choisir la sauvegarde à restaurer",
            InitialDirectory = _sauvegardes.Dossier,
            Filter = "Sauvegardes Asprenaut (materiel_*.db)|materiel_*.db|Toutes les bases (*.db)|*.db",
            CheckFileExists = true,
        };
        if (dialogue.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        var nom = Path.GetFileName(dialogue.FileName);
        if (!Dialogues.Confirmer(FindForm(), $"Remplacer toutes les données actuelles par la sauvegarde « {nom} » ?\n\nUne copie des données actuelles est faite avant."))
        {
            return;
        }

        var resultat = await _sauvegardes.RestaurerAsync(dialogue.FileName);
        if (!resultat.Reussi)
        {
            Dialogues.Erreur(FindForm(), resultat.Message);
            return;
        }

        Dialogues.Information(FindForm(), resultat.Message);
        _navigation.AllerA<EcranSorties>(); // Les données ont changé : on repart de l'accueil.
    }

    // ---------------------------------------------------------------- Club

    private CarteSection CarteClub()
    {
        var r = _reglages.Actuels;
        var carte = new CarteSection("Club", Icones.Logo, Theme.AccentClair);

        var nom = new ChampSaisie { Valeur = r.NomClub };
        nom.Champ.AccessibleName = "Nom du club";
        var responsable = new ChampSaisie { Valeur = r.ResponsableMateriel, TexteIndicatif = "Nom et téléphone" };
        responsable.Champ.AccessibleName = "Responsable matériel";
        void Enregistrer()
        {
            if (nom.Valeur.Trim() == _reglages.Actuels.NomClub && responsable.Valeur.Trim() == _reglages.Actuels.ResponsableMateriel)
            {
                return;
            }

            // Différé : pas de fenêtre (code gestionnaire, erreur) pendant que le focus quitte le champ.
            BeginInvoke(() =>
            {
                var resultat = Autoriser() ? _reglages.ChangerClub(nom.Valeur, responsable.Valeur) : null;
                if (resultat is not { Reussi: true })
                {
                    nom.Valeur = _reglages.Actuels.NomClub;
                    responsable.Valeur = _reglages.Actuels.ResponsableMateriel;
                }

                if (resultat is { Reussi: false })
                {
                    Dialogues.Information(FindForm(), resultat.Message);
                }
            });
        }

        nom.Champ.Leave += (_, _) => Enregistrer();
        responsable.Champ.Leave += (_, _) => Enregistrer();

        var champs = new TableLayoutPanel { ColumnCount = 2, RowCount = 2, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 18) };
        champs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        champs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        champs.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        champs.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.HauteurChamp));
        var lNom = Ui.LibelleChamp("Nom du club");
        var lResp = Ui.LibelleChamp("Responsable matériel");
        lResp.Margin = new Padding(7, 0, 0, 8);
        nom.Margin = new Padding(0, 0, 7, 0);
        responsable.Margin = new Padding(7, 0, 0, 0);
        nom.Dock = responsable.Dock = DockStyle.Fill;
        champs.Controls.Add(lNom, 0, 0);
        champs.Controls.Add(lResp, 1, 0);
        champs.Controls.Add(nom, 0, 1);
        champs.Controls.Add(responsable, 1, 1);
        carte.Ajouter(champs);

        // Numéro d'affiliation : repris dans l'identification marquée sur le matériel.
        var numero = new ChampSaisie { Valeur = r.NumeroClub, TexteIndicatif = "Ex. : 14 71-0025" };
        numero.Champ.AccessibleName = "Numéro d'affiliation";
        numero.Champ.Leave += (_, _) =>
        {
            if (numero.Valeur.Trim() == _reglages.Actuels.NumeroClub)
            {
                return;
            }

            BeginInvoke(() =>
            {
                if (!Autoriser() || !_reglages.ChangerNumeroClub(numero.Valeur).Reussi)
                {
                    numero.Valeur = _reglages.Actuels.NumeroClub;
                }
            });
        };
        carte.Ajouter(Ui.LibelleChamp("Numéro d'affiliation FFESSM"));
        numero.Height = Theme.HauteurChamp;
        numero.Margin = new Padding(0, 0, 0, 8);
        carte.Ajouter(numero);
        var aide = Ui.Libelle("Repris dans l'identification du matériel : « (Stab) S01 taille XL Asprenaut 14 71-0025 ».", Theme.Police(9.75f), Theme.TexteSecondaire);
        aide.MaximumSize = new Size(560, 0);
        carte.Ajouter(aide);
        return carte;
    }

    // ---------------------------------------------------------------- Types de sortie

    private readonly TableLayoutPanel _listeTypes = new()
    {
        ColumnCount = 1,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        BackColor = Color.Transparent,
        Margin = Padding.Empty,
    };

    /// <summary>
    /// Types de sortie du club et leurs utilisations : ajouter, modifier (nom, pictogramme), utilisations par sortie,
    /// supprimer (jamais utilisé) ou masquer / réafficher (bouton œil).
    /// </summary>
    private CarteSection CarteTypesSortie()
    {
        var carte = new CarteSection("Types de sortie et utilisations", Icones.Sorties, Theme.AccentClair);
        var aide = Ui.Libelle("Nombre d'utilisations ajouté au compteur de chaque équipement sorti. Modifiable sur chaque sortie. "
            + "Un type déjà utilisé ne se supprime pas : il se masque, pour garder l'historique.", Theme.Police(9.75f), Theme.TexteSecondaire);
        aide.MaximumSize = new Size(560, 0);
        aide.Margin = new Padding(0, 0, 0, 8);
        carte.Ajouter(aide);
        _listeTypes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        carte.Ajouter(_listeTypes);

        var ajouter = new BoutonArrondi("Ajouter un type", StyleBouton.Secondaire, Icones.Plus) { Margin = new Padding(0, 12, 0, 0) };
        ajouter.Click += (_, _) => Executer(() => EditerTypeAsync(null));
        carte.Ajouter(ajouter, pleineLargeur: false);
        return carte;
    }

    private async Task AfficherTypesAsync()
    {
        var types = await _typesSortie.ListerAsync();
        if (IsDisposed)
        {
            return;
        }

        _listeTypes.SuspendLayout();
        while (_listeTypes.Controls.Count > 0)
        {
            var c = _listeTypes.Controls[0];
            _listeTypes.Controls.RemoveAt(0);
            c.Dispose();
        }

        _listeTypes.RowStyles.Clear();
        foreach (var t in types)
        {
            var ligne = LigneType(t);
            if (IsHandleCreated)
            {
                Dessin.MettreALEchelle(ligne, this); // Lignes construites en pixels logiques, ajoutées après l'affichage.
            }

            _listeTypes.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            ligne.Dock = DockStyle.Fill;
            _listeTypes.Controls.Add(ligne, 0, _listeTypes.RowStyles.Count - 1);
        }

        _listeTypes.ResumeLayout(true);
    }

    /// <summary>« Piscine · 3 sorties | − 1 utilisation + | Modifier | 🗑 ou 👁 ».</summary>
    private TableLayoutPanel LigneType(TypeDeSortie t)
    {
        var nom = Ui.Libelle(t.Nom, Theme.PoliceTitre(10.5f), t.Actif ? Theme.Texte : Theme.TexteSecondaire);
        nom.Margin = Padding.Empty;
        var precision = (t.NbSorties == 0 ? "Aucune sortie" : Libelles.Pluriel(t.NbSorties, "sortie")) + (t.Actif ? "" : " · masqué");
        var detail = Ui.Libelle(precision, Theme.Police(9f), Theme.TexteSecondaire);
        detail.Margin = Padding.Empty;
        var textes = Ui.Pile(nom, detail);
        textes.Anchor = AnchorStyles.Left;

        var utilisations = new SelecteurNombre($"Utilisations pour {t.Nom}", n => Libelles.Pluriel(n, "utilisation"))
        {
            Width = 226,
            Nombre = t.Utilisations,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(8, 0, 4, 0),
        };
        utilisations.Demande += (_, n) =>
        {
            if (Autoriser())
            {
                Executer(async () =>
                {
                    if (await _typesSortie.ChangerUtilisationsAsync(t.Id, n) is { Reussi: true, Id: { } retenu })
                    {
                        utilisations.Nombre = (int)retenu;
                        t.Utilisations = (int)retenu;
                    }
                });
            }
        };

        var modifier = new BoutonArrondi("Modifier", StyleBouton.Secondaire) { Height = Theme.HauteurCliquable, Anchor = AnchorStyles.Right, Margin = new Padding(4, 0, 4, 0), AccessibleName = $"Modifier le type {t.Nom}" };
        modifier.Click += (_, _) => Executer(() => EditerTypeAsync(t));

        // Jamais utilisé : corbeille (suppression). Déjà utilisé : œil, pour masquer ou réafficher sans perdre l'historique.
        var utilise = t.NbSorties > 0;
        var action = utilise
            ? new BoutonArrondi(t.Actif ? $"Masquer {t.Nom}" : $"Réafficher {t.Nom}", StyleBouton.Icone, t.Actif ? Icones.OeilBarre : Icones.Oeil)
            : new BoutonArrondi($"Supprimer {t.Nom}", StyleBouton.Icone, Icones.Corbeille) { CouleurIcone = Theme.Erreur };
        action.Anchor = AnchorStyles.Right;
        action.Margin = Padding.Empty;
        action.Click += (_, _) => Executer(() => utilise ? BasculerTypeAsync(t) : SupprimerTypeAsync(t));

        var ligne = new TableLayoutPanel
        {
            ColumnCount = 4,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(0, 6, 0, 6),
        };
        ligne.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ligne.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ligne.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ligne.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ligne.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        ligne.Controls.Add(textes, 0, 0);
        ligne.Controls.Add(utilisations, 1, 0);
        ligne.Controls.Add(modifier, 2, 0);
        ligne.Controls.Add(action, 3, 0);
        return ligne;
    }

    /// <summary>Bouton œil : masque un type utilisé (plus proposé, historique gardé) ou le réaffiche.</summary>
    private async Task BasculerTypeAsync(TypeDeSortie type)
    {
        if (!Autoriser())
        {
            return;
        }

        var resultat = await _typesSortie.BasculerMasqueAsync(type.Id);
        if (!resultat.Reussi)
        {
            Dialogues.Erreur(FindForm(), resultat.Message);
            return;
        }

        await AfficherTypesAsync();
    }

    /// <summary>Fenêtre d'ajout (<paramref name="type"/> null) ou de modification d'un type de sortie.</summary>
    private async Task EditerTypeAsync(TypeDeSortie? type)
    {
        if (!Autoriser())
        {
            return;
        }

        var nom = new ChampSaisie { Valeur = type?.Nom ?? "", TexteIndicatif = "Ex. : Sortie bateau" };
        nom.Champ.AccessibleName = "Nom du type de sortie";
        nom.Champ.MaxLength = TypeDeSortie.LongueurMaxNom;
        var pictogramme = new ListeDeroulante { AccessibleName = "Pictogramme" };
        foreach (var famille in Enum.GetValues<TypeSortie>())
        {
            pictogramme.Items.Add(new Choix<TypeSortie>(famille, Icones.NomPictogramme(famille)));
        }

        pictogramme.SelectedItem = pictogramme.Items.OfType<Choix<TypeSortie>>().First(c => c.Valeur == (type?.Pictogramme ?? TypeSortie.Autre));

        // Proposé ou masqué : bouton œil de la liste. Utilisations : sélecteur de la liste (nouveau type : selon le pictogramme).
        using var fenetre = new FenetreFormulaire(type is null ? "Ajouter un type de sortie" : $"Modifier « {type.Nom} »", type is null ? "Ajouter" : "Enregistrer", () =>
        {
            var famille = (pictogramme.SelectedItem as Choix<TypeSortie>)?.Valeur ?? TypeSortie.Autre;
            return type is null
                ? _typesSortie.CreerAsync(nom.Valeur, famille)
                : _typesSortie.ModifierAsync(type.Id, nom.Valeur, famille, type.Actif);
        });
        fenetre.DeuxChamps("Nom", nom, "Pictogramme", pictogramme);

        fenetre.Shown += (_, _) => nom.Champ.Focus();
        if (fenetre.ShowDialog(FindForm()) == DialogResult.OK)
        {
            await AfficherTypesAsync();
        }
    }

    private async Task SupprimerTypeAsync(TypeDeSortie type)
    {
        if (!Autoriser() || !Dialogues.Confirmer(FindForm(), $"Supprimer le type « {type.Nom} » ?"))
        {
            return;
        }

        var resultat = await _typesSortie.SupprimerAsync(type.Id);
        if (!resultat.Reussi)
        {
            Dialogues.Erreur(FindForm(), resultat.Message);
            return;
        }

        await AfficherTypesAsync();
    }

    // ---------------------------------------------------------------- À propos

    private CarteSection CarteAPropos()
    {
        var carte = new CarteSection("À propos", null, Theme.AccentClair);
        var infos = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 8) };
        infos.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        infos.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void Info(string terme, string valeur, Font? police = null)
        {
            infos.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var t = Ui.Libelle(terme, Theme.Police(10.5f), Theme.TexteSecondaire);
            t.Margin = new Padding(0, 0, 20, 10);
            var v = Ui.Libelle(valeur, police ?? Theme.Police(10.5f), Theme.TexteDoux);
            v.Margin = new Padding(0, 0, 0, 10);
            infos.Controls.Add(t, 0, infos.RowStyles.Count - 1);
            infos.Controls.Add(v, 1, infos.RowStyles.Count - 1);
        }

        Info("Version", Application.ProductVersion.Split('+')[0]);
        Info("Base de données", _parametres.CheminBase, Theme.PoliceCode(9.75f));
        Info("Framework", ".NET 10 · WinForms");
        carte.Ajouter(infos);

        var journal = new BoutonArrondi("Ouvrir le journal des actions", StyleBouton.Lien) { Margin = Padding.Empty, Height = Theme.HauteurCliquable };
        journal.Click += (_, _) => _navigation.AllerA<EcranHistorique>();
        carte.Ajouter(journal, pleineLargeur: false);
        return carte;
    }

    /// <summary>Paramètres : action protégée (le code est demandé si le mode gestion n'est pas ouvert).</summary>
    private bool Autoriser() => _garde.Exiger(FindForm(), ActionProtegee.Parametres, "Modifier les paramètres");

    /// <summary>Interrupteur de réglage : en mode consultation sans code accepté, il revient à sa position.</summary>
    private LigneReglage Interrupteur(string libelle, string aide, bool valeur, Action<bool> changement, bool filet = true)
    {
        var bouton = new Controles.Interrupteur(libelle) { Checked = valeur };
        var retour = false;
        bouton.CheckedChanged += (_, _) =>
        {
            if (retour)
            {
                return;
            }

            if (!Autoriser())
            {
                retour = true;
                bouton.Checked = !bouton.Checked;
                retour = false;
                return;
            }

            changement(bouton.Checked);
        };
        return new LigneReglage(libelle, aide, bouton, filet);
    }

    /// <summary>Liste de réglage : en mode consultation sans code accepté, elle revient au choix enregistré.</summary>
    private void ProtegerListe(ListeDeroulante liste, Func<int> indexActuel, Action<object?> changement)
    {
        var retour = false;
        liste.SelectedIndexChanged += (_, _) =>
        {
            if (retour)
            {
                return;
            }

            if (!Autoriser())
            {
                retour = true;
                liste.SelectedIndex = Math.Max(0, indexActuel());
                retour = false;
                return;
            }

            changement(liste.SelectedItem);
        };
    }

    /// <summary>
    /// Choix d'un thème : une mini-fenêtre dessinée avec les couleurs de ce thème (menu, cartes, bouton),
    /// son nom et une coche s'il est actif. Le cadre de sélection suit le thème actuel.
    /// </summary>
    private sealed class ApercuTheme : Button
    {
        public const int Hauteur = 176;
        private bool _survol;

        public ApercuTheme(ModeAffichage mode)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Mode = mode;
            Text = mode == ModeAffichage.Clair ? "Clair" : "Sombre";
            AccessibleRole = AccessibleRole.RadioButton;
            AccessibleName = $"Thème {Text.ToLower(Libelles.Culture)}";
            AccessibleDescription = Actif ? "Thème actuel" : null;
        }

        public ModeAffichage Mode { get; }

        private bool Actif => Mode == Theme.Mode;

        protected override void OnMouseEnter(EventArgs e)
        {
            _survol = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _survol = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            float E(float v) => Dessin.Echelle(this, v);
            Dessin.EffacerAvecParent(this, g);
            Dessin.Qualite(g);
            var p = Theme.PaletteDe(Mode);

            // Cadre : accent du thème actuel si choisi, bordure sinon.
            var hLegende = E(44);
            var cadre = new RectangleF(E(1), E(1), Width - E(3), Height - hLegende - E(4));
            using (var chemin = Dessin.Arrondi(cadre, E(16)))
            {
                using var fond = new SolidBrush(p.Fond);
                g.FillPath(fond, chemin);
                var couleur = Actif ? Theme.Accent : _survol ? Theme.Sur(Theme.Surface, Theme.Accent, 0.5f) : Theme.BordureForte;
                using var bord = new Pen(couleur, E(Actif ? 2.5f : 1.2f));
                g.DrawPath(bord, chemin);
            }

            // Mini-fenêtre : menu à gauche, deux cartes, un bouton.
            var interieur = RectangleF.Inflate(cadre, -E(10), -E(10));
            using var decoupe = Dessin.Arrondi(interieur, E(10));
            g.SetClip(decoupe);
            using (var menu = new SolidBrush(p.FondMenu))
            {
                g.FillRectangle(menu, interieur.X, interieur.Y, interieur.Width * 0.27f, interieur.Height);
            }

            Barre(g, p.Accent, interieur.X + E(8), interieur.Y + E(12), interieur.Width * 0.27f - E(16), E(8));
            for (var i = 0; i < 3; i++)
            {
                Barre(g, p.TexteSecondaire, interieur.X + E(8), interieur.Y + E(30 + (i * 15)), (interieur.Width * 0.27f) - E(24), E(5));
            }

            var x = interieur.X + (interieur.Width * 0.27f) + E(10);
            var largeurCarte = (interieur.Right - x - E(18)) / 2;
            Barre(g, p.Texte, x, interieur.Y + E(10), largeurCarte, E(8));
            for (var i = 0; i < 2; i++)
            {
                var c = new RectangleF(x + (i * (largeurCarte + E(8))), interieur.Y + E(28), largeurCarte, interieur.Bottom - interieur.Y - E(38));
                using (var chemin = Dessin.Arrondi(c, E(7)))
                {
                    using var surface = new SolidBrush(p.Surface);
                    g.FillPath(surface, chemin);
                    using var bord = new Pen(p.Bordure, E(1));
                    g.DrawPath(bord, chemin);
                }

                Barre(g, p.Texte, c.X + E(8), c.Y + E(10), c.Width * 0.6f, E(6));
                Barre(g, p.TexteSecondaire, c.X + E(8), c.Y + E(22), c.Width * 0.75f, E(5));
                Barre(g, i == 0 ? p.Depart : p.Retour, c.X + E(8), c.Y + E(33), c.Width * 0.4f, E(5));
                Barre(g, p.Accent, c.X + E(8), c.Bottom - E(18), c.Width - E(16), E(10));
            }

            g.ResetClip();

            // Légende : pictogramme, nom, coche si actif.
            var y = cadre.Bottom + E(6);
            var t = E(20);
            Icones.Dessiner(g, Mode == ModeAffichage.Clair ? Icones.Soleil : Icones.Lune, new RectangleF(E(4), y + ((hLegende - t) / 2), t, t), Actif ? Theme.Accent : Theme.TexteSecondaireClair);
            Dessin.Texte(g, Text, Actif ? Theme.PoliceTitre(10.5f) : Theme.Police(10.5f), new Rectangle((int)E(32), (int)y, Width - (int)E(70), (int)hLegende), Theme.Texte);
            if (Actif)
            {
                var d = E(24);
                var rond = new RectangleF(Width - d - E(4), y + ((hLegende - d) / 2), d, d);
                using (var pinceau = new SolidBrush(Theme.Accent))
                {
                    g.FillEllipse(pinceau, rond);
                }

                Icones.Dessiner(g, Icones.Coche, RectangleF.Inflate(rond, -E(5), -E(5)), Theme.TexteSurAccent, 2.4f);
            }

            if (Focused && ShowFocusCues)
            {
                using var chemin = Dessin.Arrondi(RectangleF.Inflate(cadre, -E(4), -E(4)), E(12));
                using var focus = new Pen(Theme.Accent, E(1.5f)) { DashPattern = [3f, 2f] };
                g.DrawPath(focus, chemin);
            }
        }

        private static void Barre(Graphics g, Color couleur, float x, float y, float largeur, float hauteur)
        {
            if (largeur <= 0)
            {
                return;
            }

            using var chemin = Dessin.Arrondi(new RectangleF(x, y, largeur, hauteur), hauteur / 2);
            using var pinceau = new SolidBrush(couleur);
            g.FillPath(pinceau, chemin);
        }
    }

    /// <summary>Encadré à bordure pointillée (zone d'essai du pistolet).</summary>
    private sealed class CadrePointille : TableLayoutPanel
    {
        public CadrePointille()
        {
            ColumnCount = 1;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Theme.Sur(Theme.Surface, Theme.Fond, 0.5f);
            Padding = new Padding(16);
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void Ajouter(Control c)
        {
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            c.Dock = DockStyle.Fill;
            Controls.Add(c, 0, RowStyles.Count - 1);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            Dessin.EffacerAvecParent(this, g);
            Dessin.Qualite(g);
            using var chemin = Dessin.Arrondi(new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), Dessin.Echelle(this, 18));
            using var fond = new SolidBrush(BackColor);
            g.FillPath(fond, chemin);
            using var bord = new Pen(Theme.Sur(BackColor, Theme.Accent, 0.35f), Dessin.Echelle(this, 1)) { DashPattern = [4f, 3f] };
            g.DrawPath(bord, chemin);
        }
    }
}
