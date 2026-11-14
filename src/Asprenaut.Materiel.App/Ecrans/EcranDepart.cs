using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>Étape 1 : scan du matériel qui part.</summary>
public sealed class EcranDepart : EcranScanBase
{
    private readonly ServiceSortie _sorties;
    private readonly ServiceScan _scan;
    private readonly ServiceInventaire _inventaire;
    private readonly IHorloge _horloge;
    private readonly Pastille _compte = Compteur(Theme.Depart, Theme.DepartClair);
    private readonly ListeMateriel _liste = new(StyleListe.Depart, "Rien n'est encore scanné. Scannez la première étiquette.");
    private readonly BoutonArrondi _valider = new("Valider le départ", StyleBouton.Depart, Icones.Coche, iconeApres: true);
    private readonly FiltreCategories _filtre = new(Theme.AccentClair) { Margin = new Padding(0, 0, 0, 10) };
    private readonly ServiceMoniteurs _moniteurs;
    private readonly ServiceTypesSortie _typesSortie;
    private readonly BoutonArrondi _modifier = new("Modifier la sortie", StyleBouton.Secondaire, Icones.Crayon) { Height = Theme.HauteurCliquable, Margin = new Padding(10, 0, 0, 0) };
    private readonly BoutonArrondi _supprimer = new("Supprimer", StyleBouton.Secondaire, Icones.Corbeille) { Height = Theme.HauteurCliquable, Margin = new Padding(10, 0, 0, 0), Visible = false };
    private IReadOnlyList<LigneSortie> _lignes = [];
    private StatutSortie _statut;
    private string _intitule = "";

    private readonly GardeGestion _garde;

    // Sortie nominative : panneau « À qui est ce détendeur ? » à la place de la zone de scan, vue « Par adhérent ».
    private readonly ServiceAdherents _adherents;
    private readonly PanneauAdherent _panneau = new() { Visible = false, Dock = DockStyle.Top }; // Hauteur d'après son contenu.
    private readonly Bascule _vue = new("Par équipement", "Par adhérent") { Margin = new Padding(0, 0, 0, 10) };
    private readonly ListeMateriel _listePersonnes = new(StyleListe.Personne, "Aucun équipement attribué pour l'instant.") { Visible = false };
    private readonly TableLayoutPanel _carteListe;
    private bool _nominatif;
    private LigneSortie? _enAttente;

