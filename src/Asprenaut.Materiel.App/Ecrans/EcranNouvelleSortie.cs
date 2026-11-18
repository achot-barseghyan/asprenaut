using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Création d'une sortie (maquette « Nouvelle sortie ») : étapes en haut à droite ; à gauche, quatre sections numérotées —
/// 1 Type de sortie (tuiles), 2 Quand et où (date et lieu avec raccourcis, intitulé proposé « AUTO »), 3 Responsable
/// (Moniteur | Classe puis cartes), 4 Règles et notes (compteur, un équipement par catégorie, notes) ; à droite, l'aperçu
/// de la sortie, l'étape suivante et « Créer et scanner le matériel ». Les règles sont dans <see cref="ServiceSortie.CreerAsync"/>.
/// </summary>
public sealed class EcranNouvelleSortie : EcranBase
{
    private const int LargeurApercu = 340;
    private const int LargeurApercuEtroite = 300;
    private const int EcartColonnes = 20;
    private const int LongueurNotes = 400;
    private const int LieuxProposes = 3;

    private readonly ServiceSortie _sorties;
    private readonly ServiceMoniteurs _moniteurs;
    private readonly INavigation _navigation;
    private readonly ServiceTypesSortie _typesSortie;
    private readonly GardeGestion _garde;

    private readonly ChoixTypeSortie _types = new() { ColonnesMax = 6, HauteurTuile = 84, Margin = new Padding(0, 0, 0, 16) };
    private readonly ChampDate _date = new();
    private readonly ChoixPuces _datesRapides = new() { Actions = true, AccessibleName = "Dates rapides", Margin = new Padding(0, 8, 8, 16) };
    private readonly ChampSaisie _lieu = new() { TexteIndicatif = "Nom de la piscine, du site…", Icone = Icones.Lieu };
    private readonly ChoixPuces _lieuxRapides = new() { Actions = true, AccessibleName = "Lieux récents", Margin = new Padding(8, 8, 0, 16) };
    private readonly ChampSaisie _intitule = new() { TexteIndicatif = "Ex. Piscine du jeudi" };
    private readonly Pastille _auto = new("AUTO", Theme.Teinte(Theme.Accent, 0.16f), Theme.AccentClair, police: Theme.PoliceGrasse(7.5f), hauteur: 18) { Margin = new Padding(8, 1, 0, 0) };
    private readonly Bascule _genre = new("Enseignant", "Classe") { Compact = true, AccessibleName = "Type de responsable" };
    private readonly Label _aideGenre = Ui.Libelle("", Theme.Police(9f), Theme.TexteSecondaire);
    private readonly ChoixPersonnes _personnes = new() { Colonnes = 4, LignesVisibles = 3, Margin = new Padding(0, 14, 0, 16) };
    private readonly SelecteurNombre _utilisations = new("Utilisations comptées pour cette sortie", n => Libelles.Pluriel(n, "utilisation")) { Width = 230 };
    private readonly Label _aideUtilisations = Ui.Libelle("", Theme.Police(9f), Theme.TexteSecondaire);
    private readonly OptionUnParCategorie _unParCategorie;
    private readonly ChampSaisie _notes = new(multiligne: true) { TexteIndicatif = "Ex. groupe N1, 8 élèves, départ du club à 7 h" };
    private readonly Label _compteurNotes = Ui.Libelle("", Theme.Police(9f), Theme.TexteSecondaire);

    private readonly ApercuSortie _apercu = new() { Margin = new Padding(0, 0, 0, 14) };
    private readonly BoutonArrondi _creer = new("Créer et scanner le matériel", StyleBouton.Principal, Icones.Fleche, iconeApres: true);
    private readonly Label _manque = Ui.Libelle("", Theme.Police(9f), Theme.TexteSecondaire);
    private readonly Label _message = Ui.Libelle("", Theme.PoliceTitre(9.75f), Theme.Erreur);

    private readonly TableLayoutPanel _sections;
    private readonly TableLayoutPanel _corps;
    private readonly Panel _cadre;

    private IReadOnlyList<TypeDeSortie> _listeTypes = [];
    private IReadOnlyList<Moniteur> _responsables = [];
    private readonly Dictionary<GenreResponsable, long?> _choixParGenre = [];
    private int? _utilisationsChoisies;
    private bool _titreAuto = true;
    private bool _majTitre;

