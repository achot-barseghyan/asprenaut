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
/// Membres du club : onglets « Adhérents | Moniteurs | Classes » (Adhérents ouvert à l'arrivée). Moniteurs et classes : une carte par responsable de
/// sortie (sorties de la saison, matériel dehors) et sa fiche (matériel dehors sous sa responsabilité, dernières sorties,
/// modifier, créer une sortie). Une classe (Niveau 1, Niveau 2…) est responsable d'une sortie comme un moniteur ; elle a une
/// pastille carrée (N2). Adhérents : ceux qui prennent du matériel lors d'une sortie nominative (recherche, filtres, import
/// de la liste Excel) ; leur fiche montre le matériel qu'ils ont et celui jamais rendu. Un adhérent n'est jamais responsable.
/// </summary>
public sealed class EcranMoniteurs : EcranBase
{
    // Onglets, dans l'ordre affiché : Adhérents (ouvert à l'arrivée), Moniteurs, Classes.
    private const int OngletAdherents = 0, OngletMoniteurs = 1, OngletClasses = 2;

    private readonly ServiceMoniteurs _moniteurs;
    private readonly ServiceAdherents _adherents;
    private readonly INavigation _navigation;
    private readonly GrilleAdaptative _cartes = new() { LargeurMin = 250, HauteurElement = 176, Ecart = 14, Margin = new Padding(0, 0, 22, 0) };
    private readonly Panel _fiche = new() { AutoScroll = true, BackColor = Theme.Surface, Margin = Padding.Empty };
    private readonly Carte _carteFiche = new() { Padding = new Padding(24), CouleurBordure = Theme.BordureForte };
    private readonly Bascule _onglets = new("Adhérents", "Enseignants", "Classes");
    private readonly BoutonArrondi _ajouter = new("Ajouter un enseignant", StyleBouton.Principal, Icones.Plus);
    private readonly BoutonArrondi _importer = new("Importer la liste Excel", StyleBouton.Secondaire, Icones.Document) { Visible = false };
    private readonly BoutonArrondi _modele = new("Modèle Excel à remplir", StyleBouton.Secondaire, Icones.Telecharger) { Visible = false };
    private readonly Reglages _reglages;
    private readonly Dictionary<GenreResponsable, long?> _selection = [];
    private IReadOnlyList<FicheMoniteur> _tous = [];
    private IReadOnlyList<FicheMoniteur> _liste = [];

    // Onglet Adhérents : recherche par nom, filtres, adhérent choisi.
    private readonly BarreRecherche _recherche = new("Rechercher un adhérent…", "nom ou prénom") { Margin = new Padding(0, 0, 14, 0) };
    // Puces de l'onglet Adhérents (après « Tous »), dans l'ordre affiché.
    private static readonly (string Libelle, FiltreAdherents Filtre)[] PucesAdherents =
    [
        ("Matériel dehors", FiltreAdherents.MaterielDehors),
        ("Non rendu", FiltreAdherents.NonRendu),
        ("Nitrox", FiltreAdherents.Nitrox),
        ("CACI à renouveler", FiltreAdherents.CaciARenouveler),
        ("Désactivés", FiltreAdherents.Desactives),
    ];

    // Onglet Moniteurs : mêmes recherche et puces (pas pour les classes, peu nombreuses).
    private static readonly string[] PucesMoniteurs = ["Actifs", "Inspecteurs TIV", "Matériel dehors", "Inactifs"];

    // Puces « Tous 12 / Actifs 9… » (même dessin que les filtres de catégories) ; choix gardé pour chaque onglet.
    private readonly FiltreCategories _filtre = new(Theme.AccentClair) { LibelleTout = "Tous", Margin = new Padding(0, 10, 0, 8) };
    private FiltreAdherents _choixAdherents = FiltreAdherents.Tous;
    private FiltreMoniteurs _choixMoniteurs = FiltreMoniteurs.Tous;
    private readonly ListeDeroulante _niveau = new() { AccessibleName = "Filtrer par niveau", Width = 220 };
    private readonly System.Windows.Forms.Timer _attenteRecherche = new() { Interval = 300 };
    private readonly TableLayoutPanel _page;
    private IReadOnlyList<FicheAdherent> _listeAdherents = [];
    private long? _adherentChoisi;

    private readonly GardeGestion _garde;

    public EcranMoniteurs(ServiceMoniteurs moniteurs, ServiceAdherents adherents, INavigation navigation, GardeGestion garde, Reglages reglages)
    {
        _garde = garde;
        _reglages = reglages;
        _moniteurs = moniteurs;
        _adherents = adherents;
        _navigation = navigation;

        _garde.Lier(_ajouter, ActionProtegee.Moniteurs);
        _garde.Lier(_importer, ActionProtegee.Moniteurs);
        _ajouter.Click += (_, _) => Executer(() => Adherents ? ModifierAdherentAsync(null) : ModifierAsync(null, Genre));
        _importer.Click += (_, _) => Executer(ImporterAsync);
        _modele.Click += (_, _) => Executer(EnregistrerModeleAsync); // Libre : ne change aucune donnée.
        _modele.AccessibleDescription = "Fichier Excel vide à donner au club pour noter les nouveaux adhérents, puis à importer";

        _onglets.AccessibleName = "Membres";
        _onglets.Margin = new Padding(0, 0, 0, 22);
        _onglets.Changement += ChangementOnglet;

        _filtre.Changement += (_, _) =>
        {
            if (Adherents)
            {
                _choixAdherents = PucesAdherents.Where(p => p.Libelle == _filtre.Selection).Select(p => p.Filtre).FirstOrDefault(); // Tous si aucune.
            }
            else
            {
                _choixMoniteurs = (FiltreMoniteurs)(Array.IndexOf(PucesMoniteurs, _filtre.Selection) + 1);
            }

            Executer(AfficherGenreAsync);
        };

        // Formation en cours : toutes, une classe du club (remplie à l'affichage : RemplirFormationsAsync), ou aucune.
        _niveau.Items.Add(new Choix<string?>(null, "Toutes les formations"));
        _niveau.Items.Add(new Choix<string?>(ServiceAdherents.SansNiveau, "Sans formation"));
        _niveau.SelectedIndex = 0;
        _niveau.Margin = new Padding(0, 0, 14, 0);
        _niveau.AccessibleName = "Filtrer par formation en cours";
        _niveau.SelectedIndexChanged += (_, _) =>
        {
            if (!_remplissageFormations)
            {
                Executer(AfficherAdherentsAsync);
            }
        };
        _recherche.Champ.TextChanged += (_, _) =>
        {
            _attenteRecherche.Stop();
            _attenteRecherche.Start();
        };
        _attenteRecherche.Tick += (_, _) =>
        {
            _attenteRecherche.Stop();
            Executer(AfficherGenreAsync); // Moniteurs ou adhérents, selon l'onglet.
        };

        Natif.ScrollbarsDuTheme(_fiche);
        _fiche.Dock = DockStyle.Fill;
        _carteFiche.Controls.Add(_fiche);

        var onglets = Ui.Rangee(true, _onglets);
        // Recherche (et niveau pour les adhérents) puis les puces de l'onglet ; les puces remplissent leur cellule (ancrées
        // seulement, elles gardaient la hauteur nulle de la rangée masquée au départ).
        _recherche.Margin = new Padding(0, 0, 14, 0);
        var ligneRecherche = Ui.Colonnes((_recherche, SizeType.Percent, 100), (_niveau, SizeType.AutoSize, 0));
        var filtres = Ui.Colonne(
            (ligneRecherche, SizeType.Absolute, 54),
            (_filtre, SizeType.Absolute, Theme.HauteurCliquable + 8 + 10));
        filtres.Margin = new Padding(0, 0, 0, 20);
        _page = Ui.Colonne(
            (Ui.Entete(Ui.Pile(Ui.Titre(Titre), Ui.SousTitre("Les responsables de sortie, les adhérents du club et le matériel encore dehors.")), _ajouter, _importer, _modele), SizeType.AutoSize, 0),
            (onglets, SizeType.AutoSize, 0),
            (filtres, SizeType.Absolute, 0), // HauteurFiltres sur les onglets Moniteurs et Adhérents.
            (Ui.Colonnes((_cartes, SizeType.Percent, 60), (_carteFiche, SizeType.Percent, 40)), SizeType.Percent, 100));
        filtres.Visible = false;
        Controls.Add(_page);
    }

    public override string Titre => "Membres du club";

    public override EntreeMenu Menu => EntreeMenu.Moniteurs;

    /// <summary>Hauteur de la zone recherche + puces (pixels logiques) : 54 + puces + marges.</summary>
    private const int HauteurFiltres = 54 + Theme.HauteurCliquable + 8 + 10 + 20;

    private GenreResponsable Genre => _onglets.SelectedIndex == OngletClasses ? GenreResponsable.Classe : GenreResponsable.Moniteur;

    private bool Classes => Genre == GenreResponsable.Classe && !Adherents;

    private bool Adherents => _onglets.SelectedIndex == OngletAdherents;

    protected override Task ChargerAsync(object? parametre) => RechargerAsync();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _attenteRecherche.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task RechargerAsync()
    {
        _tous = await _moniteurs.ListerFichesAsync();
        var adherents = await _adherents.ListerAsync(null, FiltreAdherents.Tous);
        if (IsDisposed)
        {
            return;
        }

        _onglets.DefinirNombre(OngletMoniteurs, _tous.Count(f => !f.Moniteur.EstClasse));
        _onglets.DefinirNombre(OngletClasses, _tous.Count(f => f.Moniteur.EstClasse));
        _onglets.DefinirNombre(OngletAdherents, adherents.Tous);
        await AfficherGenreAsync();
    }

    /// <summary>Cartes de l'onglet choisi (moniteurs ou classes) et fiche du responsable choisi dans cet onglet.</summary>
    private async Task AfficherGenreAsync()
    {
        // Recherche et filtres : moniteurs et adhérents (pas les classes). Puces et niveau de l'onglet ouvert.
        var avecFiltres = !Classes;
        var filtres = _page.GetControlFromPosition(0, 2)!;
        if (filtres.Visible != avecFiltres)
        {
            filtres.Visible = avecFiltres;
            _page.RowStyles[2].Height = avecFiltres ? Dessin.Echelle(this, HauteurFiltres) : 0;
        }

        _niveau.Visible = Adherents;
        _filtre.Unite = Adherents ? "adhérent" : "enseignant";
        var indicatif = Adherents ? "Rechercher un adhérent…" : "Rechercher un enseignant (nom, niveau, téléphone)…";
        if (_recherche.Champ.PlaceholderText != indicatif)
        {
            _recherche.Champ.PlaceholderText = indicatif;
            _attenteRecherche.Stop();
            _recherche.Champ.Text = ""; // Chaque onglet a sa recherche.
            _attenteRecherche.Stop();
        }

        _importer.Visible = Adherents;
        _modele.Visible = Adherents;
        if (Adherents)
        {
            _ajouter.Text = _ajouter.AccessibleName = "Ajouter un adhérent";
            await AfficherAdherentsAsync();
            return;
        }

        var genre = Genre;
        _ajouter.Text = Classes ? "Ajouter une classe" : "Ajouter un enseignant";
        _ajouter.AccessibleName = _ajouter.Text;
        var duGenre = _tous.Where(f => f.Moniteur.Genre == genre).ToList();
        if (Classes)
        {
            _liste = duGenre;
        }
        else
        {
            var filtrees = ServiceMoniteurs.Filtrer(duGenre, _recherche.Champ.Text, _choixMoniteurs);
            _filtre.Selection = _choixMoniteurs == FiltreMoniteurs.Tous ? null : PucesMoniteurs[(int)_choixMoniteurs - 1];
            _filtre.Afficher(filtrees.Tous, PucesMoniteurs.Zip([filtrees.Actifs, filtrees.InspecteursTiv, filtrees.MaterielDehors, filtrees.Inactifs]));
            _liste = filtrees.Fiches;
        }
        if (!_selection.TryGetValue(genre, out var choisi) || _liste.All(f => f.Moniteur.Id != choisi))
        {
            _selection[genre] = _liste.FirstOrDefault()?.Moniteur.Id;
        }

        _cartes.SuspendLayout();
        while (_cartes.Controls.Count > 0)
        {
            var c = _cartes.Controls[0];
            _cartes.Controls.RemoveAt(0);
            c.Dispose();
        }

        for (var i = 0; i < _liste.Count; i++)
        {
            var fiche = _liste[i];
            var carte = new CarteMoniteur(fiche, i) { Selectionne = fiche.Moniteur.Id == _selection[genre] };
            carte.Click += (_, _) => Executer(() => ChoisirAsync(fiche.Moniteur.Id));
            _cartes.Controls.Add(carte);
        }

        _cartes.ResumeLayout(true);
        await AfficherFicheAsync();
    }