    public EcranDepart(ServiceSortie sorties, ServiceScan scan, ServiceInventaire inventaire, ServiceMoniteurs moniteurs, IHorloge horloge, ServiceReglages reglages, INavigation navigation, GardeGestion garde, ServiceTypesSortie typesSortie, ServiceManuels manuels,
        ServiceAdherents adherents)
        : base(navigation, reglages, manuels, ModeScan.Depart)
    {
        _adherents = adherents;
        _garde = garde;
        _garde.Lier(_supprimer, ActionProtegee.SupprimerSortie);
        _moniteurs = moniteurs;
        _typesSortie = typesSortie;
        _modifier.Click += (_, _) => Executer(ModifierAsync);
        _supprimer.Click += (_, _) => Executer(SupprimerAsync);
        _supprimer.AccessibleDescription = "Possible seulement quand aucun matériel n'est scanné";
        _inventaire = inventaire;
        _sorties = sorties;
        _scan = scan;
        _horloge = horloge;
        _valider.AutoSize = false;
        _valider.Font = Theme.PoliceGrasse(12f);
        _valider.Margin = new Padding(0, 12, 0, 0);
        _valider.Click += (_, _) => Executer(ValiderAsync);
        _filtre.Changement += (_, _) => AfficherListe();
        _vue.Changement += (_, _) => AfficherListe();
        _panneau.Choisi += (_, id) => Executer(() => AttribuerAsync(id));
        _panneau.ScanRefuse += (_, code) => ZoneScan.AfficherMessage(NiveauResultat.Erreur,
            $"Choisissez d'abord l'adhérent qui prend {_panneau.Code}. Le scan de {code} n'a pas été pris en compte.", code);
        _panneau.AnnulerScan += (_, _) =>
        {
            if (_enAttente is { } ligne)
            {
                Executer(() => RetirerAsync(new ElementMateriel(ligne.EquipementId, ligne.Code, ligne.Libelle)));
            }
        };

        // Zone de scan, ou panneau « À qui est… ? » au même endroit (le bandeau des messages reste dessous).
        var zone = new Panel { Margin = Padding.Empty, BackColor = Theme.Fond };
        ZoneScan.Dock = DockStyle.Fill;
        zone.Controls.Add(ZoneScan);
        zone.Controls.Add(_panneau);
        var gauche = Ui.Colonne(
            (zone, SizeType.Percent, 100),
            (ZoneScan.Bandeau, SizeType.Absolute, 76 + 16));
        ZoneScan.Bandeau.Margin = new Padding(0, 16, 0, 0);
        gauche.Margin = new Padding(0, 0, 22, 0);

        var carteListe = new Carte();
        var listes = new Panel { Margin = Padding.Empty, BackColor = Theme.Surface };
        _liste.Dock = _listePersonnes.Dock = DockStyle.Fill;
        listes.Controls.Add(_liste);
        listes.Controls.Add(_listePersonnes);
        _carteListe = Ui.Colonne(
            (EnteteListe(Ui.TitreCarte("Matériel de la sortie"), _compte), SizeType.Absolute, HauteurEnteteListe),
            (_vue, SizeType.Absolute, 0), // 44 + 10 pour une sortie nominative.
            (_filtre, SizeType.Absolute, 44 + 10),
            (listes, SizeType.Percent, 100),
            (_valider, SizeType.Absolute, 56 + 12));
        _vue.Visible = false;
        carteListe.Controls.Add(_carteListe);

        // Rangée de droite à gauche : « Modifier la sortie » tout à droite, « Supprimer » à sa gauche (sortie vide seulement).
        Composer(Ui.Colonnes((gauche, SizeType.Percent, 58), (carteListe, SizeType.Percent, 42)), _modifier, _supprimer);
    }

    public override string Titre => "Départ du matériel";

    protected override async Task RafraichirAsync()
    {
        var resume = await _sorties.ObtenirResumeAsync(SortieId) ?? throw new InvalidOperationException("Sortie introuvable.");
        var lignes = await _sorties.ListerMaterielAsync(SortieId);
        _statut = resume.Statut;
        _intitule = resume.Intitule;
        AfficherSortie(resume);

        var enPreparation = _statut == StatutSortie.EnPreparation;
        _modifier.Visible = enPreparation;
        _supprimer.Visible = enPreparation && lignes.Count == 0;
        _compte.Text = lignes.Count.ToString(Libelles.Culture);
        _valider.Text = enPreparation ? "Valider le départ" : "Revenir au retour";
        _valider.Icone = enPreparation ? Icones.Coche : Icones.Fleche;
        _valider.Style = enPreparation ? StyleBouton.Depart : StyleBouton.Retour;
        _valider.Visible = _statut != StatutSortie.Cloturee;

        // Sortie nominative : un équipement sans adhérent bloque le scan suivant et la validation.
        _nominatif = resume.Nominatif;
        var etaitEnAttente = _enAttente is not null;
        _enAttente = ServiceSortie.AAttribuer(_nominatif, lignes);
        if (_enAttente is { } attente)
        {
            var tous = await _adherents.ListerPourDepartAsync();
            if (IsDisposed)
            {
                return;
            }

            _panneau.Afficher(attente, lignes, resume.UnParCategorie, ServiceSortie.AdherentsRecents(lignes), tous, _adherents.AujourdHui);
            _valider.Enabled = false;
            _valider.Text = $"Attribuez {attente.Code} avant de valider";
            _valider.Icone = Icones.Cadenas;
        }
        else
        {
            _valider.Enabled = true;
        }

        _panneau.Visible = _enAttente is not null;
        ZoneScan.Visible = _enAttente is null;
        var vueVisible = _nominatif;
        if (_vue.Visible != vueVisible)
        {
            _vue.Visible = vueVisible;
            _carteListe.RowStyles[1].Height = vueVisible ? Dessin.Echelle(this, 44 + 10) : 0;
        }

        await ChargerManuelsAsync();
        _lignes = lignes;
        _filtre.Afficher(lignes.Count, Categories(lignes, l => true));
        AfficherListe();

        // Le focus suit : recherche d'adhérent quand un équipement attend, sinon le champ de scan (le pistolet y tape).
        if (etaitEnAttente != (_enAttente is not null) && IsHandleCreated)
        {
            DonnerFocus();
        }
    }

