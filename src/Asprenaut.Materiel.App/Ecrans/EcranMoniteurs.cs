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
/// Membres du club : onglets « Moniteurs | Classes | Adhérents ». Moniteurs et classes : une carte par responsable de
/// sortie (sorties de la saison, matériel dehors) et sa fiche (matériel dehors sous sa responsabilité, dernières sorties,
/// modifier, créer une sortie). Une classe (Niveau 1, Niveau 2…) est responsable d'une sortie comme un moniteur ; elle a une
/// pastille carrée (N2). Adhérents : ceux qui prennent du matériel lors d'une sortie nominative (recherche, filtres, import
/// de la liste Excel) ; leur fiche montre le matériel qu'ils ont et celui jamais rendu. Un adhérent n'est jamais responsable.
/// </summary>
public sealed class EcranMoniteurs : EcranBase
{
    private const int OngletAdherents = 2;

    private readonly ServiceMoniteurs _moniteurs;
    private readonly ServiceAdherents _adherents;
    private readonly INavigation _navigation;
    private readonly GrilleAdaptative _cartes = new() { LargeurMin = 250, HauteurElement = 176, Ecart = 14, Margin = new Padding(0, 0, 22, 0) };
    private readonly Panel _fiche = new() { AutoScroll = true, BackColor = Theme.Surface, Margin = Padding.Empty };
    private readonly Carte _carteFiche = new() { Padding = new Padding(24), CouleurBordure = Theme.BordureForte };
    private readonly Bascule _onglets = new("Moniteurs", "Classes", "Adhérents");
    private readonly BoutonArrondi _ajouter = new("Ajouter un moniteur", StyleBouton.Principal, Icones.Plus);
    private readonly BoutonArrondi _importer = new("Importer la liste Excel", StyleBouton.Secondaire, Icones.Document) { Visible = false };
    private readonly Dictionary<GenreResponsable, long?> _selection = [];
    private IReadOnlyList<FicheMoniteur> _tous = [];
    private IReadOnlyList<FicheMoniteur> _liste = [];

    // Onglet Adhérents : recherche par nom, filtres, adhérent choisi.
    private readonly BarreRecherche _recherche = new("Rechercher un adhérent…", "nom ou prénom") { Margin = new Padding(0, 0, 14, 0) };
    private readonly Bascule _filtreAdherents = new("Tous", "Matériel dehors", "Non rendu");
    private readonly System.Windows.Forms.Timer _attenteRecherche = new() { Interval = 300 };
    private readonly TableLayoutPanel _page;
    private IReadOnlyList<FicheAdherent> _listeAdherents = [];
    private long? _adherentChoisi;

    private readonly GardeGestion _garde;

    public EcranMoniteurs(ServiceMoniteurs moniteurs, ServiceAdherents adherents, INavigation navigation, GardeGestion garde)
    {
        _garde = garde;
        _moniteurs = moniteurs;
        _adherents = adherents;
        _navigation = navigation;

        _garde.Lier(_ajouter, ActionProtegee.Moniteurs);
        _garde.Lier(_importer, ActionProtegee.Moniteurs);
        _ajouter.Click += (_, _) => Executer(() => Adherents ? ModifierAdherentAsync(null) : ModifierAsync(null, Genre));
        _importer.Click += (_, _) => Executer(ImporterAsync);

        _onglets.AccessibleName = "Membres";
        _onglets.Margin = new Padding(0, 0, 0, 22);
        _onglets.Changement += (_, _) => Executer(AfficherGenreAsync);

        _filtreAdherents.AccessibleName = "Filtrer les adhérents";
        _filtreAdherents.Changement += (_, _) => Executer(AfficherAdherentsAsync);
        _recherche.Champ.TextChanged += (_, _) =>
        {
            _attenteRecherche.Stop();
            _attenteRecherche.Start();
        };
        _attenteRecherche.Tick += (_, _) =>
        {
            _attenteRecherche.Stop();
            Executer(AfficherAdherentsAsync);
        };

        Natif.ScrollbarsDuTheme(_fiche);
        _fiche.Dock = DockStyle.Fill;
        _carteFiche.Controls.Add(_fiche);

        var onglets = Ui.Rangee(true, _onglets);
        // Le filtre remplit sa cellule (ancré seulement, il gardait la hauteur nulle de la rangée masquée au départ).
        var filtres = Ui.Colonnes((_recherche, SizeType.Percent, 100), (_filtreAdherents, SizeType.AutoSize, 0));
        filtres.Margin = new Padding(0, 0, 0, 20);
        _page = Ui.Colonne(
            (Ui.Entete(Ui.Pile(Ui.Titre(Titre), Ui.SousTitre("Les responsables de sortie, les adhérents du club et le matériel encore dehors.")), _ajouter, _importer), SizeType.AutoSize, 0),
            (onglets, SizeType.AutoSize, 0),
            (filtres, SizeType.Absolute, 0), // 54 + 20 sur l'onglet Adhérents.
            (Ui.Colonnes((_cartes, SizeType.Percent, 60), (_carteFiche, SizeType.Percent, 40)), SizeType.Percent, 100));
        filtres.Visible = false;
        Controls.Add(_page);
    }

