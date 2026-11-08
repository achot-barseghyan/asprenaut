using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>Formulaire de création d'une sortie : type en tuiles, date, moniteur, intitulé, lieu, notes.</summary>
public sealed class EcranNouvelleSortie : EcranBase
{
    private const int LargeurMax = 820;

    private readonly ServiceSortie _sorties;
    private readonly ServiceMoniteurs _moniteurs;
    private readonly INavigation _navigation;
    private readonly List<(Tuile Tuile, TypeSortie Type)> _types = [];
    private readonly ChampDate _date = new();
    private readonly ListeDeroulante _moniteur = new();
    private readonly ChampSaisie _intitule = new() { TexteIndicatif = "Ex. : Cours en piscine" };
    private readonly ChampSaisie _lieu = new() { TexteIndicatif = "Nom de la piscine, du site…" };
    private readonly ChampSaisie _notes = new(multiligne: true) { TexteIndicatif = "Groupe N1, 8 élèves…" };
    private readonly Label _message = Ui.Libelle("", Theme.PoliceTitre(10.5f), Theme.Erreur);
    private readonly BoutonArrondi _creer = new("Créer et scanner le matériel", StyleBouton.Principal, Icones.Fleche, iconeApres: true);
    private TypeSortie _type = TypeSortie.Piscine;

    public EcranNouvelleSortie(ServiceSortie sorties, ServiceMoniteurs moniteurs, INavigation navigation)
    {
        _sorties = sorties;
        _moniteurs = moniteurs;
        _navigation = navigation;

        // Type de sortie : une tuile par valeur.
        var tuiles = new GrilleAdaptative { LargeurMin = 96, HauteurElement = 96, Ecart = 10, ColonnesMax = 6, Margin = new Padding(0, 0, 0, 26) };
        foreach (var type in Enum.GetValues<TypeSortie>())
        {
            var tuile = new Tuile(Libelles.De(type), IconeType(type), DispositionTuile.Verticale) { Selectionne = type == _type };
            tuile.Click += (_, _) => ChoisirType(type);
            _types.Add((tuile, type));
            tuiles.Controls.Add(tuile);
        }

        _date.Date = DateOnly.FromDateTime(DateTime.Today);
        _moniteur.AccessibleName = "Moniteur responsable";
        _intitule.Champ.AccessibleName = "Intitulé";
        _lieu.Champ.AccessibleName = "Lieu";
        _notes.Champ.AccessibleName = "Notes";

        // Grille 2 × 2 : Date | Moniteur, Intitulé | Lieu.
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
        Champ(1, 0, "Moniteur responsable", _moniteur);
        Champ(0, 1, "Intitulé", _intitule);
        Champ(1, 1, "Lieu", _lieu);

        // La liste déroulante a sa propre hauteur : on la cale en haut de sa ligne.
        _moniteur.Dock = DockStyle.None;
        _moniteur.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;

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
        Ligne(tuiles, SizeType.Absolute, 96 + 26);
        Ligne(grille);
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
        Natif.ScrollbarsSombres(cadre);
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
        var moniteurs = await _moniteurs.ListerActifsAsync();
        _moniteur.Items.Clear();
        foreach (var m in moniteurs)
        {
            _moniteur.Items.Add(new Choix<long>(m.Id, m.NomComplet));
        }

        // Ouvert depuis la fiche d'un moniteur : il est déjà choisi.
        if (parametre is long moniteurId)
        {
            _moniteur.SelectedItem = _moniteur.Items.OfType<Choix<long>>().FirstOrDefault(c => c.Valeur == moniteurId);
        }

        if (_moniteur.Items.Count == 0)
        {
            _message.Text = "Aucun moniteur enregistré. Ajoutez-en un dans « Moniteurs ».";
        }

        _intitule.Champ.Focus();
    }

    private static string IconeType(TypeSortie type) => type switch
    {
        TypeSortie.Piscine => Icones.Piscine,
        TypeSortie.Fosse => Icones.Fosse,
        TypeSortie.Mer => Icones.Mer,
        TypeSortie.Lac => Icones.Lac,
        TypeSortie.Bapteme => Icones.Bapteme,
        _ => Icones.Autre,
    };

    private void ChoisirType(TypeSortie type)
    {
        _type = type;
        foreach (var (tuile, t) in _types)
        {
            tuile.Selectionne = t == type;
        }
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
                _type,
                _intitule.Valeur,
                (_moniteur.SelectedItem as Choix<long>)?.Valeur,
                _lieu.Valeur,
                _notes.Valeur);
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
