using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Accueil (maquette « Sorties ») : onglets « En cours » (bandeau, rappel de sauvegarde, cartes des sorties en cours) et
/// « Clôturées » (recherche, période, responsable, puces par type, bilan, tableau trié par colonne, pages de 12).
/// </summary>
public sealed class EcranSorties : EcranBase
{
    private readonly ServiceSortie _sorties;
    private readonly IHorloge _horloge;
    private readonly INavigation _navigation;
    private readonly ServiceReglages _reglages;

    // Onglets et bouton « Nouvelle sortie » du haut (onglet Clôturées ; sur En cours, il est dans le bandeau).
    private readonly Bascule _onglets = new("En cours", "Clôturées", "Modèles") { AccessibleName = "Sorties" };
    private readonly BoutonArrondi _nouvelleHaut = new("Nouvelle sortie", StyleBouton.Principal, Icones.Plus) { Margin = Padding.Empty, Visible = false };
    private readonly Control _panneauEnCours;
    private readonly Control _panneauCloturees;
    private readonly Control _panneauModeles;
    private readonly ServiceModelesSortie _modeles;
    private readonly ServiceTypesSortie _typesSortie;
    private readonly ServiceMoniteurs _moniteurs;
    private readonly GardeGestion _garde;

    // Démarrage rapide (onglet En cours) et onglet Modèles.
    private readonly GrilleAdaptative _rapides = new() { LargeurMin = 270, HauteurElement = 172, Ecart = 14, ColonnesMax = ServiceModelesSortie.NombreDemarrageRapide, RangeesVisibles = 1, Margin = new Padding(0, 0, 0, 26) };
    private readonly GrilleAdaptative _grilleModeles = new() { LargeurMin = 380, HauteurElement = 318, Ecart = 14, Margin = Padding.Empty };

    // Onglet En cours.
    private readonly BandeauMessage _rappelSauvegarde = new() { Height = 76, Margin = Padding.Empty };
    private readonly TableLayoutPanel _ligneRappel;
    private readonly Label _date = Ui.Libelle("", Theme.PoliceTitre(9.75f), Theme.AccentClair);
    private readonly Label _resume = Ui.Libelle("", Theme.Police(11.25f), Theme.TexteDoux);
    private readonly GrilleAdaptative _cartes = new() { LargeurMin = 400, HauteurElement = 196, Ecart = 18, Margin = Padding.Empty };

    // Onglet Clôturées : filtres, tableau, pages.
    private readonly Label _totaux = Ui.Libelle("", Theme.Police(10.5f), Theme.TexteSecondaire);
    private readonly ChampSaisie _recherche = new() { TexteIndicatif = "Sortie, lieu, responsable", Icone = Icones.Recherche };
    private readonly ListeDeroulante _periode = new() { AccessibleName = "Période" };
    private readonly ListeDeroulante _responsable = new() { AccessibleName = "Responsable" };
    private readonly BoutonArrondi _effacer = new("Effacer les filtres", StyleBouton.Lien, Icones.Croix) { Margin = Padding.Empty, Visible = false };
    private readonly FiltreCategories _types = new(Theme.AccentClair) { LibelleTout = "Toutes", Unite = "sortie", Margin = Padding.Empty };
    private readonly Bascule _bilan = new("Tous les bilans", "Tout rendu", "Manquants") { AccessibleName = "Bilan" };
    private readonly DataGridView _cloturees = new();
    private readonly Label _rangPage = Ui.Libelle("", Theme.Police(9.75f), Theme.TexteSecondaire);
    private readonly BoutonArrondi _precedente = new("‹ Précédentes", StyleBouton.Secondaire) { AccessibleName = "Sorties clôturées précédentes" };
    private readonly BoutonArrondi _suivante = new("Suivantes ›", StyleBouton.Secondaire) { AccessibleName = "Sorties clôturées suivantes" };
    private readonly System.Windows.Forms.Timer _attenteRecherche = new() { Interval = 300 };
    private FiltreSorties _filtre = new();
    private PageSorties? _pageCloturees;
    private Dictionary<string, CompteTypeSortie> _typesAffiches = [];
    private bool _remplissage;

    // Colonnes du tableau et tri correspondant (la colonne « Détail » ne trie pas).
    private static readonly (string Nom, string Titre, float Poids, TriSorties? Tri)[] Colonnes =
    [
        ("date", "Date", 70, TriSorties.Date),
        ("intitule", "Sortie", 170, TriSorties.Sortie),
        ("type", "Type", 90, TriSorties.Type),
        ("moniteur", "Responsable", 110, TriSorties.Responsable),
        ("materiel", "Matériel", 80, TriSorties.Materiel),
        ("bilan", "Bilan", 100, TriSorties.Bilan),
        ("detail", "", 70, null),
    ];

