using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>Formulaire de création d'une sortie : type en tuiles, date, responsable (moniteur ou classe), intitulé, lieu, notes.</summary>
public sealed class EcranNouvelleSortie : EcranBase
{
    private const int LargeurMax = 820;

    private readonly ServiceSortie _sorties;
    private readonly ServiceMoniteurs _moniteurs;
    private readonly INavigation _navigation;
    private readonly ServiceTypesSortie _typesSortie;
    private readonly GardeGestion _garde;
    private readonly List<(Tuile Tuile, long Id)> _types = [];
    private readonly GrilleAdaptative _tuiles = new() { LargeurMin = 96, HauteurElement = 96, Ecart = 10, ColonnesMax = 6, Margin = new Padding(0, 0, 0, 26) };
    private readonly ChampDate _date = new();
    private readonly ChoixResponsable _responsable = new();
    private readonly ChampSaisie _intitule = new() { TexteIndicatif = "Ex. : Cours en piscine" };
    private readonly ChampSaisie _lieu = new() { TexteIndicatif = "Nom de la piscine, du site…" };
    private readonly ChampSaisie _notes = new(multiligne: true) { TexteIndicatif = "Groupe N1, 8 élèves…" };
    private readonly Label _message = Ui.Libelle("", Theme.PoliceTitre(10.5f), Theme.Erreur);
    private readonly BoutonArrondi _creer = new("Créer et scanner le matériel", StyleBouton.Principal, Icones.Fleche, iconeApres: true);
    private long? _typeId;

    // Compteur d'utilisation : valeur du type choisi, modifiable pour cette sortie (2 plongées dans la journée : 2).
    private readonly SelecteurNombre _utilisations = new("Utilisations comptées pour cette sortie", n => Libelles.Pluriel(n, "utilisation")) { Width = 230 };
    private readonly Label _aideUtilisations = Ui.Libelle("", Theme.Police(9f), Theme.TexteSecondaire);
    private readonly Dictionary<long, (string Nom, int Utilisations)> _utilisationsParType = [];
    private int? _utilisationsChoisies;

    // Sortie nominative : un équipement par catégorie et par adhérent (ligne masquée pour les autres types).
    private readonly OptionUnParCategorie _unParCategorie;
    private readonly HashSet<long> _typesNominatifs = [];

