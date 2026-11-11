using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Manuels (maquette « Manuels ») : à gauche les notices des fabricants (filtre par catégorie), à droite le lecteur :
/// barre (pages, zoom, modifier, retirer, plein écran, imprimer), sommaire et « Entretien demandé par le fabricant »
/// comparé au réglage du club, page du PDF rendue par Windows, équipements liés en bas.
/// </summary>
public sealed class EcranManuels : EcranBase
{
    private const int LargeurListe = 400;
    private const int ZoomMin = 60;
    private const int ZoomMax = 400; // Notices en grand format (masque : toutes les langues sur une page).

    private readonly ServiceManuels _manuels;
    private readonly GardeGestion _garde;
    private readonly INavigation _navigation;

    private readonly FiltreCategories _filtre = new(Theme.AccentClair) { Margin = Padding.Empty, Unite = "manuel" };
    private readonly ListeManuels _liste = new();
    private readonly BoutonArrondi _ajouter = new("Ajouter un manuel (PDF)", StyleBouton.Principal, Icones.Plus);
    private readonly TableLayoutPanel _corps;
    private readonly Carte _lecteur = new() { Padding = new Padding(1), Margin = new Padding(22, 0, 0, 0) };

    private readonly Label _titreLecteur = Ui.Libelle("Manuels", Theme.PoliceTitre(12f));
    private readonly Label _fichier = Ui.Libelle("", Theme.PoliceCode(8.25f), Theme.TexteSecondaire);
    private readonly Label _pages = Etiquette(104);
    private readonly Label _zoomTexte = Etiquette(64);
    private readonly BoutonArrondi _precedente = Icone("Page précédente", Icones.ChevronGauche);
    private readonly BoutonArrondi _suivante = Icone("Page suivante", Icones.ChevronDroite);
    private readonly BoutonArrondi _moins = Icone("Dézoomer", Icones.Moins);
    private readonly BoutonArrondi _plus = Icone("Zoomer", Icones.Plus);
    private readonly BoutonArrondi _modifier = Icone("Modifier le manuel", Icones.Crayon);
    private readonly BoutonArrondi _retirer = Icone("Retirer le manuel", Icones.Corbeille);
    private readonly BoutonArrondi _plein = Icone("Plein écran", Icones.PleinEcran);
    private readonly BoutonArrondi _imprimer = Icone("Imprimer", Icones.Imprimante);
    private readonly ColonneSommaire _sommaire = new();
    private readonly VuePage _vue = new();
    private readonly FlowLayoutPanel _lies = new()
    {
        Dock = DockStyle.Fill,
        WrapContents = false,
        BackColor = Color.Transparent,
        Padding = new Padding(18, 10, 18, 0),
        Margin = Padding.Empty,
    };

    private readonly System.Windows.Forms.Timer _attenteTaille = new() { Interval = 200 };

    private IReadOnlyList<Manuel> _tous = [];
    private Manuel? _selection;
    private LecteurPdf? _pdf;
    private int _page = 1;
    private int _zoom = 100;
    private int _derniereLargeur;
    private int _rendu; // Jeton : une page rendue après un changement plus récent est ignorée.
    private bool _pleinEcran;

    public EcranManuels(ServiceManuels manuels, GardeGestion garde, INavigation navigation)
    {
        _manuels = manuels;
        _garde = garde;
        _navigation = navigation;

        _garde.Lier(_ajouter, ActionProtegee.Materiel);
        _garde.Lier(_modifier, ActionProtegee.Materiel);
        _garde.Lier(_retirer, ActionProtegee.Materiel);
        _garde.Lier(_sommaire.Regler, ActionProtegee.Parametres);

        _ajouter.Click += (_, _) => Executer(AjouterAsync);
        _filtre.Changement += (_, _) => AfficherListe();
        _liste.Choix += (_, m) => Executer(() => SelectionnerAsync(m));
        _precedente.Click += (_, _) => Aller(_page - 1, 0);
        _suivante.Click += (_, _) => Aller(_page + 1, 0);
        _moins.Click += (_, _) => Zoomer(-10);
        _plus.Click += (_, _) => Zoomer(10);
        _modifier.Click += (_, _) => Executer(ModifierAsync);
        _retirer.Click += (_, _) => Executer(RetirerAsync);
        _plein.Click += (_, _) => BasculerPleinEcran();
        _imprimer.Click += (_, _) => Imprimer();
        _sommaire.PageDemandee += (_, page) => Aller(page, 0);
        _sommaire.Aligner += (_, _) => AlignerRevision();
        _vue.PageDemandee += (_, sens) => Aller(_page + sens, sens > 0 ? 0 : 1);
        _vue.SizeChanged += (_, _) =>
        {
            _attenteTaille.Stop();
            _attenteTaille.Start();
        };
        _attenteTaille.Tick += (_, _) =>
        {
            _attenteTaille.Stop();
            if (_pdf is not null && Math.Abs(_vue.LargeurPage(_zoom) - _derniereLargeur) > 4)
            {
                Executer(() => AfficherPageAsync(null));
            }
        };

        // Barre du lecteur : titre et fichier, pages, zoom, outils.
        // Titre et fichier sur une ligne chacun, coupés par « … » quand la barre est étroite.
        var titres = Ui.Colonne((_titreLecteur, SizeType.Absolute, 26), (_fichier, SizeType.Absolute, 18));
        titres.Dock = DockStyle.None;
        titres.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        titres.Height = 44;
        foreach (var l in new[] { _titreLecteur, _fichier })
        {
            l.AutoSize = false;
            l.AutoEllipsis = true;
            l.Dock = DockStyle.Fill;
            l.Margin = Padding.Empty;
        }
        var barre = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Color.Transparent,
            Padding = new Padding(18, 0, 14, 0),
            Margin = Padding.Empty,
        };
        barre.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        barre.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        barre.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        barre.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        barre.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        barre.Controls.Add(titres, 0, 0);
        barre.Controls.Add(new Groupe(_precedente, _pages, _suivante), 1, 0);
        barre.Controls.Add(new Groupe(_moins, _zoomTexte, _plus), 2, 0);
        barre.Controls.Add(new Groupe(_modifier, _retirer, _plein, _imprimer), 3, 0);
        barre.Paint += (_, e) => Filet(e.Graphics, barre, enBas: true);