    public EcranSorties(
        ServiceSortie sorties,
        IHorloge horloge,
        INavigation navigation,
        ServiceReglages reglages,
        ServiceModelesSortie modeles,
        ServiceTypesSortie typesSortie,
        ServiceMoniteurs moniteurs,
        GardeGestion garde)
    {
        _sorties = sorties;
        _reglages = reglages;
        _horloge = horloge;
        _navigation = navigation;
        _modeles = modeles;
        _typesSortie = typesSortie;
        _moniteurs = moniteurs;
        _garde = garde;

        // Rappel de la sauvegarde complète hebdomadaire (stockage externe), visible seulement quand elle est à faire.
        var faire = new BoutonArrondi("Faire la sauvegarde ›", StyleBouton.Secondaire, Icones.Telecharger) { Margin = new Padding(14, 0, 0, 0) };
        faire.AccessibleDescription = "Ouvre Paramètres, sur les sauvegardes";
        faire.Click += (_, _) => _navigation.AllerA<EcranParametres>(EcranParametres.VersSauvegardes);
        _ligneRappel = Ui.Colonnes((_rappelSauvegarde, SizeType.Percent, 100), (faire, SizeType.AutoSize, 0));
        _ligneRappel.Height = 76;
        _ligneRappel.Margin = new Padding(0, 0, 0, 24);
        faire.Dock = DockStyle.None;
        faire.Anchor = AnchorStyles.Right;
        _ligneRappel.Visible = false;

        _panneauEnCours = CreerEnCours();
        _panneauCloturees = CreerCloturees();
        _panneauCloturees.Visible = false;
        _panneauModeles = CreerModeles();
        _panneauModeles.Visible = false;

        // Haut : onglets à gauche, « Nouvelle sortie » à droite (onglet Clôturées).
        _onglets.Changement += (_, _) => AfficherOnglet();
        _nouvelleHaut.Click += (_, _) => _navigation.AllerA<EcranNouvelleSortie>();
        var haut = Ui.Colonnes((_onglets, SizeType.AutoSize, 0), (new Panel { BackColor = Color.Transparent }, SizeType.Percent, 100), (_nouvelleHaut, SizeType.AutoSize, 0));
        _onglets.Dock = _nouvelleHaut.Dock = DockStyle.None;
        _onglets.Anchor = AnchorStyles.Left;
        _nouvelleHaut.Anchor = AnchorStyles.Right;
        haut.Height = Theme.HauteurBouton + 10;
        haut.Margin = new Padding(0, 0, 0, 24);

        var contenu = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = Padding.Empty };
        _panneauEnCours.Dock = _panneauCloturees.Dock = _panneauModeles.Dock = DockStyle.Fill;
        contenu.Controls.Add(_panneauEnCours);
        contenu.Controls.Add(_panneauCloturees);
        contenu.Controls.Add(_panneauModeles);