    private async Task ChoisirAsync(long id)
    {
        _selection[Genre] = id;
        foreach (var carte in _cartes.Controls.OfType<CarteMoniteur>())
        {
            carte.Selectionne = carte.MoniteurId == id;
        }

        await AfficherFicheAsync();
    }

    private async Task AfficherFicheAsync()
    {
        var adherents = Adherents;
        var detail = !adherents && _selection.GetValueOrDefault(Genre) is { } id ? await _moniteurs.ObtenirDetailAsync(id) : null;
        var ficheAdherent = adherents && _adherentChoisi is { } a ? await _adherents.ObtenirFicheAsync(a) : null;
        if (IsDisposed)
        {
            return;
        }

        var contenu = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Top, AutoSize = true, BackColor = Color.Transparent, Margin = Padding.Empty };
        contenu.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Ligne(Control c)
        {
            contenu.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            c.Dock = DockStyle.Fill;
            contenu.Controls.Add(c, 0, contenu.RowStyles.Count - 1);
        }

        if (adherents)
        {
            if (ficheAdherent is null)
            {
                Ligne(Ui.Libelle(_listeAdherents.Count == 0 && _recherche.Champ.Text.Trim().Length == 0 && _choixAdherents == FiltreAdherents.Tous
                    ? "Aucun adhérent. Importez la liste Excel du club ou cliquez sur « Ajouter un adhérent »."
                    : "Aucun adhérent ne correspond.", Theme.Police(11f), Theme.TexteSecondaireClair));
            }
            else
            {
                ConstruireFicheAdherent(ficheAdherent, Ligne);
            }
        }
        else if (detail is null)
        {
            Ligne(Ui.Libelle(Classes ? "Aucune classe. Cliquez sur « Ajouter une classe »." : "Aucun enseignant. Cliquez sur « Ajouter un enseignant ».", Theme.Police(11f), Theme.TexteSecondaireClair));
        }
        else
        {
            ConstruireFiche(detail, Ligne);
        }

        Dessin.MettreALEchelle(contenu, this);
        _fiche.SuspendLayout();
        while (_fiche.Controls.Count > 0)
        {
            var c = _fiche.Controls[0];
            _fiche.Controls.RemoveAt(0);
            c.Dispose();
        }