        var etiquetteLies = Ui.Libelle("Équipements liés", Theme.PoliceTitre(9.75f), Theme.TexteSecondaireClair);
        etiquetteLies.Margin = new Padding(0, 13, 10, 0);
        _lies.Controls.Add(etiquetteLies);
        _lies.Paint += (_, e) => Filet(e.Graphics, _lies, enBas: false);

        var contenuLecteur = Ui.Colonne(
            (barre, SizeType.Absolute, 74),
            (Ui.Colonnes((_sommaire, SizeType.Absolute, 284), (_vue, SizeType.Percent, 100)), SizeType.Percent, 100),
            (_lies, SizeType.Absolute, 64));
        _lecteur.Controls.Add(contenuLecteur);

        _corps = Ui.Colonnes((_liste, SizeType.Absolute, LargeurListe), (_lecteur, SizeType.Percent, 100));
        Controls.Add(Ui.Colonne(
            (Ui.Entete(Ui.Pile(Ui.Titre(Titre), Ui.SousTitre("Les notices des fabricants, lisibles directement dans l'appli, même sans internet.")), _ajouter), SizeType.AutoSize, 0),
            (_filtre, SizeType.Absolute, 62),
            (_corps, SizeType.Percent, 100)));
        MettreAJourBarre();
    }

    public override string Titre => "Manuels";

    public override EntreeMenu Menu => EntreeMenu.Manuels;

    protected override Task ChargerAsync(object? parametre) => RechargerAsync(parametre as long?, garderPage: false);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _attenteTaille.Dispose();
        }

        base.Dispose(disposing);
    }

    // ---------------------------------------------------------------- Liste

    private async Task RechargerAsync(long? choisir, bool garderPage)
    {
        _tous = await _manuels.ListerAsync();
        var categories = await _manuels.ListerCategoriesAsync();
        _filtre.Afficher(_tous.Count, categories
            .Select(c => (c.Nom, _tous.Count(m => m.Categorie == c.Nom)))
            .Where(c => c.Item2 > 0));
        AfficherListe();

        var visibles = Visibles();
        var cible = visibles.FirstOrDefault(m => m.Id == (choisir ?? _selection?.Id)) ?? visibles.FirstOrDefault();
        await SelectionnerAsync(cible, forcer: true, garderPage: garderPage);
    }

    private List<Manuel> Visibles() => _tous.Where(m => _filtre.Accepte(m.Categorie)).ToList();

    private void AfficherListe()
    {
        var vide = _tous.Count == 0
            ? "Aucun manuel pour l'instant.\nAjoutez la notice d'un fabricant avec « Ajouter un manuel (PDF) » : elle sera lisible ici, même sans internet."
            : "Aucun manuel dans cette catégorie.";
        _liste.Afficher(Visibles(), _selection?.Id, vide);
    }

    private async Task SelectionnerAsync(Manuel? manuel, bool forcer = false, bool garderPage = false)
    {
        if (!forcer && manuel?.Id == _selection?.Id)
        {
            return;
        }

        _selection = manuel;
        _liste.Selectionner(manuel?.Id);
        _pdf = null;
        _rendu++;
        if (manuel is null)
        {
            _titreLecteur.Text = "Manuels";
            _fichier.Text = "";
            _sommaire.Afficher(null, null);
            AfficherLies(null);
            _vue.Message = _tous.Count == 0 ? "Les manuels ajoutés s'affichent ici, page par page." : "Choisissez un manuel dans la liste.";
            MettreAJourBarre();
            return;
        }

        _titreLecteur.Text = $"{manuel.Marque} · {manuel.Titre}";
        _fichier.Text = manuel.Fichier;
        _sommaire.Afficher(manuel, _manuels.Conformite(manuel));
        AfficherLies(await _manuels.ListerCodesEquipementsAsync(manuel.Id));

        var page = garderPage ? _page : 1; // Un manuel s'ouvre sur sa première page ; « Lire la page 17 » mène à l'entretien.
        if (_manuels.CheminFichier(manuel) is not { } chemin)
        {
            _vue.Message = $"Fichier introuvable : {manuel.Fichier}.\nIl a été déplacé ou supprimé du dossier « Manuels ». Retirez ce manuel puis ajoutez de nouveau le PDF.";
            MettreAJourBarre();
            return;
        }

        _vue.Message = "Ouverture du manuel…";
        LecteurPdf pdf;
        try
        {
            pdf = await LecteurPdf.OuvrirAsync(chemin);
        }
        catch (Exception ex) when (LecteurPdf.EstIllisible(ex))
        {
            _vue.Message = "Ce PDF ne peut pas être lu (abîmé ou protégé par un mot de passe).";
            MettreAJourBarre();
            return;
        }

        if (_selection?.Id != manuel.Id)
        {
            return; // Un autre manuel a été choisi pendant l'ouverture.
        }

        _pdf = pdf;
        _page = Math.Clamp(page, 1, pdf.NbPages);
        await AfficherPageAsync(0);
    }

    // ---------------------------------------------------------------- Lecteur

    private void Aller(int page, int? defiler)
    {
        if (_pdf is null || page < 1 || page > _pdf.NbPages || page == _page)
        {
            return;
        }

        _page = page;
        Executer(() => AfficherPageAsync(defiler));
    }

    private void Zoomer(int pas)
    {
        var zoom = Math.Clamp(_zoom + pas, ZoomMin, ZoomMax);
        if (zoom != _zoom && _pdf is not null)
        {
            _zoom = zoom;
            Executer(() => AfficherPageAsync(null));
        }
    }

    /// <summary>Rend la page courante à la largeur du lecteur. <paramref name="defiler"/> : 0 haut, 1 bas, null sans bouger.</summary>
    private async Task AfficherPageAsync(int? defiler)
    {
        if (_pdf is not { } pdf)
        {
            return;
        }

        MettreAJourBarre();
        _sommaire.PageCourante = _page;
        var jeton = ++_rendu;
        var largeur = _vue.LargeurPage(_zoom);
        _derniereLargeur = largeur;
        var image = await pdf.RendrePageAsync(_page, largeur);
        if (jeton != _rendu || IsDisposed || !ReferenceEquals(pdf, _pdf))
        {
            image.Dispose();
            return;
        }

        _vue.Afficher(image, defiler, $"Page {_page} du manuel {_selection?.Marque} {_selection?.Titre}");
    }

    private void MettreAJourBarre()
    {
        var ouvert = _pdf is not null;
        _pages.Text = ouvert ? $"Page {_page} / {_pdf!.NbPages}" : "Page –";
        _zoomTexte.Text = $"{_zoom} %";
        _precedente.Enabled = ouvert && _page > 1;
        _suivante.Enabled = ouvert && _page < _pdf!.NbPages;
        _moins.Enabled = ouvert && _zoom > ZoomMin;
        _plus.Enabled = ouvert && _zoom < ZoomMax;
        _imprimer.Enabled = ouvert;
        _modifier.Enabled = _retirer.Enabled = _selection is not null;
        _plein.AccessibleName = _pleinEcran ? "Quitter le plein écran" : "Plein écran";
    }

    private void BasculerPleinEcran()
    {
        _pleinEcran = !_pleinEcran;
        _liste.Visible = !_pleinEcran;
        _corps.ColumnStyles[0].Width = _pleinEcran ? 0 : Dessin.Echelle(this, LargeurListe);
        _lecteur.Margin = new Padding(_pleinEcran ? 0 : (int)Dessin.Echelle(this, 22), 0, 0, 0);
        MettreAJourBarre();
    }

    private void Imprimer()
    {
        if (_pdf is not { } pdf || _selection is not { } manuel)
        {
            return;
        }

        using var document = new PrintDocument { DocumentName = $"{manuel.Marque} {manuel.Titre}" };
        document.PrinterSettings.MinimumPage = 1;
        document.PrinterSettings.MaximumPage = pdf.NbPages;
        document.PrinterSettings.FromPage = 1;
        document.PrinterSettings.ToPage = pdf.NbPages;
        using var dialogue = new PrintDialog { Document = document, AllowSomePages = true, AllowCurrentPage = true, UseEXDialog = true };
        if (dialogue.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        var (debut, fin) = document.PrinterSettings.PrintRange switch
        {
            PrintRange.SomePages => (Math.Max(1, document.PrinterSettings.FromPage), Math.Min(pdf.NbPages, document.PrinterSettings.ToPage)),
            PrintRange.CurrentPage => (_page, _page),
            _ => (1, pdf.NbPages),
        };
        var page = debut;
        document.PrintPage += (_, e) =>
        {
            // Zone en centièmes de pouce : page rendue à 200 points par pouce puis réduite à la zone imprimable.
            var zone = e.MarginBounds;
            using var image = pdf.RendrePage(page, zone.Width * 2);
            var ratio = Math.Min(zone.Width / (float)image.Width, zone.Height / (float)image.Height);
            var largeur = image.Width * ratio;
            e.Graphics!.DrawImage(image, zone.X + ((zone.Width - largeur) / 2), zone.Y, largeur, image.Height * ratio);
            page++;
            e.HasMorePages = page <= fin;
        };

        try
        {
            document.Print();
        }
        catch (Exception ex) when (ex is InvalidPrinterException or Win32Exception)
        {
            Dialogues.Erreur(FindForm(), "Impression impossible : vérifiez que l'imprimante est allumée et branchée.");
        }
    }

    private void AfficherLies(IReadOnlyList<string>? codes)
    {
        foreach (var ancien in _lies.Controls.Cast<Control>().Skip(1).ToList())
        {
            _lies.Controls.Remove(ancien);
            ancien.Dispose();
        }

        if (codes is null)
        {
            return;
        }

        const int Max = 10;
        var nouveaux = new List<Control>();
        if (codes.Count == 0)
        {
            var aucun = Ui.Libelle("Aucun équipement de cette marque dans la catégorie.", Theme.Police(9.75f), Theme.TexteSecondaire);
            aucun.Margin = new Padding(0, 13, 0, 0);
            nouveaux.Add(aucun);
        }

        foreach (var code in codes.Take(Max))
        {
            var bouton = new BoutonArrondi(code, StyleBouton.Secondaire)
            {
                Font = Theme.PoliceCode(9.75f),
                Padding = new Padding(12, 0, 12, 0),
                Margin = new Padding(0, 0, 6, 0),
                AccessibleDescription = "Ouvre la vie de l'équipement dans Historique",
            };
            bouton.Click += (_, _) => _navigation.AllerA<EcranHistorique>(code);
            nouveaux.Add(bouton);
        }

        if (codes.Count > Max)
        {
            var autres = Ui.Libelle($"+ {codes.Count - Max} autre{(codes.Count - Max > 1 ? "s" : "")}", Theme.Police(9.75f), Theme.TexteSecondaire);
            autres.Margin = new Padding(4, 13, 0, 0);
            nouveaux.Add(autres);
        }

        foreach (var c in nouveaux)
        {
            Dessin.MettreALEchelle(c, this); // Ajoutés après l'affichage : pixels logiques → écran.
            _lies.Controls.Add(c);
        }
    }

    private void Filet(Graphics g, Control controle, bool enBas)
    {
        using var stylo = new Pen(Theme.Bordure, Dessin.Echelle(this, 1));
        var y = enBas ? controle.Height - 1 : 0;
        g.DrawLine(stylo, 0, y, controle.Width, y);
    }

    // ---------------------------------------------------------------- Actions

    private async Task AjouterAsync()
    {
        if (!_garde.Exiger(FindForm(), ActionProtegee.Materiel, "Ajouter un manuel"))
        {
            return;
        }

        if (await FormulaireManuel.AjouterAsync(FindForm(), _manuels) is { } id)
        {
            _filtre.Afficher(0, []); // Revient sur « Tout » pour montrer le nouveau manuel.
            await RechargerAsync(id, garderPage: false);
        }
    }

    private async Task ModifierAsync()
    {
        if (_selection is not { } manuel || !_garde.Exiger(FindForm(), ActionProtegee.Materiel, "Modifier un manuel"))
        {
            return;
        }

        if (await FormulaireManuel.ModifierAsync(FindForm(), _manuels, manuel))
        {
            await RechargerAsync(manuel.Id, garderPage: true);
        }
    }

    private async Task RetirerAsync()
    {
        if (_selection is not { } manuel || !_garde.Exiger(FindForm(), ActionProtegee.Materiel, "Retirer un manuel"))
        {
            return;
        }

        if (!Dialogues.Confirmer(FindForm(), $"Retirer le manuel « {manuel.Marque} · {manuel.Titre} » ?\nLe fichier PDF sera supprimé du dossier des manuels."))
        {
            return;
        }

        var resultat = await _manuels.SupprimerAsync(manuel.Id);
        if (!resultat.Reussi)
        {
            Dialogues.Erreur(FindForm(), resultat.Message);
            return;
        }

        _selection = null;
        await RechargerAsync(null, garderPage: false);
    }

    private void AlignerRevision()
    {
        if (_selection is not { } manuel || !_garde.Exiger(FindForm(), ActionProtegee.Parametres, "Régler le délai de révision"))
        {
            return;
        }

        var resultat = _manuels.AlignerRevision(manuel);
        if (!resultat.Reussi)
        {
            Dialogues.Erreur(FindForm(), resultat.Message);
            return;
        }

        _sommaire.Afficher(manuel, _manuels.Conformite(manuel));
        Dialogues.Information(FindForm(), $"{resultat.Message}\nL'échéancier de la page Révisions en tient compte.");
    }

    // Pas de pastille sur les petits boutons de la barre (elle recouvrirait le bouton voisin) : le pavé de code
    // s'ouvre au clic et la description accessible annonce le code.
    private static BoutonArrondi Icone(string nom, string icone) =>
        new(nom, StyleBouton.Icone, icone) { Margin = Padding.Empty, Anchor = AnchorStyles.None, AfficherPastille = false };

    /// <summary>Défilement vertical seulement : sans cela, la barre verticale apparue fait naître une barre horizontale.</summary>
    private static void DefilementVertical(ScrollableControl controle)
    {
        controle.HorizontalScroll.Maximum = 0;
        controle.AutoScroll = false;
        controle.VerticalScroll.Visible = false;
        controle.AutoScroll = true;
    }

    private static Label Etiquette(int largeur)
    {
        var l = Ui.Libelle("", Theme.PoliceTitre(9.75f));
        l.AutoSize = false;
        l.Size = new Size(largeur, Theme.HauteurCliquable);
        l.TextAlign = ContentAlignment.MiddleCenter;
        l.Margin = Padding.Empty;
        return l;
    }

    // ================================================================ Contrôles de l'écran

    /// <summary>Boutons regroupés dans un cadre arrondi (pages, zoom, outils).</summary>
    private sealed class Groupe : FlowLayoutPanel
    {
        public Groupe(params Control[] controles)
        {
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            WrapContents = false;
            Padding = new Padding(3);
            Margin = new Padding(8, 0, 0, 0);
            Anchor = AnchorStyles.None;
            BackColor = Theme.Sur(Theme.Surface, Theme.Fond, 0.5f);
            DoubleBuffered = true;
            Controls.AddRange(controles);
        }

        protected override void OnPaintBackground(PaintEventArgs e) =>
            Dessin.PeindreFondArrondi(this, e.Graphics, BackColor, Theme.Bordure, 12);
    }

    /// <summary>Liste des manuels : cartes placées les unes sous les autres, défilement vertical.</summary>
    private sealed class ListeManuels : Panel
    {
        private readonly List<CarteManuel> _cartes = [];
        private string _vide = "";

        public ListeManuels()
        {
            DefilementVertical(this);
            BackColor = Theme.Fond;
            DoubleBuffered = true;
            Margin = Padding.Empty;
            SetStyle(ControlStyles.ResizeRedraw, true);
            Natif.ScrollbarsDuTheme(this);
        }

        public event EventHandler<Manuel>? Choix;

        public void Afficher(IEnumerable<Manuel> manuels, long? selection, string vide)
        {
            _vide = vide;
            SuspendLayout();
            foreach (var c in _cartes)
            {
                Controls.Remove(c);
                c.Dispose();
            }

            _cartes.Clear();
            foreach (var m in manuels)
            {
                var carte = new CarteManuel(m) { Selectionne = m.Id == selection };
                carte.Click += (_, _) => Choix?.Invoke(this, m);
                _cartes.Add(carte);
                Controls.Add(carte);
            }

            ResumeLayout(true);
            Invalidate();
        }

        public void Selectionner(long? id)
        {
            foreach (var c in _cartes)
            {
                c.Selectionne = c.Manuel.Id == id;
            }
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            var hauteur = (int)Dessin.Echelle(this, 128);
            var ecart = (int)Dessin.Echelle(this, 12);
            var largeur = ClientSize.Width - (int)Dessin.Echelle(this, 2);
            var y = 0;
            foreach (var c in _cartes)
            {
                c.SetBounds(0, y + AutoScrollPosition.Y, largeur, hauteur);
                y += hauteur + ecart;
            }

            if (AutoScrollMinSize.Height != y)
            {
                AutoScrollMinSize = new Size(0, y);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_cartes.Count == 0 && _vide.Length > 0)
            {
                var zone = new Rectangle((int)Dessin.Echelle(this, 6), (int)Dessin.Echelle(this, 12), Width - (int)Dessin.Echelle(this, 12), Height);
                TextRenderer.DrawText(e.Graphics, _vide, Theme.Police(10.5f), zone, Theme.TexteSecondaireClair, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }
        }
    }

    /// <summary>Carte d'un manuel : couverture (couleur de la marque, pictogramme de la catégorie), marque, titre, modèles, pastilles.</summary>
    private sealed class CarteManuel : ControleDessine
    {
        private bool _selectionne;
        private bool _survol;

        public CarteManuel(Manuel manuel)
        {
            Manuel = manuel;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = $"{manuel.Marque} {manuel.Titre}, {manuel.NbPages} pages";
        }

        public Manuel Manuel { get; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selectionne
        {
            get => _selectionne;
            set
            {
                if (_selectionne != value)
                {
                    Invalider(ref _selectionne, value);
                    AccessibleDescription = value ? "Ouvert dans le lecteur" : null;
                }
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

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            base.OnMouseDown(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                OnClick(EventArgs.Empty);
                e.Handled = true;
            }

            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void Peindre(Graphics g)
        {
            var m = Manuel;
            var surface = _selectionne ? Theme.Sur(Theme.Surface, Theme.Accent, 0.10f)
                : _survol ? Theme.Sur(Theme.Surface, Theme.AccentClair, 0.05f)
                : Theme.Surface;
            var r = new RectangleF(0.75f, 0.75f, Width - 2f, Height - 2f);
            using (var forme = Dessin.Arrondi(r, E(20)))
            using (var fond = new SolidBrush(surface))
            using (var bord = new Pen(_selectionne ? Theme.Sur(surface, Theme.Accent, 0.6f) : Theme.Bordure, E(_selectionne ? 1.5f : 1f)))
            {
                g.FillPath(fond, forme);
                g.DrawPath(bord, forme);
            }

            if (Focused && ShowFocusCues)
            {
                using var focus = new Pen(Theme.Accent, E(1.5f));
                using var interieur = Dessin.Arrondi(RectangleF.Inflate(r, -E(4), -E(4)), E(16));
                g.DrawPath(focus, interieur);
            }

            // Couverture : dégradé choisi d'après la marque (toujours le même pour une marque).
            var cx = E(16);
            var cw = E(64);
            var ch = E(84);
            var cy = (Height - ch) / 2;
            var couverture = new RectangleF(cx, cy, cw, ch);
            var (clair, fonce) = Theme.DegradesAvatar[m.Marque.ToUpperInvariant().Sum(c => c) % Theme.DegradesAvatar.Length];
            using (var forme = Dessin.Arrondi(couverture, E(10)))
            using (var degrade = new LinearGradientBrush(couverture, clair, fonce, 60f))
            {
                g.FillPath(degrade, forme);
            }

            var t = E(26);
            Icones.Dessiner(g, Icones.PourCategorie(m.Categorie), new RectangleF(cx + ((cw - t) / 2), cy + E(18), t, t), Theme.TexteSurAvatar, 1.8f);
            Dessin.Texte(g, "PDF", Theme.PoliceTitre(6.75f), new Rectangle((int)cx, (int)(cy + E(50)), (int)cw, (int)E(16)), Theme.TexteSurAvatar, centre: true);

            // Textes.
            var x = (int)(cx + cw + E(16));
            var largeur = Width - x - (int)E(14);
            var y = (int)((Height - E(96)) / 2);
            Dessin.Texte(g, m.Marque.ToUpperInvariant(), Theme.PoliceTitre(8.25f), new Rectangle(x, y, largeur, (int)E(18)), Theme.AccentClair);
            Dessin.Texte(g, m.Titre, Theme.PoliceTitre(12f), new Rectangle(x, y + (int)E(18), largeur, (int)E(26)), Theme.Texte);
            var sousTitre = m.Modeles.Length > 0 ? m.Modeles : m.Categorie ?? "";
            Dessin.Texte(g, sousTitre, Theme.Police(9f), new Rectangle(x, y + (int)E(45), largeur, (int)E(20)), Theme.TexteSecondaire);

            // Pastilles : pages, édition, équipements liés.
            var pastilles = new List<(string Texte, bool Accent)> { ($"{m.NbPages} pages", false) };
            if (m.Edition.Length > 0)
            {
                pastilles.Add((m.Edition, false));
            }

            if (m.NbEquipements > 0)
            {
                pastilles.Add((Libelles.Pluriel(m.NbEquipements, "équipement"), true));
            }

            var px = (float)x;
            var py = y + E(72);
            var police = Theme.PoliceTitre(8.25f);
            foreach (var (texte, accent) in pastilles)
            {
                var w = TextRenderer.MeasureText(texte, police, Size.Empty, TextFormatFlags.NoPadding).Width + E(18);
                if (px + w > x + largeur)
                {
                    break;
                }

                var zone = new RectangleF(px, py, w, E(22));
                using (var forme = Dessin.Arrondi(zone, zone.Height / 2))
                using (var fond = new SolidBrush(Theme.Sur(surface, Theme.AccentClair, accent ? 0.16f : 0.09f)))
                {
                    g.FillPath(fond, forme);
                }

                Dessin.Texte(g, texte, police, Rectangle.Round(zone), accent ? Theme.AccentClair : Theme.TexteDoux, centre: true);
                px += w + E(6);
            }
        }
    }

    /// <summary>Colonne gauche du lecteur : sommaire cliquable, puis l'encadré « Entretien demandé par le fabricant ».</summary>
    private sealed class ColonneSommaire : Panel
    {
        private readonly List<BoutonSection> _sections = [];
        private readonly EncadreEntretien _encadre = new();
        private Manuel? _manuel;

        public ColonneSommaire()
        {
            DefilementVertical(this);
            BackColor = Theme.Surface;
            DoubleBuffered = true;
            Margin = Padding.Empty;
            SetStyle(ControlStyles.ResizeRedraw, true);
            Natif.ScrollbarsDuTheme(this);
            _encadre.Visible = false;
            _encadre.PageDemandee += (_, page) => PageDemandee?.Invoke(this, page);
            _encadre.Regler.Click += (_, _) => Aligner?.Invoke(this, EventArgs.Empty);
            Controls.Add(_encadre);
        }

        public event EventHandler<int>? PageDemandee;

        public event EventHandler? Aligner;

        /// <summary>Bouton « Régler » de l'encadré (cadenas lié par l'écran).</summary>
        public BoutonArrondi Regler => _encadre.Regler;

        /// <summary>Met en évidence la dernière section qui commence à cette page ou avant.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int PageCourante
        {
            set
            {
                var courante = _sections.LastOrDefault(s => s.Section.Page <= value);
                foreach (var s in _sections)
                {
                    s.Selectionne = s == courante;
                }
            }
        }

        public void Afficher(Manuel? manuel, ConformiteEntretien? conformite)
        {
            _manuel = manuel;
            SuspendLayout();
            foreach (var s in _sections)
            {
                Controls.Remove(s);
                s.Dispose();
            }

            _sections.Clear();
            foreach (var section in manuel?.Sections ?? [])
            {
                var bouton = new BoutonSection(section);
                bouton.Click += (_, _) => PageDemandee?.Invoke(this, section.Page);
                _sections.Add(bouton);
                Controls.Add(bouton);
            }

            _encadre.Visible = manuel is not null;
            if (manuel is not null)
            {
                _encadre.Afficher(manuel, conformite);
            }

            AutoScrollPosition = Point.Empty;
            ResumeLayout(true);
            Invalidate();
        }

        private int Marge => (int)Dessin.Echelle(this, 16);

        private int HautTitre => (int)Dessin.Echelle(this, 30);

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            var marge = Marge;
            var largeur = ClientSize.Width - (2 * marge);
            var decalage = AutoScrollPosition.Y;
            var y = marge + HautTitre;
            var hauteur = (int)Dessin.Echelle(this, 44);
            foreach (var s in _sections)
            {
                s.SetBounds(marge, y + decalage, largeur, hauteur);
                y += hauteur + (int)Dessin.Echelle(this, 2);
            }

            if (_sections.Count == 0)
            {
                y += (int)Dessin.Echelle(this, 26);
            }

            y += (int)Dessin.Echelle(this, 14);
            if (_encadre.Visible)
            {
                var h = _encadre.Hauteur(largeur);
                _encadre.SetBounds(marge, y + decalage, largeur, h);
                y += h;
            }

            y += marge;
            if (AutoScrollMinSize.Height != y)
            {
                AutoScrollMinSize = new Size(0, y);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            var marge = Marge;
            var decalage = AutoScrollPosition.Y;
            Dessin.Texte(g, "SOMMAIRE", Theme.PoliceTitre(8.25f), new Rectangle(marge + (int)Dessin.Echelle(this, 8), marge + decalage, Width, HautTitre - (int)Dessin.Echelle(this, 6)), Theme.TexteSecondaire);
            if (_manuel is not null && _sections.Count == 0)
            {
                Dessin.Texte(g, "Pas de sommaire saisi.", Theme.Police(9.75f), new Rectangle(marge + (int)Dessin.Echelle(this, 8), marge + HautTitre + decalage, Width, (int)Dessin.Echelle(this, 22)), Theme.TexteSecondaire);
            }

            using var filet = new Pen(Theme.Bordure, Dessin.Echelle(this, 1));
            g.DrawLine(filet, ClientSize.Width - 1, 0, ClientSize.Width - 1, Height);
        }
    }

    /// <summary>Entrée du sommaire : titre (deux lignes au plus) et « p. 17 ».</summary>
    private sealed class BoutonSection : ControleDessine
    {
        private bool _selectionne;
        private bool _survol;

        public BoutonSection(SectionManuel section)
        {
            Section = section;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.Link;
            AccessibleName = $"{section.Titre}, page {section.Page}";
        }

        public SectionManuel Section { get; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selectionne
        {
            get => _selectionne;
            set
            {
                if (_selectionne != value)
                {
                    Invalider(ref _selectionne, value);
                }
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

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                OnClick(EventArgs.Empty);
                e.Handled = true;
            }

            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void Peindre(Graphics g)
        {
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            if (_selectionne || _survol)
            {
                using var forme = Dessin.Arrondi(r, E(10));
                using var fond = new SolidBrush(_selectionne ? Theme.Sur(Theme.Surface, Theme.Accent, 0.16f) : Theme.Sur(Theme.Surface, Theme.AccentClair, 0.06f));
                g.FillPath(fond, forme);
            }

            if (Focused && ShowFocusCues)
            {
                using var forme = Dessin.Arrondi(RectangleF.Inflate(r, -1.5f, -1.5f), E(9));
                using var stylo = new Pen(Theme.Accent, E(1.5f));
                g.DrawPath(stylo, forme);
            }

            var page = $"p. {Section.Page}";
            var policePage = Theme.Police(8.25f);
            var largeurPage = TextRenderer.MeasureText(page, policePage, Size.Empty, TextFormatFlags.NoPadding).Width;
            TextRenderer.DrawText(g, page, policePage, new Rectangle(0, 0, Width - (int)E(10), Height), Theme.TexteSecondaire, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

            var police = _selectionne ? Theme.PoliceTitre(9.75f) : Theme.Police(9.75f);
            var zone = new Rectangle((int)E(10), 0, Width - largeurPage - (int)E(28), Height);
            const TextFormatFlags Drapeaux = TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;
            var hauteur = Math.Min(zone.Height, TextRenderer.MeasureText(Section.Titre, police, new Size(zone.Width, int.MaxValue), Drapeaux).Height);
            zone = new Rectangle(zone.X, (Height - hauteur) / 2, zone.Width, hauteur);
            TextRenderer.DrawText(g, Section.Titre, police, zone, _selectionne ? Theme.TexteActif : Theme.TexteDoux, Drapeaux);
        }
    }

    /// <summary>
    /// Encadré « Entretien demandé par le fabricant » : délais saisis, lien vers la page de l'entretien, puis le réglage
    /// du club comparé au fabricant (et le bouton pour l'aligner).
    /// </summary>
    private sealed class EncadreEntretien : ControleDessine
    {
        private const string TitreEncadre = "Entretien demandé par le fabricant";
        private const TextFormatFlags Drapeaux = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;

        private readonly BoutonArrondi _lire = new("", StyleBouton.Lien) { AutoSize = false, Margin = Padding.Empty, Font = Theme.PoliceTitre(9.75f) };
        private Manuel? _manuel;
        private ConformiteEntretien? _conformite;
        private IReadOnlyList<(string Libelle, string Valeur)> _lignes = [];

        public EncadreEntretien()
        {
            BackColor = Theme.FondAvertissement; // Les boutons posés dessus recopient ce fond.
            Controls.Add(_lire);
            Controls.Add(Regler);
            _lire.Click += (_, _) =>
            {
                if (_manuel?.PageEntretien is { } page)
                {
                    PageDemandee?.Invoke(this, page);
                }
            };
        }

        public event EventHandler<int>? PageDemandee;

        // Sans pastille : elle déborderait de l'encadré. Le pavé de code s'ouvre au clic.
        public BoutonArrondi Regler { get; } = new("", StyleBouton.Secondaire) { AutoSize = false, Margin = Padding.Empty, Font = Theme.PoliceTitre(9.75f), AfficherPastille = false };

        public void Afficher(Manuel manuel, ConformiteEntretien? conformite)
        {
            _manuel = manuel;
            _conformite = conformite;
            _lignes = manuel.LignesEntretien.Count > 0 || manuel.MoisRevision is not { } mois
                ? manuel.LignesEntretien
                : [("Révision", ServiceManuels.DureeLisible(mois))];
            _lire.Text = manuel.PageEntretien is { } page ? $"Lire la page {page}" : "";
            _lire.Visible = manuel.PageEntretien is not null;
            Regler.Visible = conformite is { Conforme: false };
            Regler.Text = conformite is null ? "" : $"Régler le club : {ServiceManuels.DureeLisible(conformite.MoisFabricant)}";
            Regler.AccessibleDescription = $"Délai de révision des {manuel.Categorie?.ToLowerInvariant()} du club : celui du fabricant";
            PerformLayout();
            Invalidate();
        }

        public int Hauteur(int largeur) => Placer(largeur, null, positionner: false);

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            Placer(Width, null, positionner: true);
        }

        protected override void Peindre(Graphics g)
        {
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var forme = Dessin.Arrondi(r, E(16)))
            using (var fond = new SolidBrush(Theme.FondAvertissement))
            using (var bord = new Pen(Theme.Sur(Theme.FondAvertissement, Theme.Avertissement, 0.35f), E(1)))
            {
                g.FillPath(fond, forme);
                g.DrawPath(bord, forme);
            }

            Placer(Width, g, positionner: false);
        }

        /// <summary>Calcule (et dessine si <paramref name="g"/> est donné) le contenu ; retourne la hauteur totale.</summary>
        private int Placer(int largeur, Graphics? g, bool positionner)
        {
            if (_manuel is null)
            {
                return 0;
            }

            var marge = (int)E(14);
            var w = largeur - (2 * marge);
            var icone = (int)E(16);
            var retrait = icone + (int)E(8);
            var y = marge;

            var policeTitre = Theme.PoliceTitre(9.75f);
            var hTitre = Mesurer(TitreEncadre, policeTitre, w - retrait);
            if (g is not null)
            {
                Icones.Dessiner(g, Icones.Revisions, new RectangleF(marge, y + ((hTitre - icone) / 2f), icone, icone), Theme.Avertissement, 2f);
                Ecrire(g, TitreEncadre, policeTitre, new Rectangle(marge + retrait, y, w - retrait, hTitre), Theme.Avertissement);
            }

            y += hTitre + (int)E(10);

            var policeLigne = Theme.Police(9f);
            var policeValeur = Theme.PoliceTitre(9f);
            if (_lignes.Count == 0)
            {
                const string Aucun = "Aucun délai saisi. Ajoutez-les en modifiant le manuel.";
                var h = Mesurer(Aucun, policeLigne, w);
                if (g is not null)
                {
                    Ecrire(g, Aucun, policeLigne, new Rectangle(marge, y, w, h), Theme.TexteSecondaireClair);
                }

                y += h + (int)E(8);
            }

            foreach (var (libelle, valeur) in _lignes)
            {
                var wValeur = valeur.Length == 0 ? 0 : Math.Min(TextRenderer.MeasureText(valeur, policeValeur, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width + 2, (int)(w * 0.55f));
                var hValeur = valeur.Length == 0 ? 0 : Mesurer(valeur, policeValeur, wValeur);
                var wLibelle = w - wValeur - (wValeur > 0 ? (int)E(8) : 0);
                var hLibelle = Mesurer(libelle, policeLigne, wLibelle);
                if (g is not null)
                {
                    Ecrire(g, libelle, policeLigne, new Rectangle(marge, y, wLibelle, hLibelle), Theme.TexteDoux);
                    if (wValeur > 0)
                    {
                        Ecrire(g, valeur, policeValeur, new Rectangle(marge + w - wValeur, y, wValeur, hValeur), Theme.Avertissement, droite: true);
                    }
                }

                y += Math.Max(hLibelle, hValeur) + (int)E(8);
            }

            if (_lire.Visible)
            {
                var h = (int)E(Theme.HauteurCliquable);
                if (positionner)
                {
                    _lire.SetBounds(marge - (int)E(4), y, w, h);
                }

                y += h + (int)E(4);
            }

            if (_conformite is { } c)
            {
                y += (int)E(6);
                if (g is not null)
                {
                    using var filet = new Pen(Theme.Sur(Theme.FondAvertissement, Theme.Avertissement, 0.4f), E(1));
                    g.DrawLine(filet, marge, y, marge + w, y);
                }

                y += (int)E(12);
                var etat = c.Conforme ? "Réglage du club conforme." : "À vérifier.";
                var couleur = c.Conforme ? Theme.RetourClair : Theme.DepartClair;
                var policeEtat = Theme.PoliceTitre(9f);
                var hEtat = Mesurer(etat, policeEtat, w - retrait);
                var hMessage = Mesurer(c.Message, policeLigne, w - retrait);
                if (g is not null)
                {
                    Icones.Dessiner(g, c.Conforme ? Icones.Coche : Icones.Exclamation, new RectangleF(marge, y + E(1), icone, icone), couleur, 2.4f);
                    Ecrire(g, etat, policeEtat, new Rectangle(marge + retrait, y, w - retrait, hEtat), couleur);
                    Ecrire(g, c.Message, policeLigne, new Rectangle(marge + retrait, y + hEtat + (int)E(2), w - retrait, hMessage), Theme.TexteDoux);
                }

                y += hEtat + (int)E(2) + hMessage;
                if (Regler.Visible)
                {
                    y += (int)E(12);
                    var h = (int)E(Theme.HauteurCliquable);
                    if (positionner)
                    {
                        Regler.SetBounds(marge, y, w, h);
                    }

                    y += h;
                }
            }

            return y + marge;
        }

        private static int Mesurer(string texte, Font police, int largeur) =>
            TextRenderer.MeasureText(texte, police, new Size(Math.Max(1, largeur), int.MaxValue), Drapeaux).Height;

        private static void Ecrire(Graphics g, string texte, Font police, Rectangle zone, Color couleur, bool droite = false) =>
            TextRenderer.DrawText(g, texte, police, zone, couleur, Drapeaux | (droite ? TextFormatFlags.Right : TextFormatFlags.Left));
    }

    /// <summary>
    /// Page du PDF (image rendue par Windows) sur un fond sombre, centrée, avec une ombre ; défile si elle dépasse.
    /// La molette fait seulement défiler la page ; on change de page avec les boutons, le sommaire, Page préc. / suiv. et les flèches.
    /// </summary>
    private sealed class VuePage : Panel
    {
        private Bitmap? _image;
        private string? _message;

        public VuePage()
        {
            AutoScroll = true;
            BackColor = Theme.Sur(Theme.Surface, Theme.Fond, 0.6f);
            DoubleBuffered = true;
            Margin = Padding.Empty;
            TabStop = true;
            SetStyle(ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            AccessibleRole = AccessibleRole.Graphic;
            Natif.ScrollbarsDuTheme(this);
        }

        /// <summary>Changement de page demandé : +1 suivante, -1 précédente.</summary>
        public event EventHandler<int>? PageDemandee;

        private int Marge => (int)Dessin.Echelle(this, 24);

        /// <summary>Message à la place de la page (aucun manuel, fichier introuvable...).</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string? Message
        {
            get => _message;
            set
            {
                _image?.Dispose();
                _image = null;
                _message = value;
                AccessibleName = value;
                AutoScrollMinSize = Size.Empty;
                Invalidate();
            }
        }

        /// <summary>Largeur de la page en pixels : la largeur du lecteur (bornée) à 100 %, multipliée par le zoom.</summary>
        public int LargeurPage(int zoom)
        {
            var disponible = Width - SystemInformation.VerticalScrollBarWidth - (2 * Marge);
            var base100 = Math.Clamp(disponible, (int)Dessin.Echelle(this, 240), (int)Dessin.Echelle(this, 860));
            return base100 * zoom / 100;
        }

        /// <summary>Affiche une page. <paramref name="defiler"/> : 0 en haut, 1 en bas, null garde la position.</summary>
        public void Afficher(Bitmap image, int? defiler, string description)
        {
            var position = new Point(-AutoScrollPosition.X, -AutoScrollPosition.Y);
            _image?.Dispose();
            _image = image;
            _message = null;
            AccessibleName = description;
            AutoScrollMinSize = new Size(image.Width + (2 * Marge), image.Height + (2 * Marge));
            AutoScrollPosition = defiler switch
            {
                0 => Point.Empty,
                1 => new Point(position.X, AutoScrollMinSize.Height),
                _ => position,
            };
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _image?.Dispose();
                _image = null;
            }

            base.Dispose(disposing);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            base.OnMouseDown(e);
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.PageDown or Keys.Right:
                    PageDemandee?.Invoke(this, 1);
                    e.Handled = true;
                    break;
                case Keys.PageUp or Keys.Left:
                    PageDemandee?.Invoke(this, -1);
                    e.Handled = true;
                    break;
            }

            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            var marge = Marge;
            if (_image is { } image)
            {
                var x = Math.Max(marge, (ClientSize.Width - image.Width) / 2) + AutoScrollPosition.X;
                var y = marge + AutoScrollPosition.Y;
                for (var i = 6; i >= 1; i--)
                {
                    using var ombre = new SolidBrush(Theme.Alpha(Color.Black, 0.06f));
                    g.FillRectangle(ombre, x - i, y - i + 3, image.Width + (2 * i), image.Height + (2 * i));
                }

                using (var papier = new SolidBrush(Theme.PapierPdf))
                {
                    g.FillRectangle(papier, x, y, image.Width, image.Height);
                }

                g.DrawImageUnscaled(image, x, y);
            }
            else if (_message is { } message)
            {
                var zone = Rectangle.Inflate(ClientRectangle, -marge * 2, -marge * 2);
                TextRenderer.DrawText(g, message, Theme.Police(11f), zone, Theme.TexteSecondaireClair,
                    TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            if (Focused && ShowFocusCues)
            {
                using var stylo = new Pen(Theme.Accent, Dessin.Echelle(this, 1.5f));
                g.DrawRectangle(stylo, 1, 1, ClientSize.Width - 3, ClientSize.Height - 3);
            }
        }
    }
}