    public EcranNouvelleSortie(ServiceSortie sorties, ServiceMoniteurs moniteurs, INavigation navigation, ServiceTypesSortie typesSortie, GardeGestion garde)
    {
        _sorties = sorties;
        _moniteurs = moniteurs;
        _navigation = navigation;
        _typesSortie = typesSortie;
        _garde = garde;
        _unParCategorie = new OptionUnParCategorie(garde) { Margin = new Padding(0, 0, 0, 22), Visible = false };

        // Type de sortie : une tuile par type proposé (Paramètres › Types de sortie), ajoutées au chargement.

        _date.Date = DateOnly.FromDateTime(DateTime.Today);

        _intitule.Champ.AccessibleName = "Intitulé";
        _lieu.Champ.AccessibleName = "Lieu";
        _notes.Champ.AccessibleName = "Notes";

        // Grille 2 × 2 : Date | Responsable (moniteur ou classe), Intitulé | Lieu.
        var grille = new TableLayoutPanel { ColumnCount = 2, RowCount = 4, Dock = DockStyle.Fill, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 2) };
        grille.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grille.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var i = 0; i < 2; i++)
        {
            grille.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grille.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.HauteurChamp + 20));
        }

        void Champ(int colonne, int ligne, string libelle, Control champ)
        {
            var etiquette = Ui.LibelleChamp(libelle);
            etiquette.Margin = new Padding(colonne == 0 ? 0 : 10, 0, colonne == 0 ? 10 : 0, 8);
            champ.Margin = new Padding(colonne == 0 ? 0 : 10, 0, colonne == 0 ? 10 : 0, 20);
            champ.Dock = DockStyle.Fill;
            grille.Controls.Add(etiquette, colonne, ligne * 2);
            grille.Controls.Add(champ, colonne, (ligne * 2) + 1);
        }

        Champ(0, 0, "Date", _date);
        Champ(1, 0, "Responsable", _responsable);
        Champ(0, 1, "Intitulé", _intitule);
        Champ(1, 1, "Lieu", _lieu);


        var notes = Ui.Libelle("Notes", Theme.PoliceTitre(9.75f), Theme.TexteSecondaireClair);
        var facultatif = Ui.Libelle("(facultatif)", Theme.Police(9.75f), Theme.TexteSecondaire);
        facultatif.Margin = new Padding(4, 0, 0, 8);
        notes.Margin = new Padding(0, 0, 0, 8);
        _notes.Margin = new Padding(0, 0, 0, 22);

        var annuler = new BoutonArrondi("Annuler", StyleBouton.Secondaire) { Margin = new Padding(12, 0, 0, 0) };
        annuler.Click += (_, _) => _navigation.AllerA<EcranSorties>();
        _creer.Margin = new Padding(12, 0, 0, 0);
        _creer.Click += (_, _) => Executer(CreerAsync);
        var boutons = Ui.Rangee(false, _creer, annuler);
        boutons.Margin = new Padding(0, 22, 0, 0);
        var separateur = new Panel { BackColor = Theme.Bordure, Height = 1, Margin = Padding.Empty };
        _message.Margin = new Padding(0, 0, 0, 10);

        var legende = Ui.Libelle("TYPE DE SORTIE", Theme.PoliceTitre(9.75f), Theme.TexteSecondaireClair);
        legende.Margin = new Padding(0, 0, 0, 12);

        var formulaire = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            BackColor = Color.Transparent,
        };
        formulaire.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Ligne(Control c, SizeType type = SizeType.AutoSize, float valeur = 0)
        {
            formulaire.RowStyles.Add(new RowStyle(type, valeur));
            c.Dock = DockStyle.Fill;
            formulaire.Controls.Add(c, 0, formulaire.RowStyles.Count - 1);
        }

        Ligne(legende);
        Ligne(_tuiles, SizeType.Absolute, 96 + 26);
        Ligne(grille);
        Ligne(CadreUtilisations(), SizeType.Absolute, 84 + 22);
        Ligne(_unParCategorie);
        Ligne(Ui.Rangee(true, notes, facultatif));
        Ligne(_notes, SizeType.Absolute, 96 + 22);
        Ligne(_message);
        Ligne(separateur, SizeType.Absolute, 1);
        Ligne(boutons);
        boutons.Dock = DockStyle.None;
        boutons.Anchor = AnchorStyles.Right;

        var carte = new Carte { Padding = new Padding(32, 30, 32, 30) };
        carte.Controls.Add(formulaire);
        formulaire.SizeChanged += (_, _) => carte.Height = formulaire.Height + carte.Padding.Vertical;

        // Cadre défilant : la carte garde 820 px au plus et défile si la fenêtre est basse.
        var cadre = new Panel { BackColor = Theme.Fond, Margin = Padding.Empty };
        // Défilement vertical seulement (la carte suit la largeur du cadre).
        cadre.HorizontalScroll.Maximum = 0;
        cadre.AutoScroll = false;
        cadre.VerticalScroll.Visible = false;
        cadre.AutoScroll = true;
        Natif.ScrollbarsDuTheme(cadre);
        cadre.Controls.Add(carte);
        void Placer()
        {
            var largeur = Math.Min(cadre.ClientSize.Width - 1, (int)Dessin.Echelle(cadre, LargeurMax));
            carte.SetBounds(cadre.AutoScrollPosition.X, cadre.AutoScrollPosition.Y, largeur, formulaire.Height + carte.Padding.Vertical);
        }

        cadre.Resize += (_, _) => Placer();
        cadre.HandleCreated += (_, _) => Placer();
        formulaire.SizeChanged += (_, _) => Placer();

        Controls.Add(Ui.Colonne(
            (new FilAriane("Sorties", () => _navigation.AllerA<EcranSorties>()) { Courant = "Nouvelle sortie" }, SizeType.AutoSize, 0),
            (Ui.Pile(Ui.Titre(Titre), Ui.SousTitre("Créez la sortie, puis scannez le matériel qui part avec elle.")), SizeType.AutoSize, 0),
            (cadre, SizeType.Percent, 100)));
        cadre.Margin = new Padding(0, 18, 0, 0);
    }

    public override string Titre => "Nouvelle sortie";

    public override EntreeMenu Menu => EntreeMenu.Sorties;

    protected override async Task ChargerAsync(object? parametre)
    {
        var types = await _typesSortie.ListerProposesAsync();
        if (IsDisposed)
        {
            return;
        }

        _tuiles.SuspendLayout();
        foreach (var type in types)
        {
            var tuile = new Tuile(type.Nom, Icones.PourTypeSortie(type.Pictogramme), DispositionTuile.Verticale);
            var id = type.Id;
            tuile.Click += (_, _) => ChoisirType(id);
            _types.Add((tuile, id));
            _utilisationsParType[id] = (type.Nom, type.Utilisations);
            if (type.Nominatif)
            {
                _typesNominatifs.Add(id);
            }

            _tuiles.Controls.Add(tuile);
        }

        _tuiles.ResumeLayout(true);
        if (types.Count > 0)
        {
            ChoisirType(types[0].Id);
        }

        // Moniteurs et classes actifs ; ouvert depuis une fiche de « Moniteurs et classes » : ce responsable est déjà choisi.
        var responsables = await _moniteurs.ListerActifsAsync();
        _responsable.Remplir(responsables, parametre as long?);
        if (responsables.Count == 0)
        {
            _message.Text = "Aucun responsable enregistré. Ajoutez un moniteur ou une classe dans « Moniteurs et classes ».";
        }

        _intitule.Champ.Focus();
    }

    private void ChoisirType(long id)
    {
        _typeId = id;
        foreach (var (tuile, t) in _types)
        {
            tuile.Selectionne = t == id;
        }

        _utilisationsChoisies = null; // Nouveau type : on repart de sa valeur.
        AfficherUtilisations();

        // L'option « un équipement par catégorie » ne concerne que les sorties nominatives.
        var nominatif = _typesNominatifs.Contains(id);
        _unParCategorie.Visible = nominatif; // Ligne AutoSize : elle disparaît avec la carte.
    }

    /// <summary>Cadre « Compteur d'utilisation » : explication à gauche, sélecteur à droite.</summary>
    private Carte CadreUtilisations()
    {
        var titre = Ui.Libelle("Compteur d'utilisation", Theme.PoliceTitre(10.5f));
        titre.Margin = new Padding(0, 0, 0, 2);
        _aideUtilisations.Margin = Padding.Empty;
        _aideUtilisations.MaximumSize = new Size(440, 0);
        var textes = Ui.Pile(titre, _aideUtilisations);
        textes.Anchor = AnchorStyles.Left;
        _utilisations.Anchor = AnchorStyles.Right;
        _utilisations.Margin = new Padding(12, 0, 0, 0);
        // En mode consultation, changer le compteur demande le code gestionnaire (cadenas sur le sélecteur).
        _garde.Lier(_utilisations, ActionProtegee.CompteurSortie);
        _utilisations.Demande += (_, n) =>
        {
            if (!_garde.Exiger(FindForm(), ActionProtegee.CompteurSortie, "Changer les utilisations"))
            {
                return;
            }

            _utilisationsChoisies = Math.Clamp(n, TypeDeSortie.UtilisationsMin, TypeDeSortie.UtilisationsMax);
            AfficherUtilisations();
        };

        var rangee = Ui.Colonnes((textes, SizeType.Percent, 100), (_utilisations, SizeType.AutoSize, 0));
        rangee.AutoSize = true;
        rangee.Dock = DockStyle.Fill;
        var cadre = new Carte
        {
            Padding = new Padding(16, 12, 16, 12),
            Rayon = 16,
            BackColor = Theme.Teinte(Theme.Accent, 0.06f),
            CouleurBordure = Theme.Teinte(Theme.Accent, 0.24f),
            Margin = new Padding(0, 0, 0, 22),
            Height = 84,
        };
        cadre.Controls.Add(rangee);
        return cadre;
    }

    private void AfficherUtilisations()
    {
        if (_typeId is not { } id || !_utilisationsParType.TryGetValue(id, out var type))
        {
            return;
        }

        var n = _utilisationsChoisies ?? type.Utilisations;
        _utilisations.Nombre = n;
        _aideUtilisations.Text = n != type.Utilisations
            ? $"Modifié pour cette sortie (défaut {type.Nom} : {type.Utilisations})."
            : $"Valeur par défaut du type {type.Nom}, réglée dans les Paramètres. Ex. 2 plongées dans la journée : passez à 2.";
    }

    private async Task CreerAsync()
    {
        if (_date.Date is not { } date)
        {
            _message.Text = "Date invalide : écrivez-la comme 08/11/2026.";
            return;
        }

        _creer.Enabled = false;
        try
        {
            var demande = new NouvelleSortie(
                date,
                Core.Modeles.TypeSortie.Autre,
                _intitule.Valeur,
                _responsable.ResponsableId,
                _lieu.Valeur,
                _notes.Valeur,
                _typeId,
                _utilisations.Nombre,
                _unParCategorie.Actif);
            var resultat = await _sorties.CreerAsync(demande);
            if (resultat is { Reussi: true, Id: { } id })
            {
                _navigation.AllerA<EcranDepart>(id);
                return;
            }

            _message.Text = resultat.Message;
        }
        finally
        {
            if (!IsDisposed)
            {
                _creer.Enabled = true;
            }
        }
    }
}