    public override string Titre => "Membres du club";

    public override EntreeMenu Menu => EntreeMenu.Moniteurs;

    private GenreResponsable Genre => _onglets.SelectedIndex == 1 ? GenreResponsable.Classe : GenreResponsable.Moniteur;

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

        _onglets.DefinirNombre(0, _tous.Count(f => !f.Moniteur.EstClasse));
        _onglets.DefinirNombre(1, _tous.Count(f => f.Moniteur.EstClasse));
        _onglets.DefinirNombre(OngletAdherents, adherents.Tous);
        await AfficherGenreAsync();
    }

    /// <summary>Cartes de l'onglet choisi (moniteurs ou classes) et fiche du responsable choisi dans cet onglet.</summary>
    private async Task AfficherGenreAsync()
    {
        // Barre de recherche et filtres : seulement pour les adhérents.
        var filtres = _page.GetControlFromPosition(0, 2)!;
        if (filtres.Visible != Adherents)
        {
            filtres.Visible = Adherents;
            _page.RowStyles[2].Height = Adherents ? Dessin.Echelle(this, 54 + 20) : 0;
        }

        _importer.Visible = Adherents;
        if (Adherents)
        {
            _ajouter.Text = _ajouter.AccessibleName = "Ajouter un adhérent";
            await AfficherAdherentsAsync();
            return;
        }

        var genre = Genre;
        _ajouter.Text = Classes ? "Ajouter une classe" : "Ajouter un moniteur";
        _ajouter.AccessibleName = _ajouter.Text;
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
                Ligne(Ui.Libelle(_listeAdherents.Count == 0 && _recherche.Champ.Text.Trim().Length == 0 && _filtreAdherents.SelectedIndex == 0
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
            Ligne(Ui.Libelle(Classes ? "Aucune classe. Cliquez sur « Ajouter une classe »." : "Aucun moniteur. Cliquez sur « Ajouter un moniteur ».", Theme.Police(11f), Theme.TexteSecondaireClair));
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
        if (m.Telephone.Length > 0)
        {
            var tel = Ui.Rangee(true, new Picto(Icones.Telephone, 18, Theme.TexteSecondaire, Color.Transparent) { Margin = new Padding(0, 2, 6, 0) },
                Ui.Libelle(m.Telephone, Theme.Police(9.75f), Theme.TexteDoux));
            textes.Controls.Add(tel);
        }

        var entete = Ui.Colonnes((new Avatar(m, index, 72), SizeType.Absolute, 72 + 16), (textes, SizeType.Percent, 100));
        entete.Height = 80;
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
    }

    // ================================================================ Adhérents

    /// <summary>Cartes des adhérents (recherche et filtre), compteurs des filtres, puis la fiche de l'adhérent choisi.</summary>
    private async Task AfficherAdherentsAsync()
    {
        if (!Adherents)
        {
            return;
        }

        var filtre = (FiltreAdherents)_filtreAdherents.SelectedIndex;
        var liste = await _adherents.ListerAsync(_recherche.Champ.Text, filtre);
        if (IsDisposed)
        {
            return;
        }

        _filtreAdherents.DefinirNombre(0, liste.Tous);
        _filtreAdherents.DefinirNombre(1, liste.MaterielDehors);
        _filtreAdherents.DefinirNombre(2, liste.NonRendu);
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
        var qualite = string.Join(" · ", new[] { "Adhérent", a.Niveau, a.Nitrox ? "certifié nitrox" : null, a.Actif ? null : "désactivé" }.Where(p => !string.IsNullOrWhiteSpace(p)));
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

        var avatar = new AvatarAdherent(a, index, 72);
        var entete = Ui.Colonnes((avatar, SizeType.Absolute, 72 + 16), (textes, SizeType.Percent, 100));
        entete.Height = 96;
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
            Margin = Padding.Empty,
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

        var prenom = new ChampSaisie { Valeur = a?.Prenom ?? "" };
        var nom = new ChampSaisie { Valeur = a?.Nom ?? "" };
        var niveau = new ChampSaisie { Valeur = a?.Niveau ?? "", TexteIndicatif = "Niveau 1, Niveau 2…" };
        var licence = new ChampSaisie { Valeur = a?.Licence ?? "", TexteIndicatif = "A-12-345678" };
        var telephone = new ChampSaisie { Valeur = a?.Telephone ?? "", TexteIndicatif = "06 12 34 56 78" };
        var actif = new Interrupteur("Actif") { Checked = a?.Actif ?? true };
        var nitrox = new Interrupteur("Certifié nitrox") { Checked = a?.Nitrox ?? false };
        prenom.Champ.AccessibleName = "Prénom";
        nom.Champ.AccessibleName = "Nom";
        niveau.Champ.AccessibleName = "Niveau";
        licence.Champ.AccessibleName = "Licence";
        licence.Champ.CharacterCasing = CharacterCasing.Upper;
        telephone.Champ.AccessibleName = "Téléphone";
        foreach (var (champ, max) in new[] { (prenom, Adherent.LongueurMaxNom), (nom, Adherent.LongueurMaxNom), (niveau, Adherent.LongueurMaxNiveau),
            (licence, Adherent.LongueurMaxLicence), (telephone, Adherent.LongueurMaxTelephone) })
        {
            champ.Champ.MaxLength = max;
        }

        ResultatOperation? enregistre = null;
        using var fenetre = new FenetreFormulaire(titre, a is null ? "Ajouter" : "Enregistrer", async () => enregistre = await _adherents.EnregistrerAsync(new Adherent
        {
            Id = a?.Id ?? 0,
            Prenom = prenom.Valeur,
            Nom = nom.Valeur,
            Niveau = niveau.Valeur,
            Licence = licence.Valeur,
            Telephone = telephone.Valeur,
            Actif = actif.Checked,
            Nitrox = nitrox.Checked,
        }));
        fenetre.DeuxChamps("Prénom", prenom, "Nom", nom);
        fenetre.DeuxChamps("Niveau", niveau, "Licence", licence);
        fenetre.Champ("Téléphone", telephone);
        fenetre.Ajouter(new LigneReglage("Certifié nitrox", "Peut prendre un bloc ou un détendeur nitrox", nitrox, filet: false));
        if (a is not null)
        {
            fenetre.Ajouter(new LigneReglage("Actif", "Désactivé : ne peut plus sortir de matériel ; son historique est gardé", actif));
        }

        if (fenetre.ShowDialog(FindForm()) == DialogResult.OK && enregistre?.Id is { } id)
        {
            _adherentChoisi = id;
            await RechargerAsync();
        }
    }

    /// <summary>
    /// « Importer la liste Excel » (.xlsx ou .csv) : les colonnes Nom et Prénom sont obligatoires, Niveau, Licence et
    /// Téléphone facultatives. Un adhérent de même licence est mis à jour ; les autres sont ajoutés.
    /// </summary>
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
            parties.Add("Moniteur");
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

    /// <summary>Ajout (m = null) ou modification d'un moniteur ou d'une classe dans une petite fenêtre.</summary>
    private async Task ModifierAsync(Moniteur? m, GenreResponsable genre)
    {
        var classe = genre == GenreResponsable.Classe;
        var titre = classe
            ? m is null ? "Ajouter une classe" : "Modifier la classe"
            : m is null ? "Ajouter un moniteur" : "Modifier le moniteur";
        if (!_garde.Exiger(FindForm(), ActionProtegee.Moniteurs, titre))
        {
            return;
        }

        ResultatOperation? enregistre = null;
        var actif = new Interrupteur("Actif") { Checked = m?.Actif ?? true };
        FenetreFormulaire fenetre;
        if (classe)
        {
            var nom = new ChampSaisie { Valeur = m?.NomComplet ?? "", TexteIndicatif = "Niveau 1, Niveau 2…" };
            var abrege = new ChampSaisie { Valeur = m?.Abrege ?? "", TexteIndicatif = "N1" };
            nom.Champ.AccessibleName = "Nom de la classe";
            nom.Champ.MaxLength = ServiceMoniteurs.LongueurMaxClasse;
            abrege.Champ.AccessibleName = "Abréviation";
            abrege.Champ.MaxLength = ServiceMoniteurs.LongueurMaxAbrege;
            abrege.Champ.CharacterCasing = CharacterCasing.Upper;

            // Abréviation proposée d'après le nom, tant qu'elle n'a pas été tapée.
            nom.Champ.TextChanged += (_, _) => abrege.TexteIndicatif = ServiceMoniteurs.AbregeDe(nom.Valeur) is { Length: > 0 } a ? a : "N1";

            fenetre = new FenetreFormulaire(titre, m is null ? "Ajouter" : "Enregistrer", async () => enregistre = await _moniteurs.EnregistrerAsync(new Moniteur
            {
                Id = m?.Id ?? 0,
                Genre = GenreResponsable.Classe,
                Nom = nom.Valeur,
                Abrege = abrege.Valeur,
                Actif = actif.Checked,
            }));
            fenetre.Champ("Nom de la classe", nom);
            fenetre.Champ("Abréviation (pastille, 4 caractères au plus)", abrege);
            if (m is not null)
            {
                fenetre.Ajouter(new LigneReglage("Active", "Une classe inactive n'est plus proposée pour les sorties", actif));
            }
        }
        else
        {
            var prenom = new ChampSaisie { Valeur = m?.Prenom ?? "" };
            var nom = new ChampSaisie { Valeur = m?.Nom ?? "" };
            var niveau = new ChampSaisie { Valeur = m?.Niveau ?? "", TexteIndicatif = "E3, MF1, MF2…" };
            var telephone = new ChampSaisie { Valeur = m?.Telephone ?? "", TexteIndicatif = "06 12 34 56 78" };
            var tiv = new Interrupteur("Inspecteur TIV") { Checked = m?.InspecteurTiv ?? false };
            prenom.Champ.AccessibleName = "Prénom";
            nom.Champ.AccessibleName = "Nom";
            niveau.Champ.AccessibleName = "Niveau";
            telephone.Champ.AccessibleName = "Téléphone";

            fenetre = new FenetreFormulaire(titre, m is null ? "Ajouter" : "Enregistrer", async () => enregistre = await _moniteurs.EnregistrerAsync(new Moniteur
            {
                Id = m?.Id ?? 0,
                Genre = GenreResponsable.Moniteur,
                Prenom = prenom.Valeur,
                Nom = nom.Valeur,
                Niveau = niveau.Valeur,
                Telephone = telephone.Valeur,
                InspecteurTiv = tiv.Checked,
                Actif = actif.Checked,
            }));
            fenetre.Champ("Prénom", prenom);
            fenetre.Champ("Nom", nom);
            fenetre.Champ("Niveau", niveau);
            fenetre.Champ("Téléphone", telephone);
            fenetre.Ajouter(new LigneReglage("Inspecteur TIV", "Peut faire les TIV des blocs du club", tiv, filet: false));
            if (m is not null)
            {
                fenetre.Ajouter(new LigneReglage("Actif", "Un moniteur inactif n'est plus proposé pour les sorties", actif));
            }
        }

        using (fenetre)
        {
            if (fenetre.ShowDialog(FindForm()) == DialogResult.OK && enregistre?.Id is { } id)
            {
                _selection[genre] = id;
                await RechargerAsync();
            }
        }
    }

    /// <summary>Pastille avec les initiales en dégradé : ronde pour un moniteur, carrée arrondie avec l'abréviation pour une classe.</summary>
    private sealed class Avatar : ControleDessine
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

        /// <summary>Les classes commencent plus loin dans les dégradés (comme la maquette) : N1 n'a pas la couleur du premier moniteur.</summary>
        public static (Color Clair, Color Fonce) Degrade(Moniteur m, int index) =>
            Theme.DegradesAvatar[(index + (m.EstClasse ? 2 : 0)) % Theme.DegradesAvatar.Length];

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
            AccessibleName = $"{a.NomComplet}{(a.Niveau is null ? "" : $", {a.Niveau}")}, {Libelles.Pluriel(fiche.NbDehors, "équipement")} dehors, {fiche.NbNonRendus} non rendu{(fiche.NbNonRendus > 1 ? "s" : "")}{(a.Nitrox ? ", certifié nitrox" : "")}{(a.Actif ? "" : ", désactivé")}";
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