        _fiche.Controls.Add(contenu);
        _fiche.ResumeLayout(true);
    }

    private void ConstruireFiche(DetailMoniteur detail, Action<Control> ligne)
    {
        var m = detail.Moniteur;
        var index = Math.Max(0, _liste.ToList().FindIndex(f => f.Moniteur.Id == m.Id));

        // En-tête : pastille, nom, niveau (ou « Classe »), téléphone.
        var nom = Ui.Libelle(m.NomComplet, Theme.PoliceGrasse(16f));
        nom.Margin = new Padding(0, 0, 0, 4);
        var sortiesSaison = _liste.FirstOrDefault(f => f.Moniteur.Id == m.Id)?.SortiesSaison ?? 0;
        var qualite = Ui.Libelle(Qualite(m, sortiesSaison), Theme.Police(9.75f), Theme.TexteSecondaireClair);
        var textes = Ui.Pile(nom, qualite);
        if (m.Licence.Length > 0)
        {
            textes.Controls.Add(Ui.Libelle($"Licence {m.Licence}", Theme.PoliceCode(9.5f), Theme.TexteDoux));
        }

        if (m.Telephone.Length > 0)
        {
            var tel = Ui.Rangee(true, new Picto(Icones.Telephone, 18, Theme.TexteSecondaire, Color.Transparent) { Margin = new Padding(0, 2, 6, 0) },
                Ui.Libelle(m.Telephone, Theme.Police(9.75f), Theme.TexteDoux));
            textes.Controls.Add(tel);
        }

        var entete = Ui.Colonnes((new Avatar(m, index, 72), SizeType.Absolute, 72 + 16), (textes, SizeType.Percent, 100));
        entete.Height = textes.Controls.Count > 3 ? 96 : 80;
        entete.Margin = new Padding(0, 0, 0, 18);
        entete.Controls[0].Dock = DockStyle.None;
        entete.Controls[0].Anchor = AnchorStyles.Left;
        textes.Dock = DockStyle.None;
        textes.Anchor = AnchorStyles.Left;
        ligne(entete);

        // Matériel dehors sous sa responsabilité.
        ligne(Section(m.EstClasse ? "Matériel dehors pour cette classe" : "Matériel dehors sous sa responsabilité"));
        if (detail.EnCours.Count == 0)
        {
            var rien = new BandeauMessage { Margin = new Padding(0, 0, 0, 18), Height = 60 };
            rien.Afficher(NiveauResultat.Succes, "Rien dehors. Tout le matériel est rentré.");
            ligne(rien);
        }

        foreach (var sd in detail.EnCours)
        {
            ligne(CarteDehors(sd));
        }

        // Dernières sorties clôturées.
        ligne(Section("Dernières sorties"));
        if (detail.Dernieres.Count == 0)
        {
            var aucune = Ui.Libelle("Aucune sortie clôturée.", Theme.Police(9.75f), Theme.TexteSecondaire);
            aucune.Margin = new Padding(0, 0, 0, 18);
            ligne(aucune);
        }

        foreach (var s in detail.Dernieres)
        {
            ligne(new LigneHistorique(s));
        }

        // Actions.
        var modifier = new BoutonArrondi("Modifier", StyleBouton.Secondaire) { AutoSize = false, Margin = new Padding(0, 0, 10, 0) };
        _garde.Lier(modifier, ActionProtegee.Moniteurs);
        modifier.Click += (_, _) => Executer(() => ModifierAsync(m, m.Genre));
        var creer = new BoutonArrondi("Créer une sortie", StyleBouton.Principal) { AutoSize = false, Margin = Padding.Empty, Enabled = m.Actif };
        creer.Click += (_, _) => _navigation.AllerA<EcranNouvelleSortie>(m.Id);
        var actions = Ui.Colonnes((modifier, SizeType.Percent, 42), (creer, SizeType.Percent, 58));
        actions.Height = Theme.HauteurBouton;
        actions.Margin = new Padding(0, 18, 0, 0);
        ligne(actions);

        // Un moniteur peut redevenir simple adhérent (pas une classe).
        if (!m.EstClasse)
        {
            ligne(BoutonPassage("Passer en adhérent", "Ce n'est plus un enseignant : il devient un simple adhérent", () => PasserAdherentAsync(m)));
        }
    }

    /// <summary>Bouton « Passer en moniteur » / « Passer en adhérent » sous les actions d'une fiche (action Moniteurs).</summary>
    private BoutonArrondi BoutonPassage(string texte, string description, Func<Task> action)
    {
        var bouton = new BoutonArrondi(texte, StyleBouton.Secondaire, Icones.Echange)
        {
            AutoSize = false,
            Height = Theme.HauteurBouton,
            Margin = new Padding(0, 10, 0, 0), // Au bord de la fiche défilante : la pastille du cadenas rentre d'elle-même.
            AccessibleDescription = description,
        };
        _garde.Lier(bouton, ActionProtegee.Moniteurs);
        bouton.Click += (_, _) => Executer(action);
        return bouton;
    }

    /// <summary>L'adhérent devient moniteur (confirmation), puis l'onglet Moniteurs s'ouvre sur sa fiche.</summary>
    private async Task PasserMoniteurAsync(Adherent a)
    {
        if (!_garde.Exiger(FindForm(), ActionProtegee.Moniteurs, "Passer en enseignant")
            || !Dialogues.Confirmer(FindForm(),
                $"{a.NomComplet} devient enseignant ?\n\nSa fiche d'adhérent est supprimée s'il n'a jamais pris de matériel en sortie, sinon désactivée (son historique est gardé). Il pourra être responsable des sorties."))
        {
            return;
        }

        var resultat = await _adherents.PasserMoniteurAsync(a.Id);
        if (!resultat.Reussi)
        {
            Dialogues.Erreur(FindForm(), resultat.Message);
            return;
        }

        _selection[GenreResponsable.Moniteur] = resultat.Id;
        _choixMoniteurs = FiltreMoniteurs.Tous;
        await OuvrirOngletAsync(OngletMoniteurs);
        Dialogues.Information(FindForm(), resultat.Message);
    }

    /// <summary>Le moniteur redevient simple adhérent (confirmation), puis l'onglet Adhérents s'ouvre sur sa fiche.</summary>
    private async Task PasserAdherentAsync(Moniteur m)
    {
        if (!_garde.Exiger(FindForm(), ActionProtegee.Moniteurs, "Passer en adhérent")
            || !Dialogues.Confirmer(FindForm(),
                $"{m.NomComplet} n'est plus enseignant ?\n\nIl devient un simple adhérent et ne sera plus proposé comme responsable. Ses sorties restent à son nom."))
        {
            return;
        }

        var resultat = await _adherents.PasserAdherentAsync(m.Id);
        if (!resultat.Reussi)
        {
            Dialogues.Erreur(FindForm(), resultat.Message);
            return;
        }

        _adherentChoisi = resultat.Id;
        _choixAdherents = FiltreAdherents.Tous;
        await OuvrirOngletAsync(OngletAdherents);
        Dialogues.Information(FindForm(), resultat.Message);
    }

    /// <summary>Relit les listes et les nombres des onglets, en ouvrant l'onglet demandé.</summary>
    private async Task OuvrirOngletAsync(int onglet)
    {
        _onglets.Changement -= ChangementOnglet;
        _onglets.SelectedIndex = onglet; // Sans affichage ici : RechargerAsync affiche l'onglet une seule fois.
        _onglets.Changement += ChangementOnglet;
        await RechargerAsync();
    }

    private void ChangementOnglet(object? sender, EventArgs e) => Executer(AfficherGenreAsync);

    // ================================================================ Adhérents

    private bool _remplissageFormations;

    /// <summary>
    /// Liste du filtre « formation en cours » : les classes actives (elles changent dans l'onglet Classes), choix gardé si
    /// la classe existe encore.
    /// </summary>
    private async Task RemplirFormationsAsync()
    {
        var formations = await _adherents.ListerFormationsAsync();
        var choix = (_niveau.SelectedItem as Choix<string?>)?.Valeur;
        _remplissageFormations = true;
        try
        {
            _niveau.BeginUpdate();
            _niveau.Items.Clear();
            _niveau.Items.Add(new Choix<string?>(null, "Toutes les formations"));
            foreach (var f in formations)
            {
                _niveau.Items.Add(new Choix<string?>(f.Nom, f.Nom));
            }

            _niveau.Items.Add(new Choix<string?>(ServiceAdherents.SansNiveau, "Sans formation"));
            _niveau.SelectedItem = _niveau.Items.OfType<Choix<string?>>().FirstOrDefault(c => c.Valeur == choix) ?? _niveau.Items[0];
            _niveau.EndUpdate();
        }
        finally
        {
            _remplissageFormations = false;
        }
    }

    /// <summary>Cartes des adhérents (recherche et filtre), compteurs des filtres, puis la fiche de l'adhérent choisi.</summary>
    private async Task AfficherAdherentsAsync()
    {
        if (!Adherents)
        {
            return;
        }

        await RemplirFormationsAsync();
        var filtre = _choixAdherents;
        var liste = await _adherents.ListerAsync(_recherche.Champ.Text, filtre, (_niveau.SelectedItem as Choix<string?>)?.Valeur);
        if (IsDisposed || !Adherents)
        {
            return;
        }

        _filtre.Selection = PucesAdherents.FirstOrDefault(p => p.Filtre == filtre).Libelle; // Null pour « Tous ».
        _filtre.Afficher(liste.Tous, PucesAdherents.Select(p => (p.Libelle, p.Filtre switch
        {
            FiltreAdherents.MaterielDehors => liste.MaterielDehors,
            FiltreAdherents.NonRendu => liste.NonRendu,
            FiltreAdherents.Nitrox => liste.Nitrox,
            FiltreAdherents.CaciARenouveler => liste.CaciARenouveler,
            _ => liste.Desactives,
        })));
        _listeAdherents = liste.Fiches;
        if (_adherentChoisi is null || _listeAdherents.All(f => f.Adherent.Id != _adherentChoisi))
        {
            _adherentChoisi = _listeAdherents.FirstOrDefault()?.Adherent.Id;
        }

        _cartes.SuspendLayout();
        while (_cartes.Controls.Count > 0)
        {
            var c = _cartes.Controls[0];
            _cartes.Controls.RemoveAt(0);
            c.Dispose();
        }

        for (var i = 0; i < _listeAdherents.Count; i++)
        {
            var fiche = _listeAdherents[i];
            var carte = new CarteAdherent(fiche, i) { Selectionne = fiche.Adherent.Id == _adherentChoisi };
            carte.Click += (_, _) => Executer(() => ChoisirAdherentAsync(fiche.Adherent.Id));
            _cartes.Controls.Add(carte);
        }

        _cartes.ResumeLayout(true);
        await AfficherFicheAsync();
    }

    private async Task ChoisirAdherentAsync(long id)
    {
        _adherentChoisi = id;
        foreach (var carte in _cartes.Controls.OfType<CarteAdherent>())
        {
            carte.Selectionne = carte.AdherentId == id;
        }

        await AfficherFicheAsync();
    }

    /// <summary>
    /// Fiche d'un adhérent : niveau, licence, téléphone ; matériel qu'il a en ce moment ; matériel non rendu (code, sortie,
    /// date) ; dernières sorties nominatives. Seule action : Modifier (un adhérent n'est pas responsable de sortie).
    /// </summary>
    private void ConstruireFicheAdherent(DetailAdherent detail, Action<Control> ligne)
    {
        var a = detail.Adherent;
        var index = Math.Max(0, _listeAdherents.ToList().FindIndex(f => f.Adherent.Id == a.Id));

        var nom = Ui.Libelle(a.NomComplet, Theme.PoliceGrasse(16f));
        nom.Margin = new Padding(0, 0, 0, 4);
        var qualite = string.Join(" · ", new[] { "Adhérent", a.Niveau is { } formation ? $"formation {formation}" : null, a.Nitrox ? "certifié nitrox" : null, a.Actif ? null : "désactivé" }.Where(p => !string.IsNullOrWhiteSpace(p)));
        var textes = Ui.Pile(nom, Ui.Libelle(qualite, Theme.Police(9.75f), Theme.TexteSecondaireClair));
        if (a.Licence is { Length: > 0 } licence)
        {
            textes.Controls.Add(Ui.Libelle($"Licence {licence}", Theme.PoliceCode(9.5f), Theme.TexteDoux));
        }

        if (a.Telephone is { Length: > 0 } telephone)
        {
            textes.Controls.Add(Ui.Rangee(true, new Picto(Icones.Telephone, 18, Theme.TexteSecondaire, Color.Transparent) { Margin = new Padding(0, 2, 6, 0) },
                Ui.Libelle(telephone, Theme.Police(9.75f), Theme.TexteDoux)));
        }

        if (a.Caci && a.CaciExpiration is { } finCaci)
        {
            var jours = finCaci.DayNumber - _adherents.AujourdHui.DayNumber;
            textes.Controls.Add(detail.CaciExpire
                ? Ui.Libelle($"CACI expiré le {Libelles.Date(finCaci)} : ne sort plus de matériel", Theme.PoliceGrasse(9.75f), Theme.Erreur)
                : detail.CaciBientot
                    ? Ui.Libelle($"CACI à renouveler : fin le {Libelles.Date(finCaci)} ({(jours == 0 ? "aujourd'hui" : jours == 1 ? "demain" : $"dans {jours} jours")})", Theme.PoliceGrasse(9.75f), Theme.Avertissement)
                    : Ui.Libelle($"CACI valable jusqu'au {Libelles.Date(finCaci)}", Theme.Police(9.75f), Theme.TexteDoux));
        }

        var avatar = new AvatarAdherent(a, index, 72);
        var entete = Ui.Colonnes((avatar, SizeType.Absolute, 72 + 16), (textes, SizeType.Percent, 100));
        entete.Height = textes.Controls.Count > 4 ? 120 : 96;
        entete.Margin = new Padding(0, 0, 0, 18);
        avatar.Dock = DockStyle.None;
        avatar.Anchor = AnchorStyles.Left;
        textes.Dock = DockStyle.None;
        textes.Anchor = AnchorStyles.Left;
        ligne(entete);

        // Matériel qu'il a en ce moment.
        ligne(Section("Matériel qu'il a en ce moment"));
        if (detail.Dehors.Count == 0)
        {
            var rien = new BandeauMessage { Margin = new Padding(0, 0, 0, 18), Height = 60 };
            rien.Afficher(NiveauResultat.Succes, "Rien dehors.");
            ligne(rien);
        }
        else
        {
            ligne(ListeAdherent(detail.Dehors, StyleListe.ARendre, null));
        }

        // Matériel jamais rendu (manquant à la clôture d'une sortie).
        ligne(Section("Matériel non rendu"));
        if (detail.NonRendus.Count == 0)
        {
            var aucun = Ui.Libelle("Tout a été rendu.", Theme.Police(9.75f), Theme.TexteSecondaire);
            aucun.Margin = new Padding(0, 0, 0, 18);
            ligne(aucun);
        }
        else
        {
            ligne(ListeAdherent(detail.NonRendus, StyleListe.ARendre, "Non rendu"));
        }

        ligne(Section("Dernières sorties nominatives"));
        if (detail.Sorties.Count == 0)
        {
            var aucune = Ui.Libelle("Aucune sortie nominative.", Theme.Police(9.75f), Theme.TexteSecondaire);
            aucune.Margin = new Padding(0, 0, 0, 18);
            ligne(aucune);
        }

        foreach (var s in detail.Sorties)
        {
            ligne(new LigneSortieAdherent(s));
        }

        // Actions : Modifier, et Désactiver (il ne peut plus sortir de matériel) ou Réactiver. Pas de « Créer une sortie ».
        var modifier = new BoutonArrondi("Modifier", StyleBouton.Secondaire) { AutoSize = false, Margin = new Padding(0, 0, 10, 0) };
        _garde.Lier(modifier, ActionProtegee.Moniteurs);
        modifier.Click += (_, _) => Executer(() => ModifierAdherentAsync(a));
        var activer = new BoutonArrondi(a.Actif ? "Désactiver" : "Réactiver", StyleBouton.Secondaire, a.Actif ? Icones.Croix : Icones.Coche)
        {
            AutoSize = false,
            Margin = Padding.Empty, // Au bord de la fiche défilante : la pastille du cadenas rentre d'elle-même (PastilleVerrou).
            AccessibleDescription = a.Actif ? "Il ne pourra plus sortir de matériel" : "Il pourra de nouveau sortir du matériel",
        };
        if (a.Actif)
        {
            activer.CouleurIcone = Theme.Erreur;
        }

        _garde.Lier(activer, ActionProtegee.Moniteurs);
        activer.Click += (_, _) => Executer(() => ChangerActifAsync(a));
        var actions = Ui.Colonnes((modifier, SizeType.Percent, 50), (activer, SizeType.Percent, 50));
        actions.Height = Theme.HauteurBouton;
        actions.Margin = new Padding(0, 18, 0, 0);
        ligne(actions);
        ligne(BoutonPassage("Passer en enseignant", "Cet adhérent devient enseignant : il pourra être responsable des sorties", () => PasserMoniteurAsync(a)));
    }

    /// <summary>Désactive (avec confirmation) ou réactive l'adhérent, puis relit la liste.</summary>
    private async Task ChangerActifAsync(Adherent a)
    {
        if (!_garde.Exiger(FindForm(), ActionProtegee.Moniteurs, a.Actif ? "Désactiver un adhérent" : "Réactiver un adhérent"))
        {
            return;
        }

        if (a.Actif && !Dialogues.Confirmer(FindForm(),
            $"Désactiver {a.NomComplet} ?\n\nIl ne pourra plus sortir de matériel. Son historique est gardé et vous pourrez le réactiver."))
        {
            return;
        }

        var resultat = await _adherents.ChangerActifAsync(a.Id, !a.Actif);
        if (!resultat.Reussi)
        {
            Dialogues.Erreur(FindForm(), resultat.Message);
            return;
        }

        _adherentChoisi = a.Id;
        await RechargerAsync();
    }

    /// <summary>Équipements d'un adhérent : « A07 · Lac des Settons · 25/10/2026 », pastille « Non rendu » si besoin.</summary>
    private static ListeMateriel ListeAdherent(IReadOnlyList<PassageAdherent> passages, StyleListe style, string? alerte)
    {
        var liste = new ListeMateriel(style, "") { Margin = new Padding(0, 0, 0, 18) };
        liste.Afficher(passages.Select(p => new ElementMateriel(p.EquipementId, p.Code, p.Libelle, p.Categorie,
            Detail: $"{p.Intitule} · {Libelles.Date(p.DateSortie)}", Alerte: alerte, CouleurAlerte: Theme.Erreur)));
        liste.Height = (Math.Min(passages.Count, 4) * 58) + 4;
        return liste;
    }

    /// <summary>Ajout (a = null) ou modification d'un adhérent ; le rendre inactif le retire de la liste du départ.</summary>
    private async Task ModifierAdherentAsync(Adherent? a)
    {
        var titre = a is null ? "Ajouter un adhérent" : "Modifier l'adhérent";
        if (!_garde.Exiger(FindForm(), ActionProtegee.Moniteurs, titre))
        {
            return;
        }

        var prenom = new ChampSaisie { Valeur = a?.Prenom ?? "", TexteIndicatif = "Ex. Camille" };
        var nom = new ChampSaisie { Valeur = a?.Nom ?? "", TexteIndicatif = "Ex. Martin" };
        // Formation en cours : puces des classes du club (Membres › Classes) ; sa classe reste proposée si elle a été désactivée.
        var formations = await _adherents.ListerFormationsAsync(a?.ClasseId);
        var niveau = new ChoixPuces { AccessibleName = "Formation en cours", Margin = new Padding(0, 0, 0, 10) };
        niveau.Groupe("Classe", formations.Select(f => (f.Nom, f.Actif ? f.Nom : $"{f.Nom} (désactivée)")));
        niveau.Valeur = formations.FirstOrDefault(f => f.Id == a?.ClasseId)?.Nom;
        var licence = new ChampSaisie { Valeur = a?.Licence ?? "", TexteIndicatif = "A-12-345678", Icone = Icones.CarteLicence };
        var telephone = new ChampSaisie { Valeur = a?.Telephone ?? "", TexteIndicatif = "06 12 34 56 78", Icone = Icones.Telephone };
        var actif = new Interrupteur("Actif") { Checked = a?.Actif ?? true };
        var nitrox = new Interrupteur("Certifié nitrox") { Checked = a?.Nitrox ?? false };
        var caci = new Interrupteur("Contrôler le CACI") { Checked = a?.Caci ?? false };
        var caciExpiration = new ChampDate { Date = a?.CaciExpiration, AccessibleName = "CACI valable jusqu'au" };
        prenom.Champ.AccessibleName = "Prénom";
        nom.Champ.AccessibleName = "Nom";
        licence.Champ.AccessibleName = "Licence";
        licence.Champ.CharacterCasing = CharacterCasing.Upper;
        telephone.Champ.AccessibleName = "Téléphone";
        licence.Champ.Font = telephone.Champ.Font = Theme.PoliceCode(11f);
        foreach (var (champ, max) in new[] { (prenom, Adherent.LongueurMaxNom), (nom, Adherent.LongueurMaxNom),
            (licence, Adherent.LongueurMaxLicence), (telephone, Adherent.LongueurMaxTelephone) })
        {
            champ.Champ.MaxLength = max;
        }

        ResultatOperation? enregistre = null;
        using var fenetre = new FenetreFormulaire(titre, a is null ? "Ajouter" : "Enregistrer", async () =>
        {
            // Champs obligatoires vides : bordure rouge (le service refuse et donne le message).
            prenom.Erreur = prenom.Valeur.Trim().Length == 0;
            nom.Erreur = nom.Valeur.Trim().Length == 0;
            enregistre = await _adherents.EnregistrerAsync(new Adherent
            {
                Id = a?.Id ?? 0,
                Prenom = prenom.Valeur,
                Nom = nom.Valeur,
                ClasseId = formations.FirstOrDefault(f => f.Nom == niveau.Valeur)?.Id,
                Niveau = niveau.Valeur,
                Licence = licence.Valeur,
                Telephone = telephone.Valeur,
                Actif = actif.Checked,
                Nitrox = nitrox.Checked,
                Caci = caci.Checked,
                CaciExpiration = caciExpiration.Date,
            });
            if (!enregistre.Reussi && (prenom.Erreur || nom.Erreur))
            {
                (prenom.Erreur ? prenom : nom).Champ.Focus();
            }

            return enregistre;
        }, largeur: 660, marge: 24);

        // Maquette « Ajouter un adhérent » : en-tête qui montre la fiche pendant la saisie, trois sections numérotées.
        fenetre.EnTete(a is null ? "Nouvelle fiche membre" : "Fiche membre", icone: Icones.Personne);
        fenetre.Note("champs obligatoires");
        fenetre.Separations = true;
        if (a is null)
        {
            fenetre.Valider.Icone = Icones.Plus;
        }

        var identite = new SectionFormulaire(1, "Identité");
        identite.DeuxChamps("Prénom *", prenom, "Nom *", nom);
        identite.DeuxChamps("Téléphone", telephone, "N° de licence", licence);
        fenetre.Ajouter(identite);

        var sectionNiveau = new SectionFormulaire(2, "Formation en cours");
        if (formations.Count > 0)
        {
            sectionNiveau.Ajouter(niveau);
        }

        var aideNiveau = sectionNiveau.Ajouter(Ui.Libelle(formations.Count > 0
                ? "La classe qu'il suit : « Niveau 2 » = il prépare le niveau 2. Touche à nouveau une classe pour la retirer."
                : "Aucune classe : créez-les dans Membres › Classes (Niveau 1, Niveau 2… Autonome).",
            Theme.Police(9f), Theme.TexteSecondaire));
        aideNiveau.Margin = new Padding(0, 0, 0, 12);
        fenetre.Ajouter(sectionNiveau);

        var autorisations = new SectionFormulaire(3, "Autorisations matériel");
        autorisations.Ajouter(new CarteAutorisation(new TuileIcone(null, "O₂", Theme.Nitrox), "Certifié nitrox",
            "Peut prendre un bloc ou un détendeur nitrox.", nitrox, Theme.Nitrox));

        // CACI : interrupteur activé, la date de fin apparaît sous la carte ; une fois passée, il ne sort plus de matériel.
        var carteCaci = autorisations.Ajouter(new CarteAutorisation(new TuileIcone(Icones.CoeurPouls, null, Theme.Retour), "Contrôler le CACI", "", caci, Theme.Retour));
        var etatCaci = new Pastille("", Theme.Surface, Theme.TexteSecondaire, point: Theme.TexteSecondaire, police: Theme.PoliceGrasse(9.75f), hauteur: Theme.HauteurChamp)
        {
            Anchor = AnchorStyles.Left,
            Margin = new Padding(12, 0, 0, 0),
            TabStop = false,
        };
        caciExpiration.Dock = DockStyle.Fill;
        caciExpiration.Margin = Padding.Empty;
        var unAn = new BoutonArrondi("Délivré aujourd'hui (+1 an)", StyleBouton.Lien) { Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 0, 0) };
        unAn.Click += (_, _) => caciExpiration.Date = _adherents.AujourdHui.AddYears(1);
        var aideCaci = Ui.Libelle("Date passée : l'adhérent ne peut plus sortir de matériel.", Theme.Police(9f), Theme.TexteSecondaire);
        aideCaci.Margin = new Padding(0, 4, 0, 0);
        var libelleCaci = FenetreFormulaire.LibelleChamp("Valide jusqu'au *");
        var blocCaci = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Color.Transparent };
        blocCaci.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        blocCaci.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        blocCaci.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        blocCaci.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.HauteurChamp));
        blocCaci.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        blocCaci.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        blocCaci.Controls.Add(libelleCaci, 0, 0);
        blocCaci.Controls.Add(caciExpiration, 0, 1);
        blocCaci.Controls.Add(etatCaci, 1, 1);
        blocCaci.Controls.Add(unAn, 0, 2);
        blocCaci.Controls.Add(aideCaci, 0, 3);
        blocCaci.SetColumnSpan(libelleCaci, 2);
        blocCaci.SetColumnSpan(unAn, 2);
        blocCaci.SetColumnSpan(aideCaci, 2);
        carteCaci.Dessous(blocCaci);

        if (a is not null)
        {
            var carteActif = autorisations.Ajouter(new CarteAutorisation(new TuileIcone(Icones.Personne, null, Theme.AccentClair), "Adhérent actif", "", actif, Theme.Accent));
            void AfficherActif() => carteActif.Description = actif.Checked
                ? "Peut sortir du matériel."
                : "Désactivé : ne peut plus sortir de matériel ; son historique est gardé.";
            actif.CheckedChanged += (_, _) => AfficherActif();
            AfficherActif();
        }

        fenetre.Ajouter(autorisations);

        // Aperçu : initiales et nom dans l'en-tête, étiquettes niveau / NITROX / CACI.
        void Afficher()
        {
            var aujourdhui = _adherents.AujourdHui;
            var etat = ServiceAdherents.EtatCaciSaisi(caci.Checked, caciExpiration.Date, aujourdhui);
            var couleurCaci = etat?.Niveau switch
            {
                NiveauCaci.Valide => Theme.Retour,
                NiveauCaci.Bientot => Theme.Avertissement,
                NiveauCaci.Expire => Theme.Erreur,
                _ => Theme.TexteSecondaire,
            };

            sectionNiveau.TexteDroite = niveau.Valeur ?? "non renseignée";
            sectionNiveau.CouleurDroite = niveau.Valeur is null ? Theme.TexteSecondaire : Theme.AccentClair;
            carteCaci.DessousVisible = caci.Checked;
            carteCaci.Description = caci.Checked
                ? "Le matériel est bloqué si le certificat est périmé."
                : "Désactivé : aucun contrôle du certificat médical.";
            if (etat is not null)
            {
                etatCaci.Definir(etat.Texte, Theme.Teinte(couleurCaci, 0.12f), couleurCaci, couleurCaci);
            }

            var p = ServiceAdherents.MajusculesMots(prenom.Valeur);
            var n = ServiceAdherents.MajusculesMots(nom.Valeur);
            fenetre.Avatar.Initiales = ServiceAdherents.Initiales(p, n);
            fenetre.Titre(p.Length + n.Length > 0 ? $"{p} {n}".Trim() : titre);
            var etiquettes = new List<(string, Color)>();
            if (niveau.Valeur is { } nv)
            {
                etiquettes.Add((nv, Theme.AccentClair));
            }

            if (nitrox.Checked)
            {
                etiquettes.Add(("Nitrox", Theme.Nitrox));
            }

            if (etat?.Etiquette is { } tag)
            {
                etiquettes.Add((tag, couleurCaci));
            }

            fenetre.Etiquettes(etiquettes);
        }

        foreach (var champ in new[] { prenom, nom })
        {
            champ.Champ.TextChanged += (_, _) => Afficher();
            champ.Champ.Leave += (_, _) => champ.Valeur = ServiceAdherents.MajusculesMots(champ.Valeur); // « camille » → « Camille ».
        }

        // 0612345678 → 06 12 34 56 78 pendant la frappe (curseur gardé en fin de saisie).
        telephone.Champ.TextChanged += (_, _) =>
        {
            var forme = ServiceAdherents.FormaterTelephone(telephone.Valeur);
            if (forme != telephone.Valeur && telephone.Champ.SelectionStart == telephone.Valeur.Length)
            {
                telephone.Valeur = forme;
                telephone.Champ.SelectionStart = forme.Length;
            }
        };
        niveau.ValeurChangee += (_, _) => Afficher();
        nitrox.CheckedChanged += (_, _) => Afficher();
        caci.CheckedChanged += (_, _) => Afficher();
        caciExpiration.DateChangee += (_, _) => Afficher();
        Afficher();
        fenetre.Shown += (_, _) => prenom.Champ.Focus();

        if (fenetre.ShowDialog(FindForm()) == DialogResult.OK && enregistre?.Id is { } id)
        {
            _adherentChoisi = id;
            await RechargerAsync();
        }
    }

    /// <summary>« Modèle Excel à remplir » : classeur vide aux colonnes de l'import, à donner au club puis à réimporter.</summary>
    private async Task EnregistrerModeleAsync()
    {
        // Couleur de l'en-tête : celle du logo du club (fixe dans les deux thèmes).
        var c = Theme.LogoFonce;
        var formations = (await _adherents.ListerFormationsAsync()).Select(f => f.Nom).ToList();
        var colonnes = ServiceAdherents.ColonnesModele(formations).Select(m => new Infrastructure.ColonneClasseur(
            m.EnTete, m.Obligatoire ? $"Obligatoire. {m.Aide}" : m.Aide, m.Largeur, m.Choix)).ToList();
        await Infrastructure.Fichiers.EnregistrerXlsxAsync(FindForm(), "Enregistrer le modèle des adhérents", ServiceAdherents.NomFichierModele(_reglages.NomClub),
            chemin => Infrastructure.EcrivainTableur.EcrireModele(chemin, "Adhérents", colonnes, ServiceAdherents.ModeEmploiModele, $"{c.R:X2}{c.G:X2}{c.B:X2}"));
    }

    private async Task ImporterAsync()
    {
        if (!_garde.Exiger(FindForm(), ActionProtegee.Moniteurs, "Importer la liste des adhérents"))
        {
            return;
        }

        using var dialogue = new OpenFileDialog
        {
            Title = "Importer la liste des adhérents",
            Filter = Infrastructure.LecteurTableur.Filtre,
            CheckFileExists = true,
        };
        if (dialogue.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        IReadOnlyList<IReadOnlyList<string>> tableau;
        try
        {
            var chemin = dialogue.FileName;
            tableau = await Task.Run(() => Infrastructure.LecteurTableur.Lire(chemin));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException)
        {
            Dialogues.Erreur(FindForm(), "Ce fichier n'a pas pu être lu. Enregistrez la liste en .xlsx ou en .csv, puis réessayez.");
            return;
        }

        var resultat = await _adherents.ImporterAsync(tableau);
        if (IsDisposed)
        {
            return;
        }

        if (resultat.Reussi)
        {
            Dialogues.Information(FindForm(), resultat.Message);
            await RechargerAsync();
        }
        else
        {
            Dialogues.Erreur(FindForm(), resultat.Message);
        }
    }

    private Carte CarteDehors(SortieDehors sd)
    {
        var s = sd.Sortie;
        var carte = new Carte
        {
            BackColor = Theme.Teinte(Theme.Depart, 0.08f),
            CouleurBordure = Theme.Teinte(Theme.Depart, 0.30f),
            Padding = new Padding(14),
            Margin = new Padding(0, 0, 0, 12),
            Rayon = 18,
        };
        var titre = Ui.Libelle($"{s.DateSortie.ToString("dd/MM", Libelles.Culture)} · {s.Intitule}", Theme.PoliceTitre(11.25f));
        titre.AutoEllipsis = true;
        var nombre = Ui.Libelle(Libelles.Pluriel(sd.Dehors.Count, "équipement") + " à rendre", Theme.Police(9f), Theme.DepartClair);
        var textes = Ui.Pile(titre, nombre);
        var aller = new BoutonArrondi(s.Statut == StatutSortie.EnPreparation ? "Continuer le départ" : "Scanner le retour", StyleBouton.Lien, Icones.ChevronDroite, iconeApres: true)
        {
            Margin = Padding.Empty,
            Height = Theme.HauteurCliquable,
            Font = Theme.PoliceTitre(9.75f),
        };
        aller.Click += (_, _) =>
        {
            if (s.Statut == StatutSortie.EnPreparation)
            {
                _navigation.AllerA<EcranDepart>(s.Id);
            }
            else
            {
                _navigation.AllerA<EcranRetour>(s.Id);
            }
        };
        var haut = Ui.Colonnes((textes, SizeType.Percent, 100), (aller, SizeType.AutoSize, 0));
        textes.Dock = DockStyle.None;
        textes.Anchor = AnchorStyles.Left;
        aller.Dock = DockStyle.None;
        aller.Anchor = AnchorStyles.Right;

        var liste = new ListeMateriel(StyleListe.ARendre, "") { BackColor = carte.BackColor };
        liste.Afficher(sd.Dehors.Select(l => new ElementMateriel(l.EquipementId, l.Code, l.Libelle, l.Categorie)));
        var visibles = Math.Min(sd.Dehors.Count, 5);
        carte.Controls.Add(Ui.Colonne((haut, SizeType.Absolute, 50), (liste, SizeType.Percent, 100)));
        carte.Height = 28 + 50 + 8 + (visibles * 58);
        return carte;
    }

    private static Label Section(string texte)
    {
        var l = Ui.LibelleChamp(texte);
        l.Margin = new Padding(0, 0, 0, 10);
        return l;
    }

    private static string Qualite(Moniteur m, int sortiesSaison)
    {
        var parties = new List<string>();
        if (m.EstClasse)
        {
            parties.Add("Classe");
            parties.Add($"{Libelles.Pluriel(sortiesSaison, "sortie")} cette saison");
        }
        else
        {
            parties.Add("Enseignant");
            if (!string.IsNullOrWhiteSpace(m.Niveau))
            {
                parties.Add(m.Niveau);
            }

            if (m.InspecteurTiv)
            {
                parties.Add("TIV");
            }
        }

        if (!m.Actif)
        {
            parties.Add("inactif");
        }

        return string.Join(" · ", parties);
    }

    /// <summary>Ajout (m = null) ou modification d'un moniteur ou d'une classe.</summary>
    private Task ModifierAsync(Moniteur? m, GenreResponsable genre) =>
        genre == GenreResponsable.Classe ? ModifierClasseAsync(m) : ModifierMoniteurAsync(m);

    /// <summary>
    /// Maquette « Ajouter un moniteur » : en-tête qui montre la fiche pendant la saisie (initiales, niveau, INSPECTEUR TIV),
    /// trois sections : identité, niveau d'encadrement (puces E1… MF2 ou autre niveau tapé), rôles au club.
    /// </summary>
    private async Task ModifierMoniteurAsync(Moniteur? m)
    {
        var titre = m is null ? "Ajouter un enseignant" : "Modifier l'enseignant";
        if (!_garde.Exiger(FindForm(), ActionProtegee.Moniteurs, titre))
        {
            return;
        }

        var prenom = new ChampSaisie { Valeur = m?.Prenom ?? "", TexteIndicatif = "Ex. Pascal" };
        var nom = new ChampSaisie { Valeur = m?.Nom ?? "", TexteIndicatif = "Ex. Durand" };
        var telephone = new ChampSaisie { Valeur = m?.Telephone ?? "", TexteIndicatif = "06 12 34 56 78", Icone = Icones.Telephone };
        var licence = new ChampSaisie { Valeur = m?.Licence ?? "", TexteIndicatif = "A-12-345678", Icone = Icones.CarteLicence };
        prenom.Champ.AccessibleName = "Prénom";
        nom.Champ.AccessibleName = "Nom";
        telephone.Champ.AccessibleName = "Téléphone";
        licence.Champ.AccessibleName = "Licence";
        licence.Champ.CharacterCasing = CharacterCasing.Upper;
        licence.Champ.MaxLength = Math.Max(ServiceMoniteurs.LongueurMaxLicence, licence.Valeur.Length);
        licence.Champ.Font = telephone.Champ.Font = Theme.PoliceCode(11f);

        // Niveau : une puce s'il est proposé (E1… MF2), sinon dans « Autre niveau » ; l'un efface l'autre.
        var propose = ServiceMoniteurs.NiveauPropose(m?.Niveau);
        var niveaux = new ChoixPuces { AccessibleName = "Niveau d'encadrement", Margin = new Padding(0, 0, 0, 12) };
        niveaux.GroupeDetaille(ServiceMoniteurs.NiveauxEncadrement.Select(n => (n.Code, n.Qualite)), Theme.Encadrement, Theme.TexteSurEncadrement);
        niveaux.Valeur = propose;
        var autre = new ChampSaisie { Valeur = propose is null ? m?.Niveau ?? "" : "", TexteIndicatif = "Ex. BEES 1", Size = new Size(170, Theme.HauteurCliquable) };
        autre.Champ.AccessibleName = "Autre niveau";
        autre.Champ.MaxLength = Math.Max(ServiceMoniteurs.LongueurMaxAutreNiveau, autre.Valeur.Length);
        autre.Champ.CharacterCasing = CharacterCasing.Upper;
        var libelleAutre = Ui.Libelle("Autre niveau :", Theme.Police(9f), Theme.TexteSecondaire);
        libelleAutre.Margin = new Padding(0, 13, 10, 0);
        autre.Margin = Padding.Empty;
        var ligneAutre = Ui.Rangee(true, libelleAutre, autre);
        ligneAutre.Margin = new Padding(0, 0, 0, 14);

        var tiv = new Interrupteur("Inspecteur TIV") { Checked = m?.InspecteurTiv ?? false };
        var actif = new Interrupteur("Actif") { Checked = m?.Actif ?? true };
        string? Niveau() => niveaux.Valeur ?? (autre.Valeur.Trim() is { Length: > 0 } n ? n : null);

        ResultatOperation? enregistre = null;
        using var fenetre = new FenetreFormulaire(titre, m is null ? "Ajouter" : "Enregistrer", async () =>
        {
            // Champs obligatoires vides : bordure rouge (le service refuse et donne le message).
            prenom.Erreur = prenom.Valeur.Trim().Length == 0;
            nom.Erreur = nom.Valeur.Trim().Length == 0;
            enregistre = await _moniteurs.EnregistrerAsync(new Moniteur
            {
                Id = m?.Id ?? 0,
                Genre = GenreResponsable.Moniteur,
                Prenom = ServiceAdherents.MajusculesMots(prenom.Valeur),
                Nom = ServiceAdherents.MajusculesMots(nom.Valeur),
                Niveau = Niveau(),
                Telephone = telephone.Valeur,
                Licence = licence.Valeur,
                InspecteurTiv = tiv.Checked,
                Actif = actif.Checked,
            });
            if (!enregistre.Reussi && (prenom.Erreur || nom.Erreur))
            {
                (prenom.Erreur ? prenom : nom).Champ.Focus();
            }

            return enregistre;
        }, largeur: 640, marge: 24);

        fenetre.EnTete(m is null ? "Nouvelle fiche encadrant" : "Fiche encadrant", icone: Icones.Personne);
        fenetre.Avatar.Colorer(Theme.Encadrement, Theme.TexteSurEncadrement);
        fenetre.Note("champs obligatoires");
        fenetre.Separations = true;
        if (m is null)
        {
            fenetre.Valider.Icone = Icones.Plus;
        }

        var identite = new SectionFormulaire(1, "Identité");
        identite.DeuxChamps("Prénom *", prenom, "Nom *", nom);
        identite.DeuxChamps("Téléphone", telephone, "N° de licence", licence);
        fenetre.Ajouter(identite);

        var sectionNiveau = new SectionFormulaire(2, "Niveau d'encadrement");
        sectionNiveau.Ajouter(niveaux);
        sectionNiveau.Ajouter(ligneAutre);
        fenetre.Ajouter(sectionNiveau);

        var roles = new SectionFormulaire(3, "Rôles au club");
        roles.Ajouter(new CarteAutorisation(new TuileIcone(Icones.InspecteurTiv, null, Theme.Avertissement), "Inspecteur TIV",
            "Peut faire les TIV des blocs du club.", tiv, Theme.Avertissement));
        if (m is not null)
        {
            var carteActif = roles.Ajouter(new CarteAutorisation(new TuileIcone(Icones.Personne, null, Theme.AccentClair), "Enseignant actif", "", actif, Theme.Accent));
            void AfficherActif() => carteActif.Description = actif.Checked
                ? "Proposé comme responsable des sorties."
                : "Inactif : plus proposé pour les sorties ; son historique est gardé.";
            actif.CheckedChanged += (_, _) => AfficherActif();
            AfficherActif();
        }

        fenetre.Ajouter(roles);

        // Aperçu : initiales et nom dans l'en-tête, étiquettes niveau / INSPECTEUR TIV / INACTIF.
        void Afficher()
        {
            var niveau = Niveau();
            sectionNiveau.TexteDroite = niveau ?? "non renseigné";
            sectionNiveau.CouleurDroite = niveau is null ? Theme.TexteSecondaire : Theme.Encadrement;
            autre.CouleurBordure = niveaux.Valeur is null && niveau is not null ? Theme.Encadrement : null;

            var p = ServiceAdherents.MajusculesMots(prenom.Valeur);
            var n = ServiceAdherents.MajusculesMots(nom.Valeur);
            fenetre.Avatar.Initiales = ServiceAdherents.Initiales(p, n);
            fenetre.Titre(p.Length + n.Length > 0 ? $"{p} {n}".Trim() : titre);
            var etiquettes = new List<(string, Color)>();
            if (niveau is not null)
            {
                etiquettes.Add((niveau, Theme.Encadrement));
            }

            if (tiv.Checked)
            {
                etiquettes.Add(("Inspecteur TIV", Theme.Avertissement));
            }

            if (!actif.Checked)
            {
                etiquettes.Add(("Inactif", Theme.TexteSecondaire));
            }

            fenetre.Etiquettes(etiquettes);
        }

        foreach (var champ in new[] { prenom, nom })
        {
            champ.Champ.TextChanged += (_, _) => Afficher();
            champ.Champ.Leave += (_, _) => champ.Valeur = ServiceAdherents.MajusculesMots(champ.Valeur); // « pascal » → « Pascal ».
        }

        // 0612345678 → 06 12 34 56 78 pendant la frappe (curseur gardé en fin de saisie).
        telephone.Champ.TextChanged += (_, _) =>
        {
            var forme = ServiceAdherents.FormaterTelephone(telephone.Valeur);
            if (forme != telephone.Valeur && telephone.Champ.SelectionStart == telephone.Valeur.Length)
            {
                telephone.Valeur = forme;
                telephone.Champ.SelectionStart = forme.Length;
            }
        };
        niveaux.ValeurChangee += (_, _) =>
        {
            if (niveaux.Valeur is not null)
            {
                autre.Valeur = "";
            }

            Afficher();
        };
        autre.Champ.TextChanged += (_, _) =>
        {
            if (autre.Valeur.Trim().Length > 0)
            {
                niveaux.Valeur = null;
            }

            Afficher();
        };
        tiv.CheckedChanged += (_, _) => Afficher();
        actif.CheckedChanged += (_, _) => Afficher();
        Afficher();
        fenetre.Shown += (_, _) => prenom.Champ.Focus();

        if (fenetre.ShowDialog(FindForm()) == DialogResult.OK && enregistre?.Id is { } id)
        {
            _selection[GenreResponsable.Moniteur] = id;
            await RechargerAsync();
        }
    }

    /// <summary>
    /// Maquette « Ajouter une classe » : nom et abréviation (proposée d'après le nom tant qu'elle n'est pas tapée, « AUTO »),
    /// exemples de noms, couleur de la pastille et aperçu.
    /// </summary>
    private async Task ModifierClasseAsync(Moniteur? m)
    {
        var titre = m is null ? "Ajouter une classe" : "Modifier la classe";
        if (!_garde.Exiger(FindForm(), ActionProtegee.Moniteurs, titre))
        {
            return;
        }

        var autres = _tous.Select(f => f.Moniteur).Where(c => c.EstClasse && c.Id != m?.Id).ToList();
        var eleves = m is null ? 0 : (await _adherents.ListerAsync(null, FiltreAdherents.Tous)).Fiches.Count(f => f.Adherent.ClasseId == m.Id);
        var sorties = m is null ? 0 : _tous.FirstOrDefault(f => f.Moniteur.Id == m.Id)?.SortiesSaison ?? 0;
        if (IsDisposed)
        {
            return;
        }

        var nom = new ChampSaisie { Valeur = m?.NomComplet ?? "", TexteIndicatif = "Ex. Niveau 1" };
        nom.Champ.AccessibleName = "Nom de la classe";
        nom.Champ.MaxLength = ServiceMoniteurs.LongueurMaxClasse;
        var compteur = Ui.Libelle("0/4", Theme.PoliceGrasse(9f), Theme.TexteSecondaire);
        compteur.Margin = Padding.Empty;
        var abrege = new ChampSaisie { Valeur = m?.Abrege ?? "", ADroite = compteur };
        abrege.Champ.AccessibleName = "Abréviation de la pastille";
        abrege.Champ.MaxLength = ServiceMoniteurs.LongueurMaxAbrege;
        abrege.Champ.CharacterCasing = CharacterCasing.Upper;
        abrege.Champ.TextAlign = HorizontalAlignment.Center;
        abrege.Champ.Font = Theme.PoliceGrasse(12.75f);

        // « AUTO » : l'abréviation suit le nom tant qu'elle n'a pas été tapée (ou qu'elle a été effacée).
        var auto = m is null || m.Abrege.Length == 0 || m.Abrege == ServiceMoniteurs.AbregeDe(m.NomComplet);
        var badgeAuto = new Pastille("AUTO", Theme.Teinte(Theme.Accent, 0.16f), Theme.AccentClair, police: Theme.PoliceGrasse(7f), hauteur: 18)
        {
            Margin = new Padding(8, 0, 0, 0),
            TabStop = false,
        };
        var actif = new Interrupteur("Active") { Checked = m?.Actif ?? true };
        var couleur = new ChoixCouleur(Moniteur.CouleursClasse.Select(k => (k, char.ToUpper(k[0], Libelles.Culture) + k[1..], Theme.CouleurClasse(k))))
        {
            AccessibleName = "Couleur de la pastille",
            Margin = new Padding(0, 0, 0, 18),
        };
        couleur.Valeur = m?.CouleurEffective ?? ServiceMoniteurs.CouleurProposee(autres);

        ResultatOperation? enregistre = null;
        using var fenetre = new FenetreFormulaire(titre, m is null ? "Ajouter" : "Enregistrer", async () =>
        {
            nom.Erreur = nom.Valeur.Trim().Length == 0;
            enregistre = await _moniteurs.EnregistrerAsync(new Moniteur
            {
                Id = m?.Id ?? 0,
                Genre = GenreResponsable.Classe,
                Nom = nom.Valeur,
                Abrege = abrege.Valeur,
                Couleur = couleur.Valeur,
                Actif = actif.Checked,
            });
            if (!enregistre.Reussi && nom.Erreur)
            {
                nom.Champ.Focus();
            }

            return enregistre;
        }, largeur: 640, marge: 24);

        fenetre.EnTete("Un groupe d'élèves qui sort ensemble, avec sa pastille.", icone: Icones.Groupe);
        fenetre.Avatar.Tuile = true;
        fenetre.Avatar.Size = new Size(46, 46);
        fenetre.Separations = true;
        if (m is null)
        {
            fenetre.Valider.Icone = Icones.Plus;
        }

        // Nom (toute la place) | Abréviation (150 px), libellés au-dessus.
        var libelleAbrege = Ui.Rangee(true, Ui.LibelleChamp("Abréviation"), badgeAuto);
        libelleAbrege.Controls[0].Margin = Padding.Empty;
        libelleAbrege.Margin = new Padding(7, 0, 0, 8);
        var libelleNom = FenetreFormulaire.LibelleChamp("Nom de la classe *");
        libelleNom.Margin = new Padding(0, 0, 7, 8);
        nom.Margin = new Padding(0, 0, 7, 0);
        abrege.Margin = new Padding(7, 0, 0, 0);
        var rangee = new TableLayoutPanel { ColumnCount = 2, RowCount = 2, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 18) };
        rangee.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rangee.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 164));
        rangee.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rangee.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.HauteurChamp));
        nom.Dock = abrege.Dock = DockStyle.Fill;
        libelleAbrege.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        rangee.Controls.Add(libelleNom, 0, 0);
        rangee.Controls.Add(libelleAbrege, 1, 0);
        rangee.Controls.Add(nom, 0, 1);
        rangee.Controls.Add(abrege, 1, 1);
        fenetre.Ajouter(rangee);

        void PoserAbrege(string valeur)
        {
            var avant = auto;
            abrege.Valeur = valeur; // Son TextChanged remettrait « auto » à faux.
            auto = avant;
        }

        var exemples = m is null ? ServiceMoniteurs.ExemplesClasses(autres.Select(c => c.NomComplet)) : [];
        if (exemples.Count > 0)
        {
            var puces = new ChoixPuces { Actions = true, AccessibleName = "Exemples de noms de classe", Margin = new Padding(0, 0, 0, 18) };
            puces.Groupe("", exemples.Select(e => (e, e)));
            puces.PuceActivee += (_, exemple) =>
            {
                auto = true;
                nom.Valeur = exemple;
                PoserAbrege(ServiceMoniteurs.AbregeDe(exemple));
                nom.Champ.Focus();
                nom.Champ.SelectionStart = exemple.Length;
            };
            fenetre.Ajouter(Ui.LibelleChamp("Exemples"));
            fenetre.Ajouter(puces);
        }

        fenetre.Ajouter(Ui.LibelleChamp("Couleur de la pastille"));
        fenetre.Ajouter(couleur);

        var apercu = new ApercuClasse { Margin = new Padding(0, 0, 0, 4) };
        apercu.Detail = $"{Libelles.Pluriel(eleves, "élève")} · {Libelles.Pluriel(sorties, "sortie")} cette saison";
        fenetre.Ajouter(apercu);

        if (m is not null)
        {
            var carteActif = new CarteAutorisation(new TuileIcone(Icones.Groupe, null, Theme.AccentClair), "Classe active", "", actif, Theme.Accent)
            {
                Margin = new Padding(0, 14, 0, 0),
            };
            void AfficherActif() => carteActif.Description = actif.Checked
                ? "Proposée comme responsable des sorties et comme formation des adhérents."
                : "Inactive : plus proposée ; ses sorties et ses élèves sont gardés.";
            actif.CheckedChanged += (_, _) => AfficherActif();
            AfficherActif();
            fenetre.Ajouter(carteActif);
        }

        void Afficher()
        {
            var valeur = abrege.Valeur.Trim();
            badgeAuto.Visible = auto && valeur.Length > 0;
            compteur.Text = $"{valeur.Length}/{ServiceMoniteurs.LongueurMaxAbrege}";
            compteur.ForeColor = valeur.Length == ServiceMoniteurs.LongueurMaxAbrege ? Theme.AccentClair : Theme.TexteSecondaire;
            abrege.PerformLayout(); // Le compteur a pu changer de largeur.
            apercu.Definir(valeur.Length > 0 ? valeur : "?", nom.Valeur.Trim(), couleur.Valeur, actif.Checked);
        }

        nom.Champ.TextChanged += (_, _) =>
        {
            if (auto)
            {
                PoserAbrege(ServiceMoniteurs.AbregeDe(nom.Valeur));
            }

            Afficher();
        };
        abrege.Champ.TextChanged += (_, _) =>
        {
            var sansEspace = abrege.Valeur.Replace(" ", "", StringComparison.Ordinal);
            if (sansEspace != abrege.Valeur)
            {
                abrege.Valeur = sansEspace;
                abrege.Champ.SelectionStart = sansEspace.Length;
                return; // Le TextChanged suivant fait le reste.
            }

            auto = sansEspace.Length == 0;
            Afficher();
        };
        couleur.ValeurChangee += (_, _) => Afficher();
        actif.CheckedChanged += (_, _) => Afficher();
        Afficher();
        fenetre.Shown += (_, _) => nom.Champ.Focus();

        if (fenetre.ShowDialog(FindForm()) == DialogResult.OK && enregistre?.Id is { } id)
        {
            _selection[GenreResponsable.Classe] = id;
            await RechargerAsync();
        }
    }

    /// <summary>
    /// Aperçu de la pastille d'une classe (fenêtre « Ajouter une classe ») : grande pastille, nom et détail, puce comme dans
    /// une liste, rappel de l'usage. Dessiné en un seul contrôle, hauteur fixe.
    /// </summary>
    private sealed class ApercuClasse : ControleDessine
    {
        private string _abrege = "?";
        private string _nom = "";
        private string _couleur = Moniteur.CouleursClasse[0];
        private bool _actif = true;

        public ApercuClasse()
        {
            Height = 150;
            TabStop = false;
            AccessibleRole = AccessibleRole.Graphic;
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Detail { get; set; } = "";

        public void Definir(string abrege, string nom, string couleur, bool actif)
        {
            (_abrege, _nom, _couleur, _actif) = (abrege, nom, couleur, actif);
            AccessibleName = $"Aperçu : pastille {abrege}, {(nom.Length > 0 ? nom : "classe sans nom")}";
            Invalidate();
        }

        protected override void Peindre(Graphics g)
        {
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var chemin = Dessin.Arrondi(r, E(18)))
            {
                using var fond = new SolidBrush(Theme.Sur(Theme.Surface, Theme.Fond, 0.3f));
                g.FillPath(fond, chemin);
                using var bord = new Pen(Theme.Bordure, E(1));
                g.DrawPath(bord, chemin);
            }

            var x = E(16);
            Dessin.Texte(g, "APERÇU", Theme.PoliceGrasse(8.25f), Rectangle.Round(new RectangleF(x, E(14), Width - (2 * x), E(18))), Theme.TexteSecondaire);

            // Grande pastille, nom et détail.
            var degrade = Theme.DegradeClasse(_couleur);
            var y = E(42);
            var d = E(52);
            Avatar.Dessiner(g, new RectangleF(x, y, d, d), _abrege, degrade, _actif, true, this);
            var nom = _nom.Length > 0 ? _nom : "Nom de la classe";

            // Puce comme dans une liste, à droite (si la place le permet).
            var policePuce = Theme.PoliceTitre(9.75f);
            var hPuce = E(40);
            var lPuce = E(5 + 30 + 8 + 14) + TextRenderer.MeasureText(_nom.Length > 0 ? _nom : "Classe", policePuce).Width;
            var xPuce = Width - x - lPuce;
            var gauche = x + d + E(14);
            var avecPuce = xPuce - gauche > E(160);
            var largeurTextes = (int)((avecPuce ? xPuce - E(12) : Width - x) - gauche);
            Dessin.Texte(g, nom, Theme.PoliceTitre(12f), Rectangle.Round(new RectangleF(gauche, y + E(4), largeurTextes, E(24))), _nom.Length > 0 ? Theme.Texte : Theme.TexteSecondaire);
            Dessin.Texte(g, Detail, Theme.Police(9.75f), Rectangle.Round(new RectangleF(gauche, y + E(28), largeurTextes, E(20))), Theme.TexteSecondaireClair);
            if (avecPuce)
            {
                var zone = new RectangleF(xPuce, y + ((d - hPuce) / 2), lPuce, hPuce);
                using (var chemin = Dessin.Arrondi(zone, hPuce / 2))
                {
                    using var fond = new SolidBrush(ChampSaisie.FondChamp);
                    g.FillPath(fond, chemin);
                    using var bord = new Pen(Theme.Bordure, E(1));
                    g.DrawPath(bord, chemin);
                }

                var petite = E(30);
                Avatar.Dessiner(g, new RectangleF(zone.X + E(5), zone.Y + ((hPuce - petite) / 2), petite, petite), _abrege, degrade, _actif, true, this);
                var xTexte = zone.X + E(5) + petite + E(8);
                Dessin.Texte(g, _nom.Length > 0 ? _nom : "Classe", policePuce, Rectangle.Round(new RectangleF(xTexte, zone.Y, zone.Right - xTexte - E(10), hPuce)), Theme.Texte);
            }

            Dessin.Texte(g, "La pastille représente la classe dans Membres. 4 caractères au plus.", Theme.Police(9f),
                Rectangle.Round(new RectangleF(x, Height - E(36), Width - (2 * x), E(20))), Theme.TexteSecondaire);
        }
    }

    /// <summary>Pastille avec les initiales en dégradé : ronde pour un moniteur, carrée arrondie avec l'abréviation pour une classe.</summary>
    internal sealed class Avatar : ControleDessine
    {
        private readonly string _initiales;
        private readonly (Color Clair, Color Fonce) _degrade;
        private readonly bool _actif;
        private readonly bool _classe;

        public Avatar(Moniteur m, int index, int taille)
        {
            _initiales = Initiales(m);
            _degrade = Degrade(m, index);
            _actif = m.Actif;
            _classe = m.EstClasse;
            Size = new Size(taille, taille);
            AccessibleRole = AccessibleRole.Graphic;
            AccessibleName = m.NomComplet;
        }

        public static string Initiales(Moniteur m) => m.EstClasse
            ? (m.Abrege.Length > 0 ? m.Abrege : ServiceMoniteurs.AbregeDe(m.NomComplet))
            : $"{m.Prenom.FirstOrDefault()}{m.Nom.FirstOrDefault()}".ToUpper(Libelles.Culture);

        /// <summary>Classe : la couleur choisie pour sa pastille ; moniteur : d'après sa place dans la liste.</summary>
        public static (Color Clair, Color Fonce) Degrade(Moniteur m, int index) =>
            m.EstClasse ? Theme.DegradeClasse(m.CouleurEffective) : Theme.DegradesAvatar[index % Theme.DegradesAvatar.Length];

        protected override void Peindre(Graphics g) => Dessiner(g, new RectangleF(0, 0, Width - 1, Height - 1), _initiales, _degrade, _actif, _classe, this);

        public static void Dessiner(Graphics g, RectangleF r, string initiales, (Color Clair, Color Fonce) degrade, bool actif, bool classe, Control c)
        {
            var (clair, fonce) = actif ? degrade : (Theme.TexteSecondaire, Theme.SurfaceActive);
            using (var pinceau = new LinearGradientBrush(r, clair, fonce, 40f))
            {
                if (classe)
                {
                    using var forme = Dessin.Arrondi(r, r.Height * 0.3f);
                    g.FillPath(pinceau, forme);
                }
                else
                {
                    g.FillEllipse(pinceau, r);
                }
            }

            var diviseur = initiales.Length > 2 ? 3.6f : 2.9f;
            Dessin.Texte(g, initiales, Theme.PoliceGrasse(r.Height * 72 / c.DeviceDpi / diviseur), Rectangle.Round(r), Theme.TexteSurAvatar, centre: true);
        }
    }

    /// <summary>Carte d'un moniteur ou d'une classe dans la grille (bouton accessible au clavier).</summary>
    private sealed class CarteMoniteur : Button
    {
        private readonly FicheMoniteur _fiche;
        private readonly int _index;
        private bool _selectionne;
        private bool _survol;

        public CarteMoniteur(FicheMoniteur fiche, int index)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseMnemonic = false;
            Cursor = Cursors.Hand;
            Margin = Padding.Empty;
            _fiche = fiche;
            _index = index;
            var m = fiche.Moniteur;
            AccessibleName = $"{m.NomComplet}, {Libelles.Pluriel(fiche.SortiesSaison, "sortie")} cette saison, {Libelles.Pluriel(fiche.NbDehors, "équipement")} dehors";
        }

        public long MoniteurId => _fiche.Moniteur.Id;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selectionne
        {
            get => _selectionne;
            set
            {
                _selectionne = value;
                AccessibleDescription = value ? "Fiche affichée" : null;
                Invalidate();
            }
        }

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
            var m = _fiche.Moniteur;

            var r = new RectangleF(0.75f, 0.75f, Width - 2.5f, Height - 2.5f);
            var fond = _selectionne ? Theme.Teinte(Theme.Accent, 0.10f) : _survol ? Theme.Sur(Theme.Surface, Theme.AccentClair, 0.05f) : Theme.Surface;
            using (var chemin = Dessin.Arrondi(r, E(22)))
            {
                using var pinceau = new SolidBrush(fond);
                g.FillPath(pinceau, chemin);
                using var bord = new Pen(_selectionne ? Theme.Sur(Theme.Surface, Theme.Accent, 0.55f) : Theme.Bordure, E(_selectionne ? 1.5f : 1));
                g.DrawPath(bord, chemin);
                if (Focused && ShowFocusCues)
                {
                    using var focus = new Pen(Theme.Accent, E(1.5f));
                    using var interieur = Dessin.Arrondi(RectangleF.Inflate(r, -3, -3), E(19));
                    g.DrawPath(focus, interieur);
                }
            }

            // Pastille, nom, niveau et TIV (« CLASSE » pour une classe).
            var x = E(18);
            var y = E(18);
            var d = E(52);
            Avatar.Dessiner(g, new RectangleF(x, y, d, d), Avatar.Initiales(m), Avatar.Degrade(m, _index), m.Actif, m.EstClasse, this);
            var gauche = (int)(x + d + E(14));
            var nom = Theme.PoliceTitre(12.75f);
            Dessin.Texte(g, m.NomComplet, nom, new Rectangle(gauche, (int)(y + E(2)), Width - gauche - (int)E(12), (int)E(26)), m.Actif ? Theme.Texte : Theme.TexteSecondaire);
            var px = (float)gauche;
            var py = y + E(32);
            if (m.EstClasse)
            {
                px = Pastille(g, "CLASSE", px, py, Theme.AccentClair) + E(6);
            }
            else if (!string.IsNullOrWhiteSpace(m.Niveau))
            {
                px = Pastille(g, m.Niveau.ToUpper(Libelles.Culture), px, py, Theme.AccentClair) + E(6);
            }

            if (m.InspecteurTiv)
            {
                px = Pastille(g, "TIV", px, py, Theme.Avertissement) + E(6);
            }

            if (!m.Actif)
            {
                Pastille(g, "INACTIF", px, py, Theme.TexteSecondaire);
            }

            // Deux encadrés : sorties de la saison, équipements dehors.
            var hb = E(58);
            var yb = Height - E(18) - hb;
            var lb = (Width - (2 * x) - E(8)) / 2;
            Encadre(g, new RectangleF(x, yb, lb, hb), _fiche.SortiesSaison.ToString(Libelles.Culture), "sorties cette saison",
                Theme.Sur(fond, Theme.Fond, 0.4f), Theme.Texte, Theme.TexteSecondaire);
            var dehors = _fiche.NbDehors > 0;
            Encadre(g, new RectangleF(x + lb + E(8), yb, lb, hb), _fiche.NbDehors.ToString(Libelles.Culture), "équip. dehors",
                Theme.Sur(fond, dehors ? Theme.Depart : Theme.Retour, dehors ? 0.12f : 0.10f), dehors ? Theme.DepartClair : Theme.Retour, dehors ? Theme.DepartClair : Theme.Retour);
        }

        private float Pastille(Graphics g, string texte, float x, float y, Color couleur)
        {
            var police = Theme.PoliceTitre(7.5f);
            var l = TextRenderer.MeasureText(g, texte, police, Size.Empty, TextFormatFlags.NoPadding).Width + Dessin.Echelle(this, 16);
            var r = new RectangleF(x, y, l, Dessin.Echelle(this, 20));
            using (var chemin = Dessin.Arrondi(r, r.Height / 2))
            using (var pinceau = new SolidBrush(Theme.Teinte(couleur, 0.14f)))
            {
                g.FillPath(pinceau, chemin);
            }

            Dessin.Texte(g, texte, police, Rectangle.Round(r), couleur, centre: true);
            return r.Right;
        }

        private void Encadre(Graphics g, RectangleF r, string nombre, string legende, Color fond, Color couleurNombre, Color couleurLegende)
        {
            using (var chemin = Dessin.Arrondi(r, Dessin.Echelle(this, 12)))
            using (var pinceau = new SolidBrush(fond))
            {
                g.FillPath(pinceau, chemin);
            }

            var x = (int)(r.X + Dessin.Echelle(this, 12));
            Dessin.Texte(g, nombre, Theme.PoliceGrasse(13.5f), new Rectangle(x, (int)(r.Y + Dessin.Echelle(this, 6)), (int)r.Width - 16, (int)Dessin.Echelle(this, 26)), couleurNombre);
            Dessin.Texte(g, legende, Theme.Police(8.25f), new Rectangle(x, (int)(r.Y + Dessin.Echelle(this, 32)), (int)r.Width - 16, (int)Dessin.Echelle(this, 18)), couleurLegende);
        }
    }

    /// <summary>Pastille ronde d'un adhérent (initiales en dégradé, grisée s'il est inactif).</summary>
    private sealed class AvatarAdherent : ControleDessine
    {
        private readonly Adherent _a;
        private readonly int _index;

        public AvatarAdherent(Adherent a, int index, int taille)
        {
            _a = a;
            _index = index;
            Size = new Size(taille, taille);
            AccessibleRole = AccessibleRole.Graphic;
            AccessibleName = a.NomComplet;
        }

        public static (Color Clair, Color Fonce) Degrade(int index) => Theme.DegradesAvatar[(index + 1) % Theme.DegradesAvatar.Length];

        protected override void Peindre(Graphics g) =>
            Avatar.Dessiner(g, new RectangleF(0, 0, Width - 1, Height - 1), _a.Initiales, Degrade(_index), _a.Actif, false, this);
    }

    /// <summary>Carte d'un adhérent : pastille, nom, niveau ; matériel dehors et non rendu (bouton accessible au clavier).</summary>
    private sealed class CarteAdherent : Button
    {
        private readonly FicheAdherent _fiche;
        private readonly int _index;
        private bool _selectionne;
        private bool _survol;

        public CarteAdherent(FicheAdherent fiche, int index)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseMnemonic = false;
            Cursor = Cursors.Hand;
            Margin = Padding.Empty;
            _fiche = fiche;
            _index = index;
            var a = fiche.Adherent;
            AccessibleName = $"{a.NomComplet}{(a.Niveau is null ? "" : $", {a.Niveau}")}, {Libelles.Pluriel(fiche.NbDehors, "équipement")} dehors, {fiche.NbNonRendus} non rendu{(fiche.NbNonRendus > 1 ? "s" : "")}{(a.Nitrox ? ", certifié nitrox" : "")}{(fiche.CaciExpire ? ", CACI expiré" : fiche.CaciBientot ? ", CACI à renouveler bientôt" : "")}{(a.Actif ? "" : ", désactivé")}";
        }

        public long AdherentId => _fiche.Adherent.Id;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selectionne
        {
            get => _selectionne;
            set
            {
                _selectionne = value;
                AccessibleDescription = value ? "Fiche affichée" : null;
                Invalidate();
            }
        }

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

        private float E(float v) => Dessin.Echelle(this, v);

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            Dessin.EffacerAvecParent(this, g);
            Dessin.Qualite(g);
            var a = _fiche.Adherent;

            var r = new RectangleF(0.75f, 0.75f, Width - 2.5f, Height - 2.5f);
            var fond = _selectionne ? Theme.Teinte(Theme.Accent, 0.10f) : _survol ? Theme.Sur(Theme.Surface, Theme.AccentClair, 0.05f) : Theme.Surface;
            using (var chemin = Dessin.Arrondi(r, E(22)))
            {
                using var pinceau = new SolidBrush(fond);
                g.FillPath(pinceau, chemin);
                using var bord = new Pen(_selectionne ? Theme.Sur(Theme.Surface, Theme.Accent, 0.55f) : Theme.Bordure, E(_selectionne ? 1.5f : 1));
                g.DrawPath(bord, chemin);
                if (Focused && ShowFocusCues)
                {
                    using var focus = new Pen(Theme.Accent, E(1.5f));
                    using var interieur = Dessin.Arrondi(RectangleF.Inflate(r, -3, -3), E(19));
                    g.DrawPath(focus, interieur);
                }
            }

            var x = E(18);
            var y = E(18);
            var d = E(52);
            Avatar.Dessiner(g, new RectangleF(x, y, d, d), a.Initiales, AvatarAdherent.Degrade(_index), a.Actif, false, this);
            var gauche = (int)(x + d + E(14));
            Dessin.Texte(g, a.NomComplet, Theme.PoliceTitre(12.75f), new Rectangle(gauche, (int)(y + E(2)), Width - gauche - (int)E(12), (int)E(26)), a.Actif ? Theme.Texte : Theme.TexteSecondaire);
            var px = (float)gauche;
            var py = y + E(32);
            if (!string.IsNullOrWhiteSpace(a.Niveau))
            {
                px = Pastille(g, a.Niveau.ToUpper(Libelles.Culture), px, py, Theme.AccentClair) + E(6);
            }

            if (a.Nitrox)
            {
                px = Pastille(g, "NITROX", px, py, Theme.Retour) + E(6);
            }

            if (_fiche.CaciExpire)
            {
                px = Pastille(g, "CACI EXPIRÉ", px, py, Theme.Erreur) + E(6);
            }
            else if (_fiche.CaciBientot)
            {
                px = Pastille(g, "CACI BIENTÔT", px, py, Theme.Avertissement) + E(6);
            }

            if (!a.Actif)
            {
                Pastille(g, "DÉSACTIVÉ", px, py, Theme.Erreur);
            }

            var hb = E(58);
            var yb = Height - E(18) - hb;
            var lb = (Width - (2 * x) - E(8)) / 2;
            var dehors = _fiche.NbDehors > 0;
            Encadre(g, new RectangleF(x, yb, lb, hb), _fiche.NbDehors.ToString(Libelles.Culture), "équip. dehors",
                Theme.Sur(fond, dehors ? Theme.Depart : Theme.Fond, dehors ? 0.12f : 0.4f), dehors ? Theme.DepartClair : Theme.Texte, dehors ? Theme.DepartClair : Theme.TexteSecondaire);
            var perdus = _fiche.NbNonRendus > 0;
            Encadre(g, new RectangleF(x + lb + E(8), yb, lb, hb), _fiche.NbNonRendus.ToString(Libelles.Culture), "non rendu" + (_fiche.NbNonRendus > 1 ? "s" : ""),
                Theme.Sur(fond, perdus ? Theme.Erreur : Theme.Retour, perdus ? 0.12f : 0.10f), perdus ? Theme.Erreur : Theme.Retour, perdus ? Theme.Erreur : Theme.Retour);
        }

        private float Pastille(Graphics g, string texte, float x, float y, Color couleur)
        {
            var police = Theme.PoliceTitre(7.5f);
            var l = TextRenderer.MeasureText(g, texte, police, Size.Empty, TextFormatFlags.NoPadding).Width + E(16);
            var r = new RectangleF(x, y, l, E(20));
            using (var chemin = Dessin.Arrondi(r, r.Height / 2))
            using (var pinceau = new SolidBrush(Theme.Teinte(couleur, 0.14f)))
            {
                g.FillPath(pinceau, chemin);
            }

            Dessin.Texte(g, texte, police, Rectangle.Round(r), couleur, centre: true);
            return r.Right;
        }

        private void Encadre(Graphics g, RectangleF r, string nombre, string legende, Color fond, Color couleurNombre, Color couleurLegende)
        {
            using (var chemin = Dessin.Arrondi(r, E(12)))
            using (var pinceau = new SolidBrush(fond))
            {
                g.FillPath(pinceau, chemin);
            }

            var x = (int)(r.X + E(12));
            Dessin.Texte(g, nombre, Theme.PoliceGrasse(13.5f), new Rectangle(x, (int)(r.Y + E(6)), (int)r.Width - 16, (int)E(26)), couleurNombre);
            Dessin.Texte(g, legende, Theme.Police(8.25f), new Rectangle(x, (int)(r.Y + E(32)), (int)r.Width - 16, (int)E(18)), couleurLegende);
        }
    }

    /// <summary>Ligne « 25/10 · Lac des Settons · 3 équip. · Tout rendu » d'une sortie nominative de l'adhérent.</summary>
    private sealed class LigneSortieAdherent : ControleDessine
    {
        private readonly SortieAdherent _s;

        public LigneSortieAdherent(SortieAdherent s)
        {
            _s = s;
            Height = 40;
            AccessibleName = $"{Libelles.Date(s.Date)}, {s.Intitule}, {Libelles.Pluriel(s.NbEquipements, "équipement")}, {s.Etat}";
        }

        protected override void Peindre(Graphics g)
        {
            using (var filet = new Pen(Theme.Sur(Theme.Surface, Theme.AccentClair, 0.08f)))
            {
                g.DrawLine(filet, 0, 0, Width, 0);
            }

            var police = Theme.Police(9.75f);
            var couleur = _s.EnCours ? Theme.DepartClair : _s.NbNonRendus == 0 ? Theme.Retour : Theme.Erreur;
            var pp = Theme.PoliceTitre(8.25f);
            var lp = TextRenderer.MeasureText(g, _s.Etat, pp, Size.Empty, TextFormatFlags.NoPadding).Width + E(16);
            var rp = new RectangleF(Width - lp, (Height - E(22)) / 2, lp, E(22));
            using (var chemin = Dessin.Arrondi(rp, rp.Height / 2))
            using (var pinceau = new SolidBrush(Theme.Teinte(couleur, 0.14f)))
            {
                g.FillPath(pinceau, chemin);
            }

            Dessin.Texte(g, _s.Etat, pp, Rectangle.Round(rp), couleur, centre: true);
            var nb = $"{_s.NbEquipements} équip.";
            var lnb = TextRenderer.MeasureText(g, nb, police).Width;
            var xnb = (int)(rp.Left - E(10) - lnb);
            Dessin.Texte(g, nb, police, new Rectangle(xnb, 0, lnb, Height), Theme.TexteSecondaire);
            Dessin.Texte(g, _s.Date.ToString("dd/MM", Libelles.Culture), police, new Rectangle(0, 0, (int)E(48), Height), Theme.TexteSecondaire);
            Dessin.Texte(g, $"{_s.Intitule} · {_s.Type}", police, Rectangle.FromLTRB((int)E(52), 0, xnb - (int)E(8), Height), Theme.TexteDoux);
        }
    }

    /// <summary>Ligne « 30/10 · Cours en piscine · 10 équip. · Tout rendu ».</summary>
    private sealed class LigneHistorique : ControleDessine
    {
        private readonly SortieResume _s;

        public LigneHistorique(SortieResume s)
        {
            _s = s;
            Height = 40;
            AccessibleName = $"{Libelles.Date(s.DateSortie)}, {s.Intitule}, {Libelles.Pluriel(s.NbTotal, "équipement")}, " +
                (s.NbManquants == 0 ? "tout rendu" : Libelles.Pluriel(s.NbManquants, "manquant"));
        }

        protected override void Peindre(Graphics g)
        {
            using (var filet = new Pen(Theme.Sur(Theme.Surface, Theme.AccentClair, 0.08f)))
            {
                g.DrawLine(filet, 0, 0, Width, 0);
            }

            var police = Theme.Police(9.75f);
            var ok = _s.NbManquants == 0;
            var bilan = ok ? "Tout rendu" : Libelles.Pluriel(_s.NbManquants, "manquant");
            var couleur = ok ? Theme.Retour : Theme.DepartClair;
            var pp = Theme.PoliceTitre(8.25f);
            var lp = TextRenderer.MeasureText(g, bilan, pp, Size.Empty, TextFormatFlags.NoPadding).Width + E(16);
            var rp = new RectangleF(Width - lp, (Height - E(22)) / 2, lp, E(22));
            using (var chemin = Dessin.Arrondi(rp, rp.Height / 2))
            using (var pinceau = new SolidBrush(Theme.Teinte(couleur, 0.14f)))
            {
                g.FillPath(pinceau, chemin);
            }

            Dessin.Texte(g, bilan, pp, Rectangle.Round(rp), couleur, centre: true);
            var nb = $"{_s.NbTotal} équip.";
            var lnb = TextRenderer.MeasureText(g, nb, police).Width;
            var xnb = (int)(rp.Left - E(10) - lnb);
            Dessin.Texte(g, nb, police, new Rectangle(xnb, 0, lnb, Height), Theme.TexteSecondaire);
            Dessin.Texte(g, _s.DateSortie.ToString("dd/MM", Libelles.Culture), police, new Rectangle(0, 0, (int)E(48), Height), Theme.TexteSecondaire);
            Dessin.Texte(g, _s.Intitule, police, Rectangle.FromLTRB((int)E(52), 0, xnb - (int)E(8), Height), Theme.TexteDoux);
        }
    }
}
