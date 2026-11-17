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
/// Membres du club : onglets « Adhérents | Classes » (Adhérents ouvert à l'arrivée). Toute personne du club est un adhérent
/// (recherche, filtres, import de la liste Excel ; sa fiche montre le matériel qu'il a et celui jamais rendu) ; un adhérent peut
/// aussi être enseignant (section « Enseignant » de sa fiche : responsable des sorties, niveau d'encadrement, inspecteur TIV ;
/// sa fiche montre alors le matériel dehors sous sa responsabilité et ses dernières sorties). Classes : une carte par classe
/// (Niveau 1, Niveau 2…), responsable d'une sortie comme un enseignant, pastille carrée (N2).
/// </summary>
public sealed class EcranMoniteurs : EcranBase
{
    // Onglets, dans l'ordre affiché : Adhérents (ouvert à l'arrivée), Classes.
    private const int OngletAdherents = 0, OngletClasses = 1;

    private readonly ServiceMoniteurs _moniteurs;
    private readonly ServiceAdherents _adherents;
    private readonly INavigation _navigation;
    private readonly GrilleAdaptative _cartes = new() { LargeurMin = 250, HauteurElement = 176, Ecart = 14, Margin = new Padding(0, 0, 22, 0) };
    private readonly Panel _fiche = new() { AutoScroll = true, BackColor = Theme.Surface, Margin = Padding.Empty };
    private readonly Carte _carteFiche = new() { Padding = new Padding(24), CouleurBordure = Theme.BordureForte };
    private readonly Bascule _onglets = new("Adhérents", "Classes");
    private readonly BoutonArrondi _ajouter = new("Ajouter un adhérent", StyleBouton.Principal, Icones.Plus);