    protected override void DonnerFocus()
    {
        if (_enAttente is not null)
        {
            _panneau.PrendreFocus();
        }
        else
        {
            ZoneScan.PrendreFocus();
        }
    }

    /// <summary>L'équipement en attente est pris par cet adhérent ; on revient au champ de scan.</summary>
    private async Task AttribuerAsync(long adherentId)
    {
        if (_enAttente is not { } ligne)
        {
            return;
        }

        var resultat = await _sorties.AttribuerAdherentAsync(SortieId, ligne.EquipementId, adherentId);
        ZoneScan.AfficherMessage(resultat.Reussi ? NiveauResultat.Succes : NiveauResultat.Erreur, resultat.Message, ligne.Code);
        if (!resultat.Reussi)
        {
            _panneau.Signaler(resultat.Message);
        }

        await RafraichirAsync();
    }

    /// <summary>Catégories présentes dans la sortie (ordre alphabétique), avec le nombre d'équipements retenus.</summary>
    internal static IEnumerable<(string, int)> Categories(IEnumerable<LigneSortie> lignes, Func<LigneSortie, bool> compte) =>
        lignes.GroupBy(l => l.Categorie)
            .OrderBy(g => g.Key, StringComparer.Create(Libelles.Culture, true))
            .Select(g => (g.Key, g.Count(compte)));

    private void AfficherListe()
    {
        var enPreparation = _statut == StatutSortie.EnPreparation;
        var aujourdHui = _horloge.AujourdHui;

        // Sortie nominative, « Par adhérent » : une ligne par personne avec ses codes.
        var parAdherent = _nominatif && _vue.SelectedIndex == 1;
        _listePersonnes.Visible = parAdherent;
        _liste.Visible = !parAdherent;
        if (parAdherent)
        {
            _listePersonnes.Afficher(_lignes
                .Where(l => _filtre.Accepte(l.Categorie) && l.AdherentId is not null)
                .GroupBy(l => l.AdherentId!.Value)
                .OrderByDescending(g => g.Max(l => l.ScanneDepartLe))
                .Select(g => new ElementMateriel(-g.Key, string.Join(" · ", g.OrderBy(l => l.ScanneDepartLe).Select(l => l.Code)),
                    g.First().AdherentNom ?? "", Alerte: Libelles.Pluriel(g.Count(), "équipement"), CouleurAlerte: Theme.TexteSecondaireClair)));
            return;
        }

        _liste.DefinirVide(_filtre.Selection is null
            ? "Rien n'est encore scanné. Scannez la première étiquette."
            : "Rien dans cette catégorie.");
        _liste.Afficher(
            _lignes
                .Where(l => _filtre.Accepte(l.Categorie))
                .OrderByDescending(l => l.ScanneDepartLe)
                .Select(l => new ElementMateriel(
                    l.EquipementId,
                    l.Code,
                    (string.IsNullOrWhiteSpace(l.Taille) ? l.Libelle : $"{l.Libelle} · {l.Taille}") + (l.Nitrox ? " · nitrox" : ""),
                    l.Categorie,
                    Alerte: l.RevisionDepassee(aujourdHui) ? $"Révision dépassée ({Libelles.Date(l.EcheanceDepassee(aujourdHui)!.Value)})" : null,
                    CouleurAlerte: Theme.Avertissement,
                    ManuelId: ManuelDe(l.EquipementId),
                    Adherent: _nominatif ? l.AdherentNom : null,
                    EnAttente: _enAttente?.LigneId == l.LigneId)),
            enPreparation ? "Retirer" : null,
            Icones.Croix,
            element => Executer(() => RetirerAsync(element)),
            OuvrirManuel);
    }