    public EcranNouvelleSortie(ServiceSortie sorties, ServiceMoniteurs moniteurs, INavigation navigation, ServiceTypesSortie typesSortie, GardeGestion garde)
    {
        _sorties = sorties;
        _moniteurs = moniteurs;
        _navigation = navigation;
        _typesSortie = typesSortie;
        _garde = garde;
        _unParCategorie = new OptionUnParCategorie(garde) { Margin = new Padding(0, 0, 0, 16), Visible = false };

        _date.Date = DateOnly.FromDateTime(DateTime.Today);
        _intitule.Champ.AccessibleName = "Intitulé";
        _intitule.Champ.MaxLength = 60;
        _lieu.Champ.AccessibleName = "Lieu";
        _notes.Champ.AccessibleName = "Notes";
        _notes.Champ.MaxLength = LongueurNotes;

        // --- Sections ---
        var type = Section(1, "Type de sortie");
        type.Ajouter(_types);

        var quand = Section(2, "Quand et où");
        quand.Ajouter(QuandEtOu());

        var responsable = Section(3, "Responsable");
        _genre.Margin = new Padding(0, 0, 12, 0);
        _aideGenre.Anchor = AnchorStyles.Left;
        _aideGenre.Margin = Padding.Empty;
        var rangeeGenre = Ui.Rangee(true, _genre, _aideGenre);
        rangeeGenre.Margin = Padding.Empty;
        responsable.Ajouter(rangeeGenre);
        responsable.Ajouter(_personnes);

        var regles = Section(4, "Règles et notes");
        regles.Ajouter(Compteur());
        regles.Ajouter(_unParCategorie);
        var lNotes = Ui.Rangee(true, Ui.LibelleChamp("Notes"), Ui.Libelle("(facultatif)", Theme.Police(9f), Theme.TexteSecondaire));
        lNotes.Margin = new Padding(0, 0, 0, 0);
        regles.Ajouter(lNotes);
        regles.Ajouter(Bloc([(SizeType.Absolute, 104)], (_notes, 0, 0)));
        _notes.Margin = Padding.Empty;
        regles.Ajouter(_compteurNotes);
        _compteurNotes.Dock = DockStyle.None; // À droite, sous le champ (Anchor après Dock, qui le remet à zéro).
        _compteurNotes.Anchor = AnchorStyles.Right;
        _compteurNotes.Margin = new Padding(0, 6, 0, 12);

        _sections = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };
        _sections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var section in new[] { type, quand, responsable, regles })
        {
            FenetreFormulaire.AjouterLigne(_sections, section, SizeType.AutoSize, 0);
        }

        // Colonne de gauche défilante (l'aperçu reste en place à droite, comme « sticky » dans la maquette).
        _cadre = new Panel { BackColor = Theme.Fond, Margin = Padding.Empty, Dock = DockStyle.Fill };
        _cadre.HorizontalScroll.Maximum = 0;
        _cadre.AutoScroll = false;
        _cadre.VerticalScroll.Visible = false;
        _cadre.AutoScroll = true;
        Natif.ScrollbarsDuTheme(_cadre);
        _cadre.Controls.Add(_sections);
        _cadre.Resize += (_, _) => Placer();
        _cadre.HandleCreated += (_, _) => Placer();
        _sections.SizeChanged += (_, _) => Placer();

        _corps = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = new Padding(0, 22, 0, 0) };
        _corps.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _corps.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LargeurApercu));
        _corps.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _corps.Controls.Add(_cadre, 0, 0);
        _corps.Controls.Add(ColonneApercu(), 1, 0);
        _corps.Resize += (_, _) => AjusterColonnes();

        var etapes = new EtapesSortie { Active = 1 };
        var entete = Ui.Entete(Ui.Pile(Ui.Titre(Titre), Ui.SousTitre("Créez la sortie, puis scannez le matériel qui part avec elle.")), etapes);

        Controls.Add(Ui.Colonne(
            (new FilAriane("Sorties", () => _navigation.AllerA<EcranSorties>()) { Courant = "Nouvelle sortie" }, SizeType.AutoSize, 0),
            (entete, SizeType.AutoSize, 0),
            (_corps, SizeType.Percent, 100)));

        BrancherEvenements();
    }

    public override string Titre => "Nouvelle sortie";

    public override EntreeMenu Menu => EntreeMenu.Sorties;

    // ------------------------------------------------------------------ Construction

    private static SectionFormulaire Section(int numero, string titre) => new(numero, titre)
    {
        // Sur le fond de la page (et non d'une fenêtre) : carte de la couleur des autres cartes.
        BackColor = Theme.Surface,
        Padding = new Padding(20, 20, 20, 6),
        Margin = new Padding(0, 0, 0, 16),
    };

    /// <summary>Table aux lignes données ; chaque contrôle (colonne, ligne) remplit sa case.</summary>
    private static TableLayoutPanel Bloc(IReadOnlyList<(SizeType Type, float Valeur)> lignes, params (Control Controle, int Colonne, int Ligne)[] cases)
    {
        var colonnes = cases.Max(c => c.Colonne) + 1;
        var table = new TableLayoutPanel
        {
            ColumnCount = colonnes,
            RowCount = lignes.Count,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };
        for (var i = 0; i < colonnes; i++)
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / colonnes));
        }

        foreach (var (t, v) in lignes)
        {
            table.RowStyles.Add(new RowStyle(t, v));
        }

        foreach (var (controle, colonne, ligne) in cases)
        {
            controle.Dock = DockStyle.Fill;
            table.Controls.Add(controle, colonne, ligne);
        }

        return table;
    }

    private static LibelleObligatoire Libelle(string texte, bool obligatoire) =>
        new(texte, Theme.PoliceTitre(9.75f), Theme.TexteSecondaireClair) { Etoile = obligatoire, Margin = new Padding(0, 0, 0, 8) };

    /// <summary>Date | Lieu (avec leurs raccourcis), puis l'intitulé pleine largeur.</summary>
    private TableLayoutPanel QuandEtOu()
    {
        var lDate = Libelle("Date", true);
        var lLieu = Libelle("Lieu", false);
        lLieu.Margin = new Padding(8, 0, 0, 8);
        _date.Margin = new Padding(0, 0, 8, 0);
        _lieu.Margin = new Padding(8, 0, 0, 0);

        var lIntitule = Libelle("Intitulé", true);
        lIntitule.Margin = Padding.Empty;
        var rangeeIntitule = Ui.Rangee(true, lIntitule, _auto);
        rangeeIntitule.Margin = new Padding(0, 0, 0, 8);
        _intitule.Margin = new Padding(0, 0, 0, 14);

        var bloc = Bloc(
            [(SizeType.AutoSize, 0), (SizeType.Absolute, Theme.HauteurChamp), (SizeType.AutoSize, 0), (SizeType.AutoSize, 0), (SizeType.Absolute, Theme.HauteurChamp + 14)],
            (lDate, 0, 0), (lLieu, 1, 0), (_date, 0, 1), (_lieu, 1, 1), (_datesRapides, 0, 2), (_lieuxRapides, 1, 2), (rangeeIntitule, 0, 3), (_intitule, 0, 4));
        bloc.SetColumnSpan(rangeeIntitule, 2);
        bloc.SetColumnSpan(_intitule, 2);
        lDate.Dock = lLieu.Dock = rangeeIntitule.Dock = DockStyle.None;
        lDate.Anchor = rangeeIntitule.Anchor = AnchorStyles.Left;
        lLieu.Anchor = AnchorStyles.Left;
        return bloc;
    }

    /// <summary>« Compteur d'utilisation » : explication à gauche, sélecteur à droite.</summary>
    private TableLayoutPanel Compteur()
    {
        var titre = Ui.Libelle("Compteur d'utilisation", Theme.PoliceTitre(10.5f));
        titre.Margin = new Padding(0, 0, 0, 3);
        _aideUtilisations.Margin = Padding.Empty;
        _aideUtilisations.MaximumSize = new Size(420, 0);
        var textes = Ui.Pile(titre, _aideUtilisations);
        _utilisations.Margin = new Padding(16, 10, 10, 10); // Place pour le cadenas accroché au coin.

        // En mode consultation, changer le compteur demande le code gestionnaire (cadenas sur le sélecteur).
        _garde.Lier(_utilisations, ActionProtegee.CompteurSortie);

        var rangee = Ui.Colonnes((textes, SizeType.Percent, 100), (_utilisations, SizeType.AutoSize, 0));
        textes.Dock = _utilisations.Dock = DockStyle.None;
        textes.Anchor = AnchorStyles.Left;
        _utilisations.Anchor = AnchorStyles.Right;
        rangee.AutoSize = true;
        rangee.RowStyles[0] = new RowStyle(SizeType.AutoSize);
        rangee.Margin = new Padding(0, 0, 0, 14);
        return rangee;
    }

    /// <summary>Colonne de droite : aperçu, étape suivante, boutons et ce qui manque.</summary>
    private Panel ColonneApercu()
    {
        var titre = Ui.Libelle("APERÇU", Theme.PoliceGrasse(8.25f), Theme.TexteSecondaire);
        titre.Margin = new Padding(2, 0, 0, 10);
        var note = new NoteEtape(Icones.Scanner, "Étape suivante", "Scannez chaque équipement avec le pistolet. La sortie apparaîtra dans « En cours ».")
        {
            Margin = new Padding(0, 0, 0, 16),
        };
        _creer.Click += (_, _) => Executer(CreerAsync);
        _creer.Margin = new Padding(0, 0, 0, 10);
        var annuler = new BoutonArrondi("Annuler", StyleBouton.Secondaire) { Margin = new Padding(0, 0, 0, 10) };
        annuler.Click += (_, _) => _navigation.AllerA<EcranSorties>();
        _manque.TextAlign = ContentAlignment.MiddleCenter;
        _manque.AutoSize = false;
        _manque.Height = 22;
        _message.AutoSize = true;
        _message.Margin = new Padding(0, 6, 0, 0);

        var colonne = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };
        colonne.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        FenetreFormulaire.AjouterLigne(colonne, titre, SizeType.AutoSize, 0);
        FenetreFormulaire.AjouterLigne(colonne, _apercu, SizeType.AutoSize, 0);
        FenetreFormulaire.AjouterLigne(colonne, note, SizeType.AutoSize, 0);
        FenetreFormulaire.AjouterLigne(colonne, _creer, SizeType.Absolute, 56 + 10);
        FenetreFormulaire.AjouterLigne(colonne, annuler, SizeType.Absolute, Theme.HauteurBouton + 10);
        FenetreFormulaire.AjouterLigne(colonne, _manque, SizeType.Absolute, 22);
        FenetreFormulaire.AjouterLigne(colonne, _message, SizeType.AutoSize, 0);

        var panneau = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Fond, Margin = new Padding(EcartColonnes, 0, 0, 0), AutoScroll = true };
        Natif.ScrollbarsDuTheme(panneau);
        panneau.Controls.Add(colonne);
        panneau.Resize += (_, _) => _message.MaximumSize = new Size(Math.Max(100, panneau.ClientSize.Width - 4), 0);
        return panneau;
    }

    // ------------------------------------------------------------------ Mise en page

    /// <summary>Largeur de l'aperçu et nombre de tuiles / cartes par ligne selon la place.</summary>
    private void AjusterColonnes()
    {
        var logique = _corps.Width / Dessin.Echelle(_corps, 1);
        var apercu = logique >= 1100 ? LargeurApercu : LargeurApercuEtroite;
        var largeur = Dessin.Echelle(_corps, apercu + EcartColonnes);
        if (Math.Abs(_corps.ColumnStyles[1].Width - largeur) > 0.5f)
        {
            _corps.ColumnStyles[1].Width = largeur;
        }

        var gauche = logique - apercu - EcartColonnes;
        _types.ColonnesMax = gauche >= 620 ? 6 : 3;
        _personnes.Colonnes = gauche >= 720 ? 4 : 2;
        _aideUtilisations.MaximumSize = new Size((int)Dessin.Echelle(_corps, Math.Max(160, gauche - 40 - 280)), 0);
    }

    private void Placer()
    {
        var largeur = Math.Max(1, _cadre.ClientSize.Width - 1);
        _sections.SetBounds(_cadre.AutoScrollPosition.X, _cadre.AutoScrollPosition.Y, largeur, _sections.GetPreferredSize(new Size(largeur, 0)).Height);
    }

    // ------------------------------------------------------------------ Données

    protected override async Task ChargerAsync(object? parametre)
    {
        _listeTypes = await _typesSortie.ListerProposesAsync();
        _responsables = await _moniteurs.ListerActifsAsync();
        var lieux = await _sorties.LieuxRecentsAsync(20);
        if (IsDisposed)
        {
            return;
        }

        _types.Remplir(_listeTypes.Select(t => new ChoixTypeSortie.Element(
            t.Id, t.Nom, Icones.PourTypeSortie(t), Theme.CouleurTypeSortie(t), Libelles.Pluriel(t.Utilisations, "utilisation"))));
        _types.Valeur = _listeTypes.FirstOrDefault()?.Id;

        _datesRapides.Groupe("", ServiceSortie.DatesProposees(DateOnly.FromDateTime(DateTime.Today)).Select(d => (d.Date.ToString("yyyy-MM-dd", Libelles.Culture), d.Texte)));
        _lieuxRapides.Groupe("", lieux.Take(LieuxProposes).Select(l => (l, l)));
        var suggestions = new AutoCompleteStringCollection();
        suggestions.AddRange([.. lieux]);
        _lieu.Champ.AutoCompleteCustomSource = suggestions;
        _lieu.Champ.AutoCompleteSource = AutoCompleteSource.CustomSource;
        _lieu.Champ.AutoCompleteMode = AutoCompleteMode.SuggestAppend;

        // Ouvert depuis une fiche de « Membres » : ce moniteur ou cette classe est déjà choisi.
        if (parametre is long id && _responsables.FirstOrDefault(m => m.Id == id) is { } choisi)
        {
            _choixParGenre[choisi.Genre] = choisi.Id;
            if (choisi.EstClasse)
            {
                _genre.SelectedIndex = 1; // Changement → RemplirPersonnes.
            }
        }

        // « Ajuster » d'un modèle de sortie : formulaire prérempli, pour changer un détail avant de créer.
        if (parametre is FicheModele modele)
        {
            if (_listeTypes.FirstOrDefault(t => t.Id == modele.Type?.Id) is { } typeModele)
            {
                _types.Valeur = typeModele.Id;
                _unParCategorie.Visible = typeModele.Nominatif;
            }

            _date.Date = modele.Prochaine;
            _lieu.Valeur = modele.Modele.Lieu;
            if (modele.Modele.Intitule.Trim().Length > 0)
            {
                _titreAuto = false;
                _majTitre = true;
                _intitule.Valeur = modele.Modele.Intitule.Trim();
                _majTitre = false;
            }

            _notes.Valeur = modele.Modele.Notes;
            _utilisationsChoisies = modele.UtilisationsModifiees ? modele.Utilisations : null;
            _unParCategorie.Actif = modele.Modele.UnParCategorie;
            if (_responsables.FirstOrDefault(r => r.Id == modele.Responsable?.Id) is { } responsableModele)
            {
                _choixParGenre[responsableModele.Genre] = responsableModele.Id;
                if (responsableModele.EstClasse && _genre.SelectedIndex != 1)
                {
                    _genre.SelectedIndex = 1;
                }
            }
        }

        RemplirPersonnes();
        if (_responsables.Count == 0)
        {
            _message.Text = "Aucun responsable enregistré. Ajoutez un enseignant ou une classe dans « Membres ».";
        }

        Actualiser();
        _intitule.Champ.Focus();
        _intitule.Champ.SelectAll(); // Taper remplace l'intitulé proposé.
    }

    private TypeDeSortie? TypeChoisi() => _listeTypes.FirstOrDefault(t => t.Id == _types.Valeur);

    private GenreResponsable GenreChoisi() => _genre.SelectedIndex == 1 ? GenreResponsable.Classe : GenreResponsable.Moniteur;

    private Moniteur? Responsable() => _responsables.FirstOrDefault(m => m.Id == _personnes.Valeur);

    private int Utilisations => _utilisationsChoisies ?? TypeChoisi()?.Utilisations ?? 1;

    private void RemplirPersonnes()
    {
        var g = GenreChoisi();
        var liste = _responsables.Where(m => m.Genre == g).ToList();
        _aideGenre.Text = g == GenreResponsable.Classe ? "Le matériel est noté au nom de la classe." : "L'enseignant répond du matériel sorti.";
        _personnes.AccessibleName = g == GenreResponsable.Classe ? "Classe responsable" : "Enseignant responsable";
        _personnes.TexteVide = g == GenreResponsable.Classe
            ? "Aucune classe : ajoutez-en une dans Membres › Classes."
            : "Aucun enseignant : dans Membres › Adhérents, activez « Enseignant » sur sa fiche.";
        _personnes.Remplir(
            liste.Select((m, i) => new ChoixPersonnes.Element(
                m.Id, EcranMoniteurs.Avatar.Initiales(m), m.NomComplet, FormulaireSortie.Detail(m), EcranMoniteurs.Avatar.Degrade(m, i), m.EstClasse, m.Actif)),
            _choixParGenre.GetValueOrDefault(g));
    }

    private void BrancherEvenements()
    {
        _types.ValeurChangee += (_, _) =>
        {
            // Nouveau type : on repart de ses utilisations ; l'option « un par catégorie » ne concerne que les sorties nominatives.
            _utilisationsChoisies = null;
            _unParCategorie.Visible = TypeChoisi()?.Nominatif == true;
            Actualiser();
        };
        _date.DateChangee += (_, _) => Actualiser();
        _datesRapides.PuceActivee += (_, v) => _date.Date = DateOnly.ParseExact(v, "yyyy-MM-dd", Libelles.Culture);
        _lieu.Champ.TextChanged += (_, _) => Actualiser();
        _lieuxRapides.PuceActivee += (_, v) =>
        {
            _lieu.Valeur = v;
            _lieu.Champ.SelectionStart = _lieu.Champ.TextLength;
        };
        _intitule.Champ.TextChanged += (_, _) =>
        {
            if (!_majTitre)
            {
                _titreAuto = _intitule.Valeur.Trim().Length == 0; // Tapé à la main : on ne le remplace plus.
                Actualiser();
            }
        };
        _intitule.Champ.Leave += (_, _) =>
        {
            if (_intitule.Valeur.Trim().Length == 0)
            {
                _titreAuto = true;
                Actualiser();
            }
        };
        _notes.Champ.TextChanged += (_, _) => Actualiser();
        _unParCategorie.ActifChange += (_, _) => Actualiser();
        _genre.Changement += (_, _) =>
        {
            RemplirPersonnes();
            Actualiser();
        };
        _personnes.ValeurChangee += (_, _) =>
        {
            _choixParGenre[GenreChoisi()] = _personnes.Valeur;
            Actualiser();
        };
        _utilisations.Demande += (_, n) =>
        {
            n = Math.Clamp(n, TypeDeSortie.UtilisationsMin, TypeDeSortie.UtilisationsMax);
            if (n != Utilisations && _garde.Exiger(FindForm(), ActionProtegee.CompteurSortie, "Changer les utilisations"))
            {
                _utilisationsChoisies = n;
            }

            Actualiser();
        };
    }

    /// <summary>Intitulé proposé, compteur, aperçu et ce qui manque, d'après la saisie en cours.</summary>
    private void Actualiser()
    {
        var type = TypeChoisi();
        var responsable = Responsable();
        var lieu = _lieu.Valeur.Trim();

        if (_titreAuto)
        {
            _majTitre = true;
            _intitule.Valeur = ServiceSortie.IntitulePropose(type, _date.Date, responsable, lieu);
            _majTitre = false;
        }

        _auto.Visible = _titreAuto;

        var n = Utilisations;
        _utilisations.Nombre = n;
        _aideUtilisations.Text = type is null ? ""
            : n != type.Utilisations ? $"Modifié pour cette sortie. Valeur par défaut du type {type.Nom} : {type.Utilisations}."
            : $"Valeur par défaut du type {type.Nom}, réglée dans les Paramètres. Ex. 2 plongées dans la journée : passez à 2.";
        _compteurNotes.Text = $"{_notes.Valeur.Length} / {LongueurNotes}";

        var lignes = new List<ApercuSortie.Ligne>
        {
            responsable is null
                ? new(Icones.Personne, "", "Responsable à choisir", Manquant: true)
                : new(Icones.Personne, responsable.EstClasse ? "Classe : " : "Enseignant : ", responsable.NomComplet),
            lieu.Length > 0 ? new(Icones.Lieu, "", lieu) : new(Icones.Lieu, "Lieu non précisé", ""),
            new(Icones.Liste, "", n.ToString(Libelles.Culture), $" {(n > 1 ? "utilisations" : "utilisation")} par équipement"),
        };
        if (_unParCategorie.Visible)
        {
            lignes.Add(_unParCategorie.Actif
                ? new(Icones.Bouclier, "", "1 équipement", " par catégorie et par adhérent")
                : new(Icones.Bouclier, "Règle « 1 par catégorie » ", "coupée"));
        }

        _apercu.Afficher(_date.Date, type?.Nom ?? "", type is null ? Theme.Accent : Theme.CouleurTypeSortie(type), _intitule.Valeur, lignes);

        var manque = new List<string>();
        if (type is null)
        {
            manque.Add("le type");
        }

        if (_date.Date is null)
        {
            manque.Add("la date");
        }

        if (_intitule.Valeur.Trim().Length == 0)
        {
            manque.Add("l'intitulé");
        }

        if (responsable is null)
        {
            manque.Add("le responsable");
        }

        _manque.Text = manque.Count == 0 ? "" : $"Il manque {string.Join(", ", manque)}.";
    }

    private async Task CreerAsync()
    {
        _message.Text = "";
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
                TypeChoisi()?.Pictogramme ?? TypeSortie.Autre,
                _intitule.Valeur,
                _personnes.Valeur,
                _lieu.Valeur,
                _notes.Valeur,
                _types.Valeur,
                Utilisations,
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