        Controls.Add(Ui.Colonne((haut, SizeType.AutoSize, 0), (contenu, SizeType.Percent, 100)));
    }

    public override string Titre => "Sorties";

    public override EntreeMenu Menu => EntreeMenu.Sorties;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _attenteRecherche.Dispose();
        }

        base.Dispose(disposing);
    }

    private bool Cloturees => _onglets.SelectedIndex == 1;

    private void AfficherOnglet()
    {
        _panneauEnCours.Visible = _onglets.SelectedIndex == 0;
        _panneauCloturees.Visible = Cloturees;
        _panneauModeles.Visible = _onglets.SelectedIndex == 2;
        _nouvelleHaut.Visible = Cloturees;
    }

    // ---------------------------------------------------------------- Modèles de sortie

    private Control CreerModeles()
    {
        var titre = Ui.TitreSection("Modèles de sortie");
        var precision = Ui.Libelle("Les sorties qui reviennent toujours pareil : cours du lundi, fosse du jeudi…", Theme.Police(10.5f), Theme.TexteSecondaire);
        precision.Margin = new Padding(0, 4, 0, 0);
        var textes = Ui.Pile(titre, precision);
        var nouveau = new BoutonArrondi("Nouveau modèle", StyleBouton.Principal, Icones.Plus) { Margin = Padding.Empty };
        nouveau.Click += (_, _) => Executer(() => EditerModeleAsync(null));
        var entete = Ui.Colonnes((textes, SizeType.Percent, 100), (nouveau, SizeType.AutoSize, 0));
        nouveau.Dock = DockStyle.None;
        nouveau.Anchor = AnchorStyles.Right;
        entete.Height = 64;
        entete.Margin = new Padding(0, 0, 0, 18);
        return Ui.Colonne((entete, SizeType.Absolute, 64 + 18), (_grilleModeles, SizeType.Percent, 100));
    }

    /// <summary>Démarrage rapide (4 modèles, les plus proches d'abord), onglet Modèles et nombre de l'onglet.</summary>
    private async Task AfficherModelesAsync()
    {
        var tous = await _modeles.ListerAsync();
        var rapides = await _modeles.DemarrageRapideAsync();
        if (IsDisposed)
        {
            return;
        }

        _onglets.DefinirNombre(2, tous.Count);
        _rapides.HauteurElement = rapides.Count == 0 ? 84 : 172; // Sans modèle : une simple invitation.
        Remplir(_rapides, rapides.Count == 0
            ? [new CarteSansModele("Aucun modèle pour l'instant. Créez vos sorties habituelles une fois, puis lancez-les en un clic.", () => Executer(() => EditerModeleAsync(null)))]
            : rapides.Select(f => (Control)CreerCarteRapide(f)));
        Remplir(_grilleModeles, tous.Count == 0
            ? [new CarteSansModele("Aucun modèle. Cliquez sur « Nouveau modèle » pour créer votre première sortie habituelle.", () => Executer(() => EditerModeleAsync(null)))]
            : tous.Select(f => (Control)CreerCarteModele(f)));
    }

    private static void Remplir(GrilleAdaptative grille, IEnumerable<Control> cartes)
    {
        grille.SuspendLayout();
        while (grille.Controls.Count > 0)
        {
            var c = grille.Controls[0];
            grille.Controls.RemoveAt(0);
            c.Dispose();
        }

        foreach (var carte in cartes)
        {
            grille.Controls.Add(carte);
        }

        grille.ResumeLayout(true);
    }

    private CarteRapide CreerCarteRapide(FicheModele f)
    {
        var carte = new CarteRapide(f);
        carte.Creer += (_, _) => Executer(() => CreerDepuisModeleAsync(f));
        carte.Ajuster += (_, _) => _navigation.AllerA<EcranNouvelleSortie>(f);
        return carte;
    }

    private CarteModele CreerCarteModele(FicheModele f)
    {
        var carte = new CarteModele(f);
        carte.Creer += (_, _) => Executer(() => CreerDepuisModeleAsync(f));
        carte.Modifier += (_, _) => Executer(() => EditerModeleAsync(f));
        carte.Dupliquer += (_, _) => Executer(async () =>
        {
            var r = await _modeles.DupliquerAsync(f.Modele.Id);
            await AfficherModelesAsync();
            if (r is { Reussi: true, Id: { } id } && await _modeles.ObtenirAsync(id) is { } copie)
            {
                await EditerModeleAsync(copie); // La copie s'ouvre : on change le jour, le responsable…
            }
        });
        carte.Supprimer += (_, _) => Executer(async () =>
        {
            if (Dialogues.Confirmer(FindForm(), $"Supprimer le modèle « {f.Modele.Nom} » ?\n\nLes sorties déjà créées avec lui ne changent pas."))
            {
                await _modeles.SupprimerAsync(f.Modele.Id);
                await AfficherModelesAsync();
            }
        });
        return carte;
    }

    private async Task EditerModeleAsync(FicheModele? fiche)
    {
        if (await FormulaireModele.OuvrirAsync(FindForm(), fiche, _modeles, _typesSortie, _moniteurs, _garde) && !IsDisposed)
        {
            await AfficherModelesAsync();
        }
    }

    /// <summary>« Créer la sortie » : la sortie est créée d'après le modèle, puis on passe au scan du départ.</summary>
    private async Task CreerDepuisModeleAsync(FicheModele fiche)
    {
        var r = await _modeles.CreerSortieAsync(fiche.Modele.Id);
        if (r is { Reussi: true, Id: { } id })
        {
            _navigation.AllerA<EcranDepart>(id);
        }
        else if (!IsDisposed)
        {
            Dialogues.Erreur(FindForm(), r.Message);
        }
    }

    // ---------------------------------------------------------------- Onglet En cours

    private Control CreerEnCours()
    {
        var titreEnCours = Ui.TitreSection("Sorties en cours");
        var precision = Ui.Libelle("matériel encore dehors", Theme.Police(10.5f), Theme.TexteSecondaire);
        precision.Margin = new Padding(10, 7, 0, 0);

        // Démarrage rapide : les modèles du jour et des prochains jours, « Gérer les modèles › » ouvre l'onglet.
        var titreRapide = Ui.TitreSection("Démarrage rapide");
        var precisionRapide = Ui.Libelle("vos sorties habituelles en un clic", Theme.Police(10.5f), Theme.TexteSecondaire);
        precisionRapide.Margin = new Padding(10, 7, 0, 0);
        var gerer = new BoutonArrondi("Gérer les modèles ›", StyleBouton.Lien) { Margin = Padding.Empty };
        gerer.Click += (_, _) => _onglets.SelectedIndex = 2;
        var enteteRapide = Ui.Colonnes((Ui.Rangee(true, titreRapide, precisionRapide), SizeType.Percent, 100), (gerer, SizeType.AutoSize, 0));
        gerer.Dock = DockStyle.None;
        gerer.Anchor = AnchorStyles.Right;
        enteteRapide.Height = Theme.HauteurCliquable;
        enteteRapide.Margin = new Padding(0, 0, 0, 8);

        var colonne = Ui.Colonne(
            (CreerBandeau(), SizeType.Absolute, 230 + 30),
            (_ligneRappel, SizeType.AutoSize, 0),
            (enteteRapide, SizeType.AutoSize, 0),
            (_rapides, SizeType.Absolute, 172 + 26),
            (Ui.Rangee(true, titreEnCours, precision), SizeType.AutoSize, 0),
            (_cartes, SizeType.Percent, 100)); // Toutes les sorties en cours ; la grille défile si besoin.

        // Écran peu haut : bandeau plus bas, pour garder de la place aux cartes.
        colonne.Resize += (_, _) =>
        {
            var hauteur = colonne.Height < Dessin.Echelle(colonne, 640) ? 196 : 230;
            var voulue = Dessin.Echelle(colonne, hauteur + 30);
            if (Math.Abs(colonne.RowStyles[0].Height - voulue) > 0.5f)
            {
                colonne.RowStyles[0].Height = voulue;
            }
        };
        return colonne;
    }

    // ---------------------------------------------------------------- Onglet Clôturées

    private Control CreerCloturees()
    {
        // Titre et totaux du filtre : « 10 sorties · 119 équipements sortis ».
        var titre = Ui.TitreSection("Sorties clôturées");
        _totaux.Margin = new Padding(12, 9, 0, 0);
        var entete = Ui.Rangee(true, titre, _totaux);
        entete.Margin = new Padding(0, 0, 0, 14);

        // Filtres : recherche, période, responsable, effacer ; puis types et bilan.
        _periode.Items.AddRange([
            new Choix<PeriodeSorties>(PeriodeSorties.Saison, "Toute la saison"),
            new Choix<PeriodeSorties>(PeriodeSorties.TrenteJours, "30 derniers jours"),
            new Choix<PeriodeSorties>(PeriodeSorties.TroisMois, "3 derniers mois"),
            new Choix<PeriodeSorties>(PeriodeSorties.Toutes, "Toutes les sorties"),
        ]);
        _periode.SelectedIndex = 0;
        _responsable.Items.Add(new Choix<long?>(null, "Tous les responsables"));
        _responsable.SelectedIndex = 0;
        _recherche.Champ.AccessibleName = "Rechercher une sortie";
        _recherche.Champ.TextChanged += (_, _) =>
        {
            if (!_remplissage)
            {
                _attenteRecherche.Stop();
                _attenteRecherche.Start();
            }
        };
        _attenteRecherche.Tick += (_, _) =>
        {
            _attenteRecherche.Stop();
            Filtrer(_filtre with { Recherche = _recherche.Valeur });
        };
        _periode.SelectedIndexChanged += (_, _) =>
        {
            if (!_remplissage && _periode.SelectedItem is Choix<PeriodeSorties> p)
            {
                Filtrer(_filtre with { Periode = p.Valeur });
            }
        };
        _responsable.SelectedIndexChanged += (_, _) =>
        {
            if (!_remplissage && _responsable.SelectedItem is Choix<long?> r)
            {
                Filtrer(_filtre with { ResponsableId = r.Valeur });
            }
        };
        _types.Changement += (_, _) =>
            Filtrer(_filtre with { TypeId = _types.Selection is { } nom && _typesAffiches.TryGetValue(nom, out var t) ? t.TypeId : null });
        _types.CouleurPoint = nom => _typesAffiches.TryGetValue(nom, out var t) ? Theme.CouleurTypeSortie(t.Famille) : null;
        _bilan.Changement += (_, _) =>
        {
            if (!_remplissage)
            {
                Filtrer(_filtre with { Bilan = (BilanSorties)_bilan.SelectedIndex });
            }
        };
        _effacer.Click += (_, _) => EffacerFiltres();

        static Control Champ(string libelle, Control champ)
        {
            var etiquette = Ui.LibelleChamp(libelle);
            etiquette.Margin = new Padding(2, 0, 0, 6);
            var pile = Ui.Colonne((etiquette, SizeType.AutoSize, 0), (champ, SizeType.Absolute, Theme.HauteurChamp));
            pile.Margin = new Padding(0, 0, 12, 0);
            return pile;
        }

        var ligne1 = Ui.Colonnes(
            (Champ("Recherche", _recherche), SizeType.Percent, 100),
            (Champ("Période", _periode), SizeType.Absolute, 220),
            (Champ("Responsable", _responsable), SizeType.Absolute, 260),
            (_effacer, SizeType.AutoSize, 0));
        _effacer.Dock = DockStyle.None;
        _effacer.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        var ligne2 = Ui.Colonnes((_types, SizeType.Percent, 100), (_bilan, SizeType.AutoSize, 0));
        _bilan.Dock = DockStyle.None;
        _bilan.Anchor = AnchorStyles.Right;
        var filtres = Ui.Colonne((ligne1, SizeType.Absolute, Theme.HauteurChamp + 26), (ligne2, SizeType.Absolute, 52));
        ligne2.Margin = new Padding(0, 14, 0, 0);
        var carteFiltres = new Carte { Padding = new Padding(18, 16, 18, 14), Rayon = 22, Margin = new Padding(0, 0, 0, 18) };
        filtres.Dock = DockStyle.Fill;
        carteFiltres.Controls.Add(filtres);

        // Tableau : clic sur une ligne (ou Entrée) = détail de la sortie ; clic sur un en-tête = tri.
        Theme.AppliquerGrille(_cloturees, 58);
        foreach (var (nom, titreColonne, poids, _) in Colonnes)
        {
            Tableaux.AjouterColonne(_cloturees, nom, titreColonne, poids);
        }

        _cloturees.AccessibleName = "Sorties clôturées";
        _cloturees.CellClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && _cloturees.Rows[e.RowIndex].Tag is SortieResume s)
            {
                _navigation.AllerA<EcranRetour>(s.Id); // Ses manquants ; en mode gestion, on peut les rendre.
            }
        };
        _cloturees.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && _cloturees.CurrentRow?.Tag is SortieResume s)
            {
                e.SuppressKeyPress = true;
                _navigation.AllerA<EcranRetour>(s.Id);
            }
        };
        _cloturees.ColumnHeaderMouseClick += (_, e) =>
        {
            if (Colonnes[e.ColumnIndex].Tri is { } tri)
            {
                // Même colonne : on inverse ; autre colonne : dates et nombres du plus grand, textes de A à Z.
                var descendant = _filtre.Tri == tri ? !_filtre.Descendant : tri is TriSorties.Date or TriSorties.Materiel or TriSorties.Bilan;
                Filtrer(_filtre with { Tri = tri, Descendant = descendant }, page: _pageCloturees?.Page ?? 1);
            }
        };
        _cloturees.CellMouseEnter += (_, e) =>
            _cloturees.Cursor = e.RowIndex >= 0 || (e.ColumnIndex >= 0 && Colonnes[e.ColumnIndex].Tri is not null) ? Cursors.Hand : Cursors.Default;
        _cloturees.CellPainting += PeindreCellule;

        var carteTableau = new Carte { Padding = new Padding(8, 6, 8, 8), Rayon = 22 };
        _cloturees.Dock = DockStyle.Fill;
        _rangPage.AutoSize = false;
        _rangPage.Dock = DockStyle.Fill;
        _rangPage.TextAlign = ContentAlignment.MiddleLeft;
        _rangPage.Margin = new Padding(12, 0, 0, 0);
        _precedente.Margin = new Padding(0, 0, 8, 0);
        _suivante.Margin = new Padding(0, 0, 8, 0);
        _precedente.Click += (_, _) => Executer(() => AfficherClotureesAsync((_pageCloturees?.Page ?? 1) - 1));
        _suivante.Click += (_, _) => Executer(() => AfficherClotureesAsync((_pageCloturees?.Page ?? 1) + 1));
        var pagination = Ui.Colonnes((_rangPage, SizeType.Percent, 100), (_precedente, SizeType.AutoSize, 0), (_suivante, SizeType.AutoSize, 0));
        pagination.Dock = DockStyle.Bottom;
        pagination.Height = Theme.HauteurBouton + 12;
        pagination.Padding = new Padding(0, 6, 0, 0);
        carteTableau.Controls.Add(pagination);
        carteTableau.Controls.Add(_cloturees);
        _cloturees.BringToFront(); // Le tableau prend la place laissée par la barre du bas.

        return Ui.Colonne(
            (entete, SizeType.AutoSize, 0),
            (carteFiltres, SizeType.Absolute, Theme.HauteurChamp + 26 + 52 + 30 + 18),
            (carteTableau, SizeType.Percent, 100));
    }

    /// <summary>Sortie (titre et lieu), type (pastille de sa couleur), bilan (pastille), bouton « Détail ».</summary>
    private void PeindreCellule(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Graphics is not { } g || _cloturees.Rows[e.RowIndex].Tag is not SortieResume s)
        {
            return;
        }

        switch (Colonnes[e.ColumnIndex].Nom)
        {
            case "intitule":
                var selection = (e.State & DataGridViewElementStates.Selected) != 0;
                e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border | (selection ? DataGridViewPaintParts.SelectionBackground : 0));
                var x = e.CellBounds.X + (int)Dessin.Echelle(_cloturees, 12);
                var l = e.CellBounds.Width - (int)Dessin.Echelle(_cloturees, 20);
                var milieu = e.CellBounds.Y + (e.CellBounds.Height / 2);
                var lieu = s.Lieu.Trim();
                var hTitre = (int)Dessin.Echelle(_cloturees, 22);
                Dessin.Texte(g, s.Intitule, Theme.PoliceTitre(10.5f), new Rectangle(x, lieu.Length == 0 ? milieu - (hTitre / 2) : milieu - hTitre, l, hTitre), Theme.Texte);
                if (lieu.Length > 0)
                {
                    Dessin.Texte(g, lieu, Theme.Police(9f), new Rectangle(x, milieu, l, (int)Dessin.Echelle(_cloturees, 20)), Theme.TexteSecondaire);
                }

                e.Handled = true;
                break;
            case "type":
                Tableaux.PeindrePastille(e, s.LibelleType.ToUpper(Libelles.Culture), Theme.CouleurTypeSortie(s.Type));
                break;
            case "bilan":
                if (s.NbManquants == 0)
                {
                    Tableaux.PeindrePastille(e, "Tout rendu", Theme.Retour);
                }
                else
                {
                    Tableaux.PeindrePastille(e, Libelles.Pluriel(s.NbManquants, "manquant"), Theme.Depart);
                }

                break;
            case "detail":
                Tableaux.PeindreBoutons(e, "Détail ›");
                break;
        }
    }

    private void Filtrer(FiltreSorties filtre, int page = 1)
    {
        _filtre = filtre;
        Executer(() => AfficherClotureesAsync(page));
    }

    private void EffacerFiltres()
    {
        _remplissage = true;
        _attenteRecherche.Stop();
        _recherche.Valeur = "";
        _periode.SelectedIndex = 0;
        _responsable.SelectedIndex = 0;
        _bilan.SelectedIndex = 0;
        _types.Selection = null;
        _remplissage = false;
        Filtrer(new FiltreSorties { Tri = _filtre.Tri, Descendant = _filtre.Descendant });
    }

    protected override async Task ChargerAsync(object? parametre)
    {
        var enCours = await _sorties.ListerEnCoursAsync();
        var toutes = await _sorties.ListerClotureesAsync(1, 1); // Nombre de l'onglet : toutes les sorties clôturées.
        var responsables = await _sorties.ListerResponsablesAsync();
        if (IsDisposed)
        {
            return;
        }

        _onglets.DefinirNombre(0, enCours.Count);
        _onglets.DefinirNombre(1, toutes.Total);
        _remplissage = true;
        while (_responsable.Items.Count > 1)
        {
            _responsable.Items.RemoveAt(1);
        }

        foreach (var m in responsables)
        {
            _responsable.Items.Add(new Choix<long?>(m.Id, m.EstClasse ? $"{m.NomComplet} (classe)" : m.NomComplet));
        }

        _remplissage = false;
        _date.Text = _horloge.AujourdHui.ToString("dddd d MMMM", Libelles.Culture).ToUpper(Libelles.Culture);

        // Sauvegarde complète de la semaine : rappel tant qu'elle n'est pas faite.
        var sauvegarde = _reglages.EtatSauvegardeComplete(_horloge.MaintenantUtc.ToLocalTime());
        _ligneRappel.Visible = sauvegarde.AFaire;
        if (sauvegarde.AFaire)
        {
            _rappelSauvegarde.Afficher(NiveauResultat.Avertissement, sauvegarde.Message, RappelSauvegarde.Conseil, detailEnCode: false);
        }

        var dehors = enCours.Sum(s => s.NbDehors);
        _resume.Text = enCours.Count == 0
            ? "Aucune sortie en cours : tout le matériel est rangé."
            : $"{Libelles.Pluriel(enCours.Count, "sortie")} en cours · {Libelles.Pluriel(dehors, "équipement")} encore dehors";

        _cartes.SuspendLayout();
        while (_cartes.Controls.Count > 0)
        {
            var c = _cartes.Controls[0];
            _cartes.Controls.RemoveAt(0);
            c.Dispose();
        }

        foreach (var sortie in enCours)
        {
            var carte = new CarteSortie(sortie, _sorties.EstEnRetard(sortie));
            carte.Ouvrir += (_, _) =>
            {
                if (sortie.Statut == StatutSortie.EnPreparation)
                {
                    _navigation.AllerA<EcranDepart>(sortie.Id);
                }
                else
                {
                    _navigation.AllerA<EcranRetour>(sortie.Id);
                }
            };
            _cartes.Controls.Add(carte);
        }

        if (enCours.Count == 0)
        {
            _cartes.Controls.Add(new CarteAucune());
        }

        _cartes.ResumeLayout(true);

        await AfficherModelesAsync();
        await AfficherClotureesAsync(_pageCloturees?.Page ?? 1);
    }

    /// <summary>Une page des sorties clôturées du filtre (12 par page), ses totaux, les puces par type et la barre des pages.</summary>
    private async Task AfficherClotureesAsync(int page)
    {
        var filtre = _filtre;
        var resultat = await _sorties.ListerClotureesAsync(filtre, page);
        if (IsDisposed || filtre != _filtre)
        {
            return; // Un autre filtre a été choisi entre-temps : sa propre lecture l'affichera.
        }

        _pageCloturees = resultat;
        _totaux.Text = $"{Libelles.Pluriel(resultat.Total, "sortie")} · {Libelles.Pluriel(resultat.NbEquipements, "équipement sorti", "équipements sortis")}";
        _effacer.Visible = filtre.Actif;

        // Puces par type (sans le filtre de type).
        var parType = (resultat.ParType ?? []).ToList();
        _typesAffiches = parType.GroupBy(t => t.Nom).ToDictionary(g => g.Key, g => g.First());
        _types.Selection = filtre.TypeId is null ? null : parType.FirstOrDefault(t => t.TypeId == filtre.TypeId)?.Nom;
        _types.Afficher(parType.Sum(t => t.Nombre), parType.Select(t => (t.Nom, t.Nombre)));

        // En-têtes : la colonne triée porte une flèche.
        for (var i = 0; i < Colonnes.Length; i++)
        {
            var c = Colonnes[i];
            var fleche = c.Tri == filtre.Tri ? (filtre.Descendant ? " ↓" : " ↑") : "";
            _cloturees.Columns[i].HeaderText = c.Titre.ToUpper(Libelles.Culture) + fleche;
        }

        _cloturees.SuspendLayout();
        _cloturees.Rows.Clear();
        foreach (var s in resultat.Sorties)
        {
            var i = _cloturees.Rows.Add(Libelles.Date(s.DateSortie), s.Intitule, s.LibelleType, s.MoniteurNom, $"{s.NbTotal} équip.", "", "");
            _cloturees.Rows[i].Tag = s;
        }

        _cloturees.ResumeLayout();
        _rangPage.Text = resultat.Total == 0
            ? filtre.Actif ? "Aucune sortie ne correspond. Effacez les filtres pour tout revoir." : "Aucune sortie clôturée pour l'instant."
            : resultat.NbPages == 1
                ? Libelles.Pluriel(resultat.Total, "sortie clôturée", "sorties clôturées")
                : $"Sorties {resultat.Premiere} à {resultat.Derniere} sur {resultat.Total} · page {resultat.Page} / {resultat.NbPages}";
        _precedente.Visible = _suivante.Visible = resultat.NbPages > 1;
        _precedente.Enabled = resultat.Page > 1;
        _suivante.Enabled = resultat.Page < resultat.NbPages;
    }

    private BandeauAccueil CreerBandeau()
    {
        var bandeau = new BandeauAccueil { Margin = new Padding(0, 0, 0, 30) };
        var titre = Ui.Libelle("Bonne plongée, l'équipe !", Theme.PoliceGrasse(25.5f));
        _date.Margin = new Padding(0, 0, 0, 10);
        titre.Margin = new Padding(0, 0, 0, 8);
        _resume.Margin = new Padding(2, 0, 0, 18);

        var nouvelle = new BoutonArrondi("Nouvelle sortie", StyleBouton.Principal, Icones.Plus) { Margin = Padding.Empty };
        nouvelle.Click += (_, _) => _navigation.AllerA<EcranNouvelleSortie>();
        var inventaire = new BoutonArrondi("Voir l'inventaire", StyleBouton.Verre) { Margin = new Padding(12, 0, 0, 0) };
        inventaire.Click += (_, _) => _navigation.AllerA<EcranInventaire>();

        var contenu = Ui.Pile(_date, titre, _resume, Ui.Rangee(true, nouvelle, inventaire));
        var logo = new LogoClub();
        bandeau.Controls.Add(logo);
        bandeau.Controls.Add(contenu);

        // Logo du club à gauche (150 px, moins si le bandeau est réduit), textes et boutons à sa droite, centrés en hauteur.
        bandeau.Layout += (_, _) =>
        {
            var marge = (int)Dessin.Echelle(bandeau, 23);
            var cote = Math.Max(0, Math.Min((int)Dessin.Echelle(bandeau, 150), bandeau.Height - (2 * marge)));
            logo.Bounds = new Rectangle(bandeau.Padding.Left, (bandeau.Height - cote) / 2, cote, cote);
            contenu.Location = new Point(logo.Right + (int)Dessin.Echelle(bandeau, 30), Math.Max(bandeau.Padding.Top / 2, (bandeau.Height - contenu.Height) / 2));
        };
        return bandeau;
    }

    /// <summary>Carte d'une sortie en préparation ou en cours.</summary>
    private sealed class CarteSortie : Carte
    {
        public CarteSortie(SortieResume s, bool enRetard)
        {
            Padding = new Padding(24, 22, 24, 22);
            AccessibleName = enRetard ? $"{s.Intitule}, en retard" : s.Intitule;
            if (enRetard)
            {
                // Réglage « Alerte si non rendu après N jours » : la carte passe en orange.
                CouleurBordure = Theme.Depart;
                BackColor = Theme.Teinte(Theme.Depart, 0.06f);
            }

            var enPreparation = s.Statut == StatutSortie.EnPreparation;
            var date = new PastilleDate { Date = s.DateSortie };
            var type = new Pastille(s.LibelleType.ToUpper(Libelles.Culture), Theme.Teinte(Theme.Accent, 0.12f), Theme.AccentClair, police: Theme.PoliceTitre(7.5f), hauteur: 24)
            {
                Margin = new Padding(0, 0, 0, 6),
            };
            var titre = Ui.Libelle(s.Intitule, Theme.PoliceTitre(15f));
            titre.AutoSize = false;
            titre.AutoEllipsis = true;
            titre.Height = 32;
            var moniteur = Ui.Libelle(
                string.IsNullOrWhiteSpace(s.Lieu) ? s.LibelleResponsable : $"{s.LibelleResponsable} · {s.Lieu}",
                Theme.Police(9.75f),
                Theme.TexteSecondaireClair);
            moniteur.AutoSize = false;
            moniteur.AutoEllipsis = true;
            moniteur.Height = 22;

            var infos = Ui.Colonne((type, SizeType.AutoSize, 0), (titre, SizeType.AutoSize, 0), (moniteur, SizeType.AutoSize, 0));
            type.Dock = DockStyle.None;
            type.Anchor = AnchorStyles.Left;
            infos.Margin = new Padding(18, 0, 12, 0);

            var anneau = new AnneauProgression(84);
            if (enPreparation)
            {
                anneau.Couleur = Theme.Depart;
                anneau.Valeur = s.NbTotal > 0 ? 1 : 0;
                anneau.Nombre = s.NbTotal.ToString(Libelles.Culture);
                anneau.Legende = s.NbTotal > 1 ? "scannés" : "scanné";
            }
            else
            {
                anneau.Valeur = s.Progression;
                anneau.Nombre = s.NbRendus.ToString(Libelles.Culture);
                anneau.Total = $"/{s.NbTotal}";
                anneau.Legende = s.NbRendus > 1 ? "rendus" : "rendu";
            }

            var haut = Ui.Colonnes((date, SizeType.Absolute, 62), (infos, SizeType.Percent, 100), (anneau, SizeType.Absolute, 84));
            date.Dock = anneau.Dock = DockStyle.None;
            date.Anchor = anneau.Anchor = AnchorStyles.None;

            var etat = enPreparation
                ? new Pastille("En préparation", Theme.Teinte(Theme.Depart, 0.10f), Theme.Depart, Theme.Depart, Theme.PoliceTitre(9.75f), 38)
                : new Pastille(
                    enRetard ? $"En retard · {s.NbTotal - s.NbRendus} à rendre" : $"{s.NbTotal - s.NbRendus} à rendre",
                    Theme.Teinte(Theme.Depart, enRetard ? 0.22f : 0.10f),
                    enRetard ? Theme.DepartClair : Theme.Depart,
                    Theme.Depart,
                    Theme.PoliceTitre(9.75f),
                    38);
            var bouton = enPreparation
                ? new BoutonArrondi("Continuer le départ", StyleBouton.Depart, Icones.Fleche, iconeApres: true)
                : new BoutonArrondi("Scanner le retour", StyleBouton.Retour, Icones.Fleche, iconeApres: true);
            bouton.AutoSize = false;
            bouton.Margin = new Padding(12, 0, 0, 0);
            bouton.Font = Theme.PoliceTitre(10.5f);
            bouton.Click += (_, _) => Ouvrir?.Invoke(this, EventArgs.Empty);

            var bas = Ui.Colonnes((etat, SizeType.AutoSize, 0), (bouton, SizeType.Percent, 100));
            etat.Dock = DockStyle.None;
            etat.Anchor = AnchorStyles.Left;

            var colonne = Ui.Colonne((haut, SizeType.Absolute, 86), (bas, SizeType.Percent, 100));
            bas.Margin = new Padding(0, 18, 0, 0);
            Controls.Add(colonne);
        }

        public event EventHandler? Ouvrir;
    }

    /// <summary>Carte affichée quand aucune sortie n'est en cours.</summary>
    private sealed class CarteAucune : Carte
    {
        public CarteAucune()
        {
            Padding = new Padding(28);
            var texte = Ui.Libelle("Aucune sortie en cours.\nCliquez sur « Nouvelle sortie » pour préparer le départ du matériel.", Theme.Police(11f), Theme.TexteSecondaireClair);
            texte.AutoSize = false;
            texte.Dock = DockStyle.Fill;
            texte.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(texte);
        }
    }
}