    protected override async Task TraiterCodeAsync(string code)
    {
        var resultat = await _scan.ScannerDepartAsync(SortieId, code);
        ZoneScan.AfficherResultat(resultat);

        // Code inconnu : on propose de créer l'équipement (code pré-rempli), puis il part directement avec la sortie.
        if (resultat.Action == ActionProposee.CreerEquipement && Dialogues.Confirmer(FindForm(), resultat.Message)
            && _garde.Exiger(FindForm(), ActionProtegee.Materiel, "Ajouter un équipement")
            && await FormulaireEquipement.OuvrirAsync(FindForm(), _inventaire, resultat.Code, type: TypeLu) is { } cree && !IsDisposed)
        {
            ZoneScan.AfficherResultat(await _scan.ScannerDepartAsync(SortieId, cree));
        }

        await RafraichirAsync();
    }

    private async Task RetirerAsync(ElementMateriel element)
    {
        var resultat = await _scan.RetirerDuDepartAsync(SortieId, element.EquipementId);
        ZoneScan.AfficherMessage(resultat.Reussi ? NiveauResultat.Succes : NiveauResultat.Erreur, resultat.Message, element.Code);
        await RafraichirAsync();
    }

    /// <summary>Modifier date, type, intitulé, responsable, lieu, notes : fenêtre préremplie, validée par ServiceSortie.</summary>
    private async Task ModifierAsync()
    {
        if (await _sorties.ObtenirAsync(SortieId) is not { } s || IsDisposed)
        {
            return;
        }

        var date = new ChampDate { Date = s.DateSortie };
        // Types proposés, plus celui de la sortie s'il a été masqué depuis.
        var type = new ListeDeroulante { AccessibleName = "Type de sortie" };
        var types = await _typesSortie.ListerProposesAsync(s.TypeId);
        foreach (var t in types)
        {
            type.Items.Add(new Choix<long>(t.Id, t.Nom));
        }

        type.SelectedIndex = Math.Max(0, types.ToList().FindIndex(t => t.Id == s.TypeId));
        var intitule = new ChampSaisie { Valeur = s.Intitule };
        intitule.Champ.AccessibleName = "Intitulé";
        var lieu = new ChampSaisie { Valeur = s.Lieu, TexteIndicatif = "Nom de la piscine, du site…" };
        lieu.Champ.AccessibleName = "Lieu";
        var notes = new ChampSaisie(multiligne: true) { Valeur = s.Notes, TexteIndicatif = "Groupe N1, 8 élèves…" };
        notes.Champ.AccessibleName = "Notes";

        // Moniteurs et classes actifs, plus le responsable actuel s'il est devenu inactif.
        var responsable = new ChoixResponsable();
        responsable.Remplir((await _moniteurs.ListerTousAsync()).Where(m => m.Actif || m.Id == s.MoniteurId).ToList(), s.MoniteurId);

        // Utilisations comptées : celles de la sortie ; un autre type reprend sa valeur par défaut.
        var utilisations = new SelecteurNombre("Utilisations comptées pour cette sortie", n => Libelles.Pluriel(n, "utilisation")) { Nombre = s.Utilisations };
        _garde.Lier(utilisations, ActionProtegee.CompteurSortie);
        utilisations.Demande += (_, n) =>
        {
            if (_garde.Exiger(utilisations.FindForm(), ActionProtegee.CompteurSortie, "Changer les utilisations"))
            {
                utilisations.Nombre = Math.Clamp(n, TypeDeSortie.UtilisationsMin, TypeDeSortie.UtilisationsMax);
            }
        };
        // Sortie nominative (la sienne, ou celle du nouveau type) : un équipement par catégorie et par adhérent.
        var unParCategorie = new OptionUnParCategorie(_garde) { Actif = s.UnParCategorie, Visible = s.Nominatif, Margin = new Padding(0, 0, 0, 14) };
        type.SelectedIndexChanged += (_, _) =>
        {
            if (type.SelectedItem is Choix<long> c && types.FirstOrDefault(t => t.Id == c.Valeur) is { } choisi)
            {
                utilisations.Nombre = choisi.Id == s.TypeId ? s.Utilisations : choisi.Utilisations;
                unParCategorie.Visible = choisi.Id == s.TypeId ? s.Nominatif : choisi.Nominatif;
            }
        };

        using var fenetre = new FenetreFormulaire("Modifier la sortie", "Enregistrer", () =>
            date.Date is not { } d
                ? Task.FromResult(ResultatOperation.Echec("Date invalide : écrivez-la comme 08/11/2026."))
                : _sorties.ModifierAsync(SortieId, new NouvelleSortie(
                    d,
                    s.Type,
                    intitule.Valeur,
                    responsable.ResponsableId,
                    lieu.Valeur,
                    notes.Valeur,
                    (type.SelectedItem as Choix<long>)?.Valeur,
                    utilisations.Nombre,
                    unParCategorie.Actif)));
        fenetre.DeuxChamps("Date", date, "Type", type);
        fenetre.DeuxChamps("Intitulé", intitule, "Utilisations (compteur)", utilisations);
        fenetre.Ajouter(unParCategorie);
        fenetre.Champ("Responsable", responsable); // Toute la largeur : « Moniteur | Classe » puis la liste.
        fenetre.Champ("Lieu", lieu);
        fenetre.Champ("Notes (facultatif)", notes, 96);

        if (fenetre.ShowDialog(FindForm()) == DialogResult.OK && !IsDisposed)
        {
            await RafraichirAsync();
            ZoneScan.AfficherMessage(NiveauResultat.Succes, "Sortie modifiée.");
        }

        ZoneScan.PrendreFocus();
    }