    // Enseignants que le rapprochement du démarrage n'a pas pu relier seul (homonymes, noms proches) : à trancher à la main.
    private readonly BoutonArrondi _relier = new("Enseignants à relier", StyleBouton.Secondaire, Icones.Echange) { Visible = false };
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
        ("Enseignants", FiltreAdherents.Enseignants),
        ("Inspecteurs TIV", FiltreAdherents.InspecteursTiv),
        ("Matériel dehors", FiltreAdherents.MaterielDehors),
        ("Non rendu", FiltreAdherents.NonRendu),
        ("Nitrox", FiltreAdherents.Nitrox),
        ("CACI à renouveler", FiltreAdherents.CaciARenouveler),
        ("Désactivés", FiltreAdherents.Desactives),
    ];

    // Puces « Tous 12 / Enseignants 4… » (même dessin que les filtres de catégories) ; pas de filtre pour les classes.
    private readonly FiltreCategories _filtre = new(Theme.AccentClair) { LibelleTout = "Tous", Margin = new Padding(0, 10, 0, 8) };
    private FiltreAdherents _choixAdherents = FiltreAdherents.Tous;
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
        _garde.Lier(_relier, ActionProtegee.Moniteurs);
        _ajouter.Click += (_, _) => Executer(() => Adherents ? ModifierAdherentAsync(null) : ModifierClasseAsync(null));
        _relier.Click += (_, _) => Executer(RelierEnseignantsAsync);
        _relier.AccessibleDescription = "Enseignants dont la fiche d'adhérent n'a pas pu être retrouvée sans hésitation : choisissez la bonne";
        _importer.Click += (_, _) => Executer(ImporterAsync);
        _modele.Click += (_, _) => Executer(EnregistrerModeleAsync); // Libre : ne change aucune donnée.
        _modele.AccessibleDescription = "Fichier Excel vide, ou avec la liste actuelle des adhérents, à remplir puis à importer";

        _onglets.AccessibleName = "Membres";
        _onglets.Margin = new Padding(0, 0, 0, 22);
        _onglets.Changement += ChangementOnglet;

        _filtre.Changement += (_, _) =>
        {
            _choixAdherents = PucesAdherents.Where(p => p.Libelle == _filtre.Selection).Select(p => p.Filtre).FirstOrDefault(); // Tous si aucune.
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
            Executer(AfficherGenreAsync);
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
            (Ui.Entete(Ui.Pile(Ui.Titre(Titre), Ui.SousTitre("Les adhérents du club, dont les enseignants, les classes et le matériel encore dehors.")), _ajouter, _importer, _modele, _relier), SizeType.AutoSize, 0),
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

    private bool Classes => _onglets.SelectedIndex == OngletClasses;

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
        var aRelier = await _adherents.ListerEnseignantsARelierAsync();
        if (IsDisposed)
        {
            return;
        }

        _onglets.DefinirNombre(OngletClasses, _tous.Count(f => f.Moniteur.EstClasse));
        _onglets.DefinirNombre(OngletAdherents, adherents.Tous);
        _relier.Text = _relier.AccessibleName = aRelier.Count == 1 ? "1 enseignant à relier" : $"{aRelier.Count} enseignants à relier";
        _relier.Visible = aRelier.Count > 0;
        await AfficherGenreAsync();
    }

    /// <summary>Cartes de l'onglet choisi (adhérents ou classes) et fiche de celui qui est choisi.</summary>
    private async Task AfficherGenreAsync()
    {
        // Recherche et filtres : adhérents seulement (les classes sont peu nombreuses).
        var avecFiltres = Adherents;
        var filtres = _page.GetControlFromPosition(0, 2)!;
        if (filtres.Visible != avecFiltres)
        {
            filtres.Visible = avecFiltres;
            _page.RowStyles[2].Height = avecFiltres ? Dessin.Echelle(this, HauteurFiltres) : 0;
        }

        _filtre.Unite = "adhérent";
        _importer.Visible = Adherents;
        _modele.Visible = Adherents;
        if (Adherents)
        {
            _ajouter.Text = _ajouter.AccessibleName = "Ajouter un adhérent";
            await AfficherAdherentsAsync();
            return;
        }

        const GenreResponsable genre = GenreResponsable.Classe;
        _ajouter.Text = _ajouter.AccessibleName = "Ajouter une classe";
        _liste = _tous.Where(f => f.Moniteur.Genre == genre).ToList();
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
        _selection[GenreResponsable.Classe] = id;
        foreach (var carte in _cartes.Controls.OfType<CarteMoniteur>())
        {
            carte.Selectionne = carte.MoniteurId == id;
        }

        await AfficherFicheAsync();
    }

    private async Task AfficherFicheAsync()
    {
        var adherents = Adherents;
        var detail = !adherents && _selection.GetValueOrDefault(GenreResponsable.Classe) is { } id ? await _moniteurs.ObtenirDetailAsync(id) : null;
        var ficheAdherent = adherents && _adherentChoisi is { } a ? await _adherents.ObtenirFicheAsync(a) : null;

        // Adhérent enseignant (ou qui l'a été) : matériel sous sa responsabilité et ses dernières sorties.
        var encadrement = ficheAdherent?.Adherent.MoniteurId is { } moniteurId ? await _moniteurs.ObtenirDetailAsync(moniteurId) : null;
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
                ConstruireFicheAdherent(ficheAdherent, encadrement, Ligne);
            }
        }
        else if (detail is null)
        {
            Ligne(Ui.Libelle("Aucune classe. Cliquez sur « Ajouter une classe ».", Theme.Police(11f), Theme.TexteSecondaireClair));
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
        modifier.Click += (_, _) => Executer(() => ModifierClasseAsync(m));
        var creer = new BoutonArrondi("Créer une sortie", StyleBouton.Principal) { AutoSize = false, Margin = Padding.Empty, Enabled = m.Actif };
        creer.Click += (_, _) => _navigation.AllerA<EcranNouvelleSortie>(m.Id);
        var actions = Ui.Colonnes((modifier, SizeType.Percent, 42), (creer, SizeType.Percent, 58));
        actions.Height = Theme.HauteurBouton;
        actions.Margin = new Padding(0, 18, 0, 0);
        ligne(actions);
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
            FiltreAdherents.Enseignants => liste.Enseignants,
            FiltreAdherents.InspecteursTiv => liste.InspecteursTiv,
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
    /// Fiche d'un adhérent : formation, licence, téléphone, CACI ; matériel qu'il a en ce moment ; matériel non rendu (code,
    /// sortie, date) ; dernières sorties nominatives. Enseignant (<paramref name="encadrement"/>) : matériel dehors sous sa
    /// responsabilité et dernières sorties encadrées, « Créer une sortie ». Actions : Modifier, Désactiver / Réactiver.
    /// </summary>
    private void ConstruireFicheAdherent(DetailAdherent detail, DetailMoniteur? encadrement, Action<Control> ligne)
    {
        var a = detail.Adherent;
        var index = Math.Max(0, _listeAdherents.ToList().FindIndex(f => f.Adherent.Id == a.Id));

        var nom = Ui.Libelle(a.NomComplet, Theme.PoliceGrasse(16f));
        nom.Margin = new Padding(0, 0, 0, 4);
        var role = a.Enseignant ? $"Enseignant{(a.NiveauEncadrement is { Length: > 0 } niveauEnc ? $" {niveauEnc}" : "")}" : "Adhérent";
        var qualite = string.Join(" · ", new[]
        {
            role, a.InspecteurTiv ? "inspecteur TIV" : null, a.NiveauObtenu, a.Niveau is { } formation ? $"prépare {formation}" : null,
            ServiceAdherents.ListeBrevets(a.AutresBrevets) is { Count: > 0 } autres ? string.Join(", ", autres) : null,
            a.Nitrox ? "certifié nitrox" : null, a.Actif ? null : "désactivé",
        }.Where(p => !string.IsNullOrWhiteSpace(p)));
        var textes = Ui.Pile(nom, Ui.Libelle(qualite, Theme.Police(9.75f), a.Enseignant ? Theme.Encadrement : Theme.TexteSecondaireClair));
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

        // Enseignant (ou ancien enseignant qui a encadré) : ce qu'il a sous sa responsabilité, ses dernières sorties encadrées.
        if (encadrement is not null && (a.Enseignant || encadrement.EnCours.Count > 0 || encadrement.Dernieres.Count > 0))
        {
            ligne(Section("Matériel dehors sous sa responsabilité"));
            if (encadrement.EnCours.Count == 0)
            {
                var rien = new BandeauMessage { Margin = new Padding(0, 0, 0, 18), Height = 60 };
                rien.Afficher(NiveauResultat.Succes, "Rien dehors. Tout le matériel est rentré.");
                ligne(rien);
            }

            foreach (var sd in encadrement.EnCours)
            {
                ligne(CarteDehors(sd));
            }

            ligne(Section("Dernières sorties encadrées"));
            if (encadrement.Dernieres.Count == 0)
            {
                var aucune = Ui.Libelle("Aucune sortie clôturée.", Theme.Police(9.75f), Theme.TexteSecondaire);
                aucune.Margin = new Padding(0, 0, 0, 18);
                ligne(aucune);
            }

            foreach (var s in encadrement.Dernieres)
            {
                ligne(new LigneHistorique(s));
            }
        }

        // Actions : Modifier, et Désactiver (il ne peut plus sortir de matériel) ou Réactiver ; enseignant : Créer une sortie.
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
        if (a.Enseignant && a.MoniteurId is { } moniteurId)
        {
            var creer = new BoutonArrondi("Créer une sortie", StyleBouton.Principal, Icones.Plus)
            {
                AutoSize = false,
                Height = Theme.HauteurBouton,
                Margin = new Padding(0, 10, 0, 0),
                AccessibleDescription = $"Nouvelle sortie avec {a.NomComplet} pour responsable",
            };
            creer.Click += (_, _) => _navigation.AllerA<EcranNouvelleSortie>(moniteurId);
            ligne(creer);
        }
    }

    /// <summary>Désactive (avec confirmation) ou réactive l'adhérent, puis relit la liste.</summary>
    private async Task ChangerActifAsync(Adherent a)
    {
        if (!_garde.Exiger(FindForm(), ActionProtegee.Moniteurs, a.Actif ? "Désactiver un adhérent" : "Réactiver un adhérent"))
        {
            return;
        }

        if (a.Actif && !Dialogues.Confirmer(FindForm(),
            $"Désactiver {a.NomComplet} ?\n\nIl ne pourra plus sortir de matériel{(a.Enseignant ? " ni être responsable d'une sortie" : "")}. Son historique est gardé et vous pourrez le réactiver."))
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

    /// <summary>
    /// « N enseignants à relier » : pour chaque enseignant que le démarrage n'a pas relié seul (homonymes, noms proches), une
    /// fenêtre propose les fiches d'adhérent possibles ou « Créer sa fiche d'adhérent ». « Annuler » arrête ; le reste attend.
    /// </summary>
    private async Task RelierEnseignantsAsync()
    {
        if (!_garde.Exiger(FindForm(), ActionProtegee.Moniteurs, "Relier un enseignant à sa fiche d'adhérent"))
        {
            return;
        }

        const string Nouvelle = "nouvelle";
        long? dernier = null;
        foreach (var cas in await _adherents.ListerEnseignantsARelierAsync())
        {
            var m = cas.Enseignant;
            var liste = new ListeChoix { AccessibleName = $"Fiche d'adhérent de {m.NomComplet}", Margin = new Padding(0, 0, 0, 4) };
            liste.Afficher(cas.Candidats
                .Select(a => new ElementChoix(a.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), a.Initiales, true, a.NomComplet,
                    string.Join(" · ", new[] { a.Licence is { } l ? $"licence {l}" : null, a.Telephone, a.Niveau, a.Actif ? null : "désactivé" }
                        .Where(t => !string.IsNullOrWhiteSpace(t))) is { Length: > 0 } detail ? detail : "Ni licence ni téléphone"))
                .Append(new ElementChoix(Nouvelle, "+", true, "Créer sa fiche d'adhérent", "Aucune de ces fiches n'est la sienne"))
                .ToList(), "");

            ResultatOperation? relie = null;
            using var fenetre = new FenetreFormulaire($"Relier {m.NomComplet}", "Relier", async () =>
            {
                if (liste.Choix is not { } choix)
                {
                    return ResultatOperation.Echec("Choisissez sa fiche d'adhérent dans la liste.");
                }

                relie = await _adherents.RelierEnseignantAsync(m.Id, choix.Cle == Nouvelle ? null : long.Parse(choix.Cle, System.Globalization.CultureInfo.InvariantCulture));
                return relie;
            }, largeur: 560, marge: 24);
            fenetre.EnTete("Enseignant : quelle est sa fiche d'adhérent ?", icone: Icones.Echange);
            fenetre.Avatar.Colorer(Theme.Encadrement, Theme.TexteSurEncadrement);
            var explication = Ui.Libelle(cas.Candidats.Count == 0
                    ? $"Aucun adhérent ne s'appelle {m.NomComplet} : choisissez « Créer sa fiche d'adhérent »."
                    : cas.Candidats.Count > 1 && cas.Candidats.All(c => ServiceAdherents.Simplifier(c.NomComplet) == ServiceAdherents.Simplifier(m.NomComplet))
                    ? $"Plusieurs adhérents s'appellent {m.NomComplet}. Choisissez celui qui est l'enseignant : son matériel et son CACI seront ceux de cette fiche."
                    : $"Ces adhérents ont un nom proche de {m.NomComplet}{(m.Licence.Length > 0 ? $" (licence {m.Licence})" : "")}. Est-ce l'un d'eux ?",
                Theme.Police(9.75f), Theme.TexteSecondaireClair);
            explication.MaximumSize = new Size(500, 0);
            explication.Margin = new Padding(0, 0, 0, 14);
            fenetre.Ajouter(explication);
            fenetre.Ajouter(liste);
            fenetre.Shown += (_, _) => liste.Focus();
            if (fenetre.ShowDialog(FindForm()) != DialogResult.OK)
            {
                break;
            }

            dernier = relie?.Id ?? dernier;
        }

        _adherentChoisi = dernier ?? _adherentChoisi;
        _choixAdherents = FiltreAdherents.Tous;
        await OuvrirOngletAsync(OngletAdherents);
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
        // Maquette « Niveau et formation » : brevet obtenu sur l'échelle Aucun… N4 (déduit de la classe suivie s'il n'est pas
        // renseigné : « Niveau 2 » → N1), interrupteur « En formation » = la classe qui prépare le brevet suivant (une autre
        // classe déjà suivie, Autonome, est gardée tant qu'on ne coupe pas), autres brevets en puces (Nitrox = certifié nitrox).
        var formations = await _adherents.ListerFormationsAsync(a?.ClasseId);
        var classe = formations.FirstOrDefault(f => f.Id == a?.ClasseId);
        var echelle = new EchelleBrevets { Margin = new Padding(0, 4, 0, 18) };
        echelle.Valeur = ServiceAdherents.IndiceBrevet(a?.NiveauObtenu) ?? ServiceAdherents.BrevetDeduit(classe?.Nom);
        var formation = new Interrupteur("En formation") { Checked = classe is not null };
        var brevets = new ChoixBrevets(ServiceAdherents.AutresBrevetsProposes) { Margin = new Padding(0, 0, 0, 12) };
        brevets.Brevets = [.. (a?.Nitrox == true ? ["Nitrox"] : Array.Empty<string>()), .. ServiceAdherents.ListeBrevets(a?.AutresBrevets)];
        Moniteur? Suivante() => ServiceAdherents.ClasseSuivante(echelle.Valeur, formations);

        var licence = new ChampSaisie { Valeur = a?.Licence ?? "", TexteIndicatif = "A-12-345678", Icone = Icones.CarteLicence };
        var telephone = new ChampSaisie { Valeur = a?.Telephone ?? "", TexteIndicatif = "06 12 34 56 78", Icone = Icones.Telephone };
        var actif = new Interrupteur("Actif") { Checked = a?.Actif ?? true };
        var nitrox = new Interrupteur("Certifié nitrox") { Checked = a?.Nitrox ?? false };
        var caci = new Interrupteur("Contrôler le CACI") { Checked = a?.Caci ?? false };
        var caciExpiration = new ChampDate { Date = a?.CaciExpiration, AccessibleName = "CACI valable jusqu'au" };

        // Rôles : enseignant (responsable des sorties ; niveau d'encadrement en puce E1… MF2 ou tapé dans « Autre niveau ») et,
        // à part, inspecteur TIV (enseignant ou non).
        var enseignant = new Interrupteur("Enseignant") { Checked = a?.Enseignant ?? false };
        var propose = ServiceMoniteurs.NiveauPropose(a?.NiveauEncadrement);
        var niveauxEncadrement = new ChoixPuces { AccessibleName = "Niveau d'encadrement", Margin = new Padding(0, 0, 0, 12) };
        niveauxEncadrement.GroupeDetaille(ServiceMoniteurs.NiveauxEncadrement.Select(n => (n.Code, n.Qualite)), Theme.Encadrement, Theme.TexteSurEncadrement);
        niveauxEncadrement.Valeur = propose;
        var autreNiveau = new ChampSaisie { Valeur = propose is null ? a?.NiveauEncadrement ?? "" : "", TexteIndicatif = "Ex. BEES 1", Size = new Size(170, Theme.HauteurCliquable) };
        autreNiveau.Champ.AccessibleName = "Autre niveau d'encadrement";
        autreNiveau.Champ.MaxLength = Math.Max(ServiceMoniteurs.LongueurMaxAutreNiveau, autreNiveau.Valeur.Length);
        autreNiveau.Champ.CharacterCasing = CharacterCasing.Upper;
        autreNiveau.Margin = Padding.Empty;
        var tiv = new Interrupteur("Inspecteur TIV") { Checked = a?.InspecteurTiv ?? false };
        string? NiveauEncadrement() => niveauxEncadrement.Valeur ?? (autreNiveau.Valeur.Trim() is { Length: > 0 } n ? n : null);

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
                ClasseId = classe?.Id,
                Niveau = classe?.Nom,
                NiveauObtenu = echelle.Valeur is { } brevet ? ServiceAdherents.BrevetsObtenus[brevet] : null,
                AutresBrevets = ServiceAdherents.JoindreBrevets(brevets.Brevets),
                Licence = licence.Valeur,
                Telephone = telephone.Valeur,
                Actif = actif.Checked,
                Nitrox = nitrox.Checked,
                Caci = caci.Checked,
                CaciExpiration = caciExpiration.Date,
            }, new RolesAdherent(enseignant.Checked, NiveauEncadrement(), tiv.Checked));
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

        // Niveau et formation (maquette) : BREVET OBTENU (échelle, « Effacer »), EN FORMATION (carte dorée), AUTRES BREVETS.
        var sectionNiveau = new SectionFormulaire(2, "Niveau et formation");
        Label Intertitre(string texte)
        {
            var libelle = Ui.Libelle(texte.ToUpper(Libelles.Culture), Theme.PoliceGrasse(8.25f), Theme.TexteSecondaire);
            libelle.Anchor = AnchorStyles.Left;
            libelle.Margin = Padding.Empty;
            return libelle;
        }

        var effacer = new BoutonArrondi("Effacer", StyleBouton.Lien) { Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        effacer.Click += (_, _) => echelle.Valeur = null;
        var enteteBrevet = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Height = Theme.HauteurCliquable, Margin = new Padding(0, 0, 0, 6), BackColor = Color.Transparent };
        enteteBrevet.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        enteteBrevet.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        enteteBrevet.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        enteteBrevet.Controls.Add(Intertitre("Brevet obtenu"), 0, 0);
        enteteBrevet.Controls.Add(effacer, 1, 0);
        sectionNiveau.Ajouter(enteteBrevet);
        sectionNiveau.Ajouter(echelle);

        var titreFormation = sectionNiveau.Ajouter(Intertitre("En formation"));
        titreFormation.Margin = new Padding(0, 0, 0, 10);
        var carteFormation = sectionNiveau.Ajouter(new CarteAutorisation(new TuileIcone(Icones.Diplome, null, Theme.Avertissement), "", "", formation, Theme.Avertissement));
        carteFormation.Margin = new Padding(0, 0, 0, 18);

        var facultatif = Ui.Libelle("· facultatif", Theme.Police(8.25f), Theme.TexteSecondaire);
        facultatif.Margin = new Padding(6, 0, 0, 0);
        var titreBrevets = sectionNiveau.Ajouter(Ui.Rangee(true, Intertitre("Autres brevets"), facultatif));
        titreBrevets.Margin = new Padding(0, 0, 0, 10);
        sectionNiveau.Ajouter(brevets);
        fenetre.Ajouter(sectionNiveau);

        // Rôles au club : enseignant (niveau d'encadrement sous la carte) et, à part, inspecteur TIV.
        var sectionRoles = new SectionFormulaire(3, "Rôles au club");
        var carteEnseignant = sectionRoles.Ajouter(new CarteAutorisation(new TuileIcone(Icones.Personne, null, Theme.Encadrement), "Enseignant", "", enseignant, Theme.Encadrement));
        var libelleAutre = Ui.Libelle("Autre niveau :", Theme.Police(9f), Theme.TexteSecondaire);
        libelleAutre.Margin = new Padding(0, 13, 10, 0);
        var ligneAutre = Ui.Rangee(true, libelleAutre, autreNiveau);
        ligneAutre.Margin = Padding.Empty;
        var blocEnseignant = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, BackColor = Color.Transparent };
        blocEnseignant.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var c in new Control[] { FenetreFormulaire.LibelleChamp("Niveau d'encadrement"), niveauxEncadrement, ligneAutre })
        {
            blocEnseignant.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            c.Dock = c is Label ? DockStyle.None : DockStyle.Fill;
            blocEnseignant.Controls.Add(c, 0, blocEnseignant.RowStyles.Count - 1);
        }

        carteEnseignant.Dessous(blocEnseignant);
        sectionRoles.Ajouter(new CarteAutorisation(new TuileIcone(Icones.InspecteurTiv, null, Theme.Avertissement), "Inspecteur TIV",
            "Peut faire les TIV des blocs du club, qu'il soit enseignant ou non.", tiv, Theme.Avertissement));
        fenetre.Ajouter(sectionRoles);

        var autorisations = new SectionFormulaire(4, "Autorisations matériel");
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

            var brevet = echelle.Valeur;
            var niveauObtenu = brevet is { } b ? ServiceAdherents.BrevetsObtenus[b] : null;
            var suivante = Suivante();
            var prepare = classe is not null && classe.Id == suivante?.Id;
            sectionNiveau.TexteDroite = ServiceAdherents.ResumeNiveau(brevet, classe?.Nom);
            sectionNiveau.CouleurDroite = brevet is null && classe is null ? Theme.TexteSecondaire : Theme.AccentClair;
            effacer.Visible = brevet is not null;
            echelle.EnFormation = prepare;
            carteFormation.Disponible = classe is not null || suivante is not null;
            var prochain = brevet is >= 0 and < 4 ? ServiceAdherents.NiveauxPlongee[brevet.Value] : null;
            (carteFormation.Titre, carteFormation.Description) = (classe, suivante) switch
            {
                ({ } c, _) when prepare => ($"Prépare le {c.Nom}", $"Suit la classe {c.Nom} cette saison."),
                ({ } c, _) => ($"Suit la classe {c.Nom}", "Coupez pour retirer cette classe."),
                (null, { } s) => ($"Prépare le {s.Nom}", "Ne suit pas de formation cette saison."),
                _ when brevet is null => ("Choisissez d'abord le brevet obtenu", "Pas de classe à suivre."),
                _ when prochain is null => ("Niveau maximum atteint", "Pas de classe à suivre."),
                _ => ($"Pas de classe « {prochain} »", "Créez-la dans Membres › Classes pour la proposer ici."),
            };
            var niveauEnc = NiveauEncadrement();
            carteEnseignant.DessousVisible = enseignant.Checked;
            carteEnseignant.Description = enseignant.Checked
                ? "Proposé comme responsable des sorties."
                : a?.Enseignant == false && a.NiveauEncadrement is not null ? "N'est plus enseignant : ses sorties restent à son nom." : "Peut être responsable des sorties.";
            sectionRoles.TexteDroite = string.Join(" · ", new[] { enseignant.Checked ? $"enseignant{(niveauEnc is null ? "" : $" {niveauEnc}")}" : null, tiv.Checked ? "inspecteur TIV" : null }
                .Where(t => t is not null)) is { Length: > 0 } texteRoles ? texteRoles : "aucun";
            sectionRoles.CouleurDroite = enseignant.Checked ? Theme.Encadrement : tiv.Checked ? Theme.Avertissement : Theme.TexteSecondaire;
            autreNiveau.CouleurBordure = niveauxEncadrement.Valeur is null && niveauEnc is not null ? Theme.Encadrement : null;
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
            if (enseignant.Checked)
            {
                etiquettes.Add((niveauEnc is null ? "Enseignant" : $"Enseignant {niveauEnc}", Theme.Encadrement));
            }

            if (tiv.Checked)
            {
                etiquettes.Add(("Inspecteur TIV", Theme.Avertissement));
            }

            if (niveauObtenu is not null)
            {
                etiquettes.Add((niveauObtenu, Theme.TexteSecondaireClair));
            }

            if (classe is { } nv)
            {
                etiquettes.Add(($"Prépare {nv.Nom}", Theme.AccentClair));
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
        // Brevet changé : la formation suit (classe du brevet suivant) ; une autre classe (Autonome) est gardée.
        var majFormation = false;
        echelle.ValeurChangee += (_, _) =>
        {
            if (formation.Checked && (classe is null || ServiceAdherents.BrevetDeduit(classe.Nom) is not null))
            {
                classe = Suivante();
                majFormation = true;
                formation.Checked = classe is not null;
                majFormation = false;
            }

            Afficher();
        };
        formation.CheckedChanged += (_, _) =>
        {
            if (!majFormation)
            {
                classe = formation.Checked ? Suivante() : null;
                if (formation.Checked && classe is null)
                {
                    formation.Checked = false;
                }
            }

            Afficher();
        };

        // « Nitrox » des autres brevets = l'interrupteur « Certifié nitrox » des autorisations.
        brevets.BrevetsChanges += (_, _) =>
        {
            nitrox.Checked = brevets.Contient("Nitrox");
            Afficher();
        };
        nitrox.CheckedChanged += (_, _) => brevets.Definir("Nitrox", nitrox.Checked);
        enseignant.CheckedChanged += (_, _) => Afficher();
        tiv.CheckedChanged += (_, _) => Afficher();
        niveauxEncadrement.ValeurChangee += (_, _) =>
        {
            if (niveauxEncadrement.Valeur is not null)
            {
                autreNiveau.Valeur = "";
            }

            Afficher();
        };
        autreNiveau.Champ.TextChanged += (_, _) =>
        {
            if (autreNiveau.Valeur.Trim().Length > 0)
            {
                niveauxEncadrement.Valeur = null;
            }

            Afficher();
        };
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

    /// <summary>
    /// « Modèle Excel à remplir » : choix entre la liste actuelle des adhérents (à corriger puis réimporter : chaque ligne
    /// retrouve sa fiche par son « N° fiche ») et un classeur vide, aux colonnes de l'import.
    /// </summary>
    private async Task EnregistrerModeleAsync()
    {
        const string AvecListe = "liste", Vide = "vide";
        var lignes = await _adherents.LignesModeleAsync();
        var choix = new ListeChoix { AccessibleName = "Contenu du fichier Excel", Margin = new Padding(0, 0, 0, 4) };
        choix.Afficher(
        [
            new ElementChoix(AvecListe, "≡", false, "Avec la liste actuelle des adhérents",
                $"{Libelles.Pluriel(lignes.Count, "adhérent actif", "adhérents actifs")} à corriger ou compléter, puis à réimporter"),
            new ElementChoix(Vide, "+", false, "Modèle vide", "Seulement les titres des colonnes, pour noter de nouveaux adhérents"),
        ], "");
        choix.Choisir(lignes.Count > 0 ? AvecListe : Vide);
        choix.Height = (2 * 58) + 8 + 4;

        string? retenu = null;
        using (var fenetre = new FenetreFormulaire("Fichier Excel des adhérents", "Enregistrer", () =>
        {
            retenu = choix.Choix?.Cle;
            return Task.FromResult(retenu is null ? ResultatOperation.Echec("Choisissez le contenu du fichier.") : ResultatOperation.Ok(""));
        }, largeur: 560, marge: 24))
        {
            fenetre.EnTete("Que doit contenir le fichier ?", icone: Icones.Telecharger);
            var explication = Ui.Libelle("À l'import, chaque personne est reconnue (N° fiche, licence, puis nom et prénom) : sa fiche est mise à jour, jamais créée en double.",
                Theme.Police(9.75f), Theme.TexteSecondaireClair);
            explication.MaximumSize = new Size(500, 0);
            explication.Margin = new Padding(0, 0, 0, 14);
            fenetre.Ajouter(explication);
            fenetre.Ajouter(choix);
            fenetre.Shown += (_, _) => choix.Focus();
            if (fenetre.ShowDialog(FindForm()) != DialogResult.OK || retenu is null)
            {
                return;
            }
        }

        var avecListe = retenu == AvecListe;
        // Couleur de l'en-tête : celle du logo du club (fixe dans les deux thèmes).
        var c = Theme.LogoFonce;
        var formations = (await _adherents.ListerFormationsAsync()).Select(f => f.Nom).ToList();
        var colonnes = ServiceAdherents.ColonnesModele(formations, avecListe).Select(m => new Infrastructure.ColonneClasseur(
            m.EnTete, m.Obligatoire ? $"Obligatoire. {m.Aide}" : m.Aide, m.Largeur, m.Choix)).ToList();
        await Infrastructure.Fichiers.EnregistrerXlsxAsync(FindForm(),
            avecListe ? "Enregistrer la liste des adhérents" : "Enregistrer le modèle des adhérents",
            ServiceAdherents.NomFichierModele(_reglages.NomClub, avecListe ? _adherents.AujourdHui : null),
            chemin => Infrastructure.EcrivainTableur.EcrireModele(chemin, "Adhérents", colonnes, ServiceAdherents.ModeEmploiModele(avecListe), $"{c.R:X2}{c.G:X2}{c.B:X2}",
                avecListe ? lignes : null));
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
            AccessibleName = $"{a.NomComplet}{(a.Enseignant ? $", enseignant{(a.NiveauEncadrement is { } enc ? $" {enc}" : "")}" : "")}{(a.InspecteurTiv ? ", inspecteur TIV" : "")}{(a.NiveauObtenu is null ? "" : $", {a.NiveauObtenu}")}{(a.Niveau is null ? "" : $", prépare {a.Niveau}")},{Libelles.Pluriel(fiche.NbDehors, "équipement")} dehors, {fiche.NbNonRendus} non rendu{(fiche.NbNonRendus > 1 ? "s" : "")}{(a.Nitrox ? ", certifié nitrox" : "")}{(fiche.CaciExpire ? ", CACI expiré" : fiche.CaciBientot ? ", CACI à renouveler bientôt" : "")}{(a.Actif ? "" : ", désactivé")}";
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

            // Enseignant : toute la carte est teintée de violet (bordure comprise), pour le repérer d'un coup d'œil.
            var enseignant = a.Enseignant;
            var teinte = enseignant ? Theme.Sur(Theme.Surface, Theme.Encadrement, 0.13f) : Theme.Surface;
            var fond = _selectionne ? Theme.Sur(teinte, Theme.Accent, 0.10f) : _survol ? Theme.Sur(teinte, Theme.AccentClair, 0.05f) : teinte;
            using (var chemin = Dessin.Arrondi(r, E(22)))
            {
                using var pinceau = new SolidBrush(fond);
                g.FillPath(pinceau, chemin);
                var couleurBord = _selectionne ? Theme.Sur(Theme.Surface, Theme.Accent, 0.55f) : enseignant ? Theme.Sur(Theme.Surface, Theme.Encadrement, 0.5f) : Theme.Bordure;
                using var bord = new Pen(couleurBord, E(_selectionne || enseignant ? 1.5f : 1));
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

            // Pastilles, de la plus importante à la moins importante ; celles qui ne tiennent plus sur la ligne sont omises
            // (la fiche donne tout). Désactivé en premier : il ne sort plus rien.
            if (!a.Actif)
            {
                px = Pastille(g, "DÉSACTIVÉ", px, py, Theme.Erreur);
            }

            if (_fiche.CaciExpire)
            {
                px = Pastille(g, "CACI EXPIRÉ", px, py, Theme.Erreur);
            }

            if (enseignant)
            {
                px = Pastille(g, a.NiveauEncadrement is { Length: > 0 } enc ? $"ENSEIGNANT {enc.ToUpper(Libelles.Culture)}" : "ENSEIGNANT", px, py, Theme.Encadrement);
            }

            if (a.InspecteurTiv)
            {
                px = Pastille(g, "TIV", px, py, Theme.Avertissement);
            }

            // Niveau obtenu (N1), puis la classe suivie (« PRÉPARE N2 »).
            if (!string.IsNullOrWhiteSpace(a.NiveauObtenu))
            {
                px = Pastille(g, a.NiveauObtenu == ServiceAdherents.Debutant ? "DÉBUTANT" : ServiceMoniteurs.AbregeDe(a.NiveauObtenu), px, py, Theme.TexteSecondaireClair);
            }

            if (!string.IsNullOrWhiteSpace(a.Niveau))
            {
                px = Pastille(g, $"PRÉPARE {ServiceMoniteurs.AbregeDe(a.Niveau)}", px, py, Theme.AccentClair);
            }

            if (a.Nitrox)
            {
                px = Pastille(g, "NITROX", px, py, Theme.Retour);
            }

            if (_fiche.CaciBientot)
            {
                Pastille(g, "CACI BIENTÔT", px, py, Theme.Avertissement);
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
            if (x + l > Width - E(14))
            {
                return x; // Ne tient plus sur la ligne : omise.
            }

            var r = new RectangleF(x, y, l, E(20));
            using (var chemin = Dessin.Arrondi(r, r.Height / 2))
            using (var pinceau = new SolidBrush(Theme.Teinte(couleur, 0.14f)))
            {
                g.FillPath(pinceau, chemin);
            }

            Dessin.Texte(g, texte, police, Rectangle.Round(r), couleur, centre: true);
            return r.Right + E(6);
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