    /// <summary>Supprimer une sortie créée par erreur (bouton visible seulement si aucun matériel n'est scanné).</summary>
    private async Task SupprimerAsync()
    {
        if (!_garde.Exiger(FindForm(), ActionProtegee.SupprimerSortie, "Supprimer une sortie")
            || !Dialogues.Confirmer(FindForm(), $"Supprimer la sortie « {_intitule} » ?\n\nElle ne contient aucun matériel. Cette action est définitive."))
        {
            ZoneScan.PrendreFocus();
            return;
        }

        var resultat = await _sorties.SupprimerAsync(SortieId);
        if (!resultat.Reussi)
        {
            ZoneScan.AfficherMessage(NiveauResultat.Erreur, resultat.Message);
            await RafraichirAsync();
            return;
        }

        Navigation.AllerA<EcranSorties>();
    }

    private async Task ValiderAsync()
    {
        if (_statut != StatutSortie.EnPreparation)
        {
            Navigation.AllerA<EcranRetour>(SortieId);
            return;
        }

        var resultat = await _sorties.ValiderDepartAsync(SortieId);
        if (!resultat.Reussi)
        {
            ZoneScan.AfficherMessage(NiveauResultat.Erreur, resultat.Message);
            return;
        }

        Dialogues.Information(FindForm(), $"{resultat.Message}\nBonne plongée ! Au retour, ouvrez la sortie pour scanner le matériel.");
        Navigation.AllerA<EcranSorties>();
    }
}
