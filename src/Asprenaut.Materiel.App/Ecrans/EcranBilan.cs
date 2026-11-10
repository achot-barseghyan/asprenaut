using System.ComponentModel;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Impression;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Bilan de saison, pour l'assemblée générale : grands chiffres, sorties par mois et par type, tension sur le stock,
/// matériel le plus et le moins utilisé, pertes et contrôles faits. Saison précédente ou en cours ; rapport PDF.
/// Les cartes sont dessinées (pas de contrôle par ligne) et placées par <see cref="PageBilan"/> selon la largeur.
/// </summary>
public sealed class EcranBilan : EcranBase
{
    private readonly ServiceBilan _service;
    private readonly ServiceReglages _reglages;
    private readonly ChoixSaison _saisons;
    private readonly PageBilan _page = new();
    private readonly CarteChiffre[] _chiffres;
    private readonly CarteMois _mois = new();
    private readonly CarteTypes _types = new();
    private readonly CarteTension _tension = new();
    private readonly CarteClassement _plusUtilises = new("Les plus utilisés", null, rang: true);
    private readonly CarteClassement _peuSortis = new("Peu ou pas sortis", $"Moins de {ServiceBilan.SeuilPeuSorti} sorties cette saison", rang: false);
    private readonly CartePertes _pertes = new();
    private Bilan? _bilan;

    public EcranBilan(ServiceBilan service, ServiceReglages reglages)
    {
        _service = service;
        _reglages = reglages;

        var saisons = _service.ListerSaisons();
        _saisons = new ChoixSaison(saisons.Select(s => (s.AnneeDebut, s.EnCours ? $"{s.Libelle} (en cours)" : s.Libelle)).ToList())
        {
            // La saison précédente d'abord : c'est elle que l'on présente en assemblée générale.
            Choisie = saisons[0].AnneeDebut,
        };
        _saisons.Changement += (_, _) => Executer(RechargerAsync);

        var rapport = new BoutonArrondi("Rapport PDF pour l'AG", StyleBouton.Principal, Icones.Document);
        rapport.Click += (_, _) => Executer(EnregistrerRapportAsync);

        Color[] couleurs = [Theme.Texte, Theme.Accent, Theme.Depart, Theme.Avertissement];
        _chiffres = couleurs.Select(c => new CarteChiffre(c)).ToArray();
        _page.Placer(_chiffres, _mois, _types, _tension, [_plusUtilises, _peuSortis, _pertes]);

        var defilement = new Panel { BackColor = Theme.Fond, Margin = Padding.Empty };
        defilement.HorizontalScroll.Maximum = 0;
        defilement.AutoScroll = false;
        defilement.VerticalScroll.Visible = false;
        defilement.AutoScroll = true;
        Natif.ScrollbarsDuTheme(defilement);
        // Largeur posée à la main (pas de Dock) : en Dock Top, la page gardait sa largeur d'avant l'apparition
        // de la barre de défilement et le bord droit des cartes passait dessous.
        defilement.Controls.Add(_page);
        void Ajuster()
        {
            // La barre apparaît après coup (quand la page grandit) : sa place est réservée dès que la page dépasse.
            var barre = _page.Height > defilement.ClientSize.Height && !defilement.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
            var largeur = defilement.ClientSize.Width - barre - (int)Dessin.Echelle(this, 6);
            if (largeur > 0 && _page.Width != largeur)
            {
                _page.Width = largeur;
            }
        }

        defilement.Layout += (_, _) => Ajuster();
        defilement.ClientSizeChanged += (_, _) => Ajuster();
        _page.SizeChanged += (_, _) => Ajuster();

        // Choix de la saison sous le titre (et non à côté du bouton) : à 1280 px, l'en-tête n'avait plus de place pour le titre.
        _saisons.Margin = new Padding(0, 0, 0, 20);
        var entete = Ui.Entete(Ui.Pile(Ui.Titre(Titre), Ui.SousTitre("Utilisation, pertes et révisions de la saison, pour l'assemblée générale.")), rapport);
        entete.Margin = new Padding(0, 0, 0, 14);
        Controls.Add(Ui.Colonne(
            (entete, SizeType.AutoSize, 0),
            (Ui.Rangee(true, _saisons), SizeType.AutoSize, 0),
            (defilement, SizeType.Percent, 100)));
    }

    public override string Titre => "Bilan de saison";

    public override EntreeMenu Menu => EntreeMenu.Bilan;

    protected override Task ChargerAsync(object? parametre) => RechargerAsync();

    private async Task RechargerAsync()
    {
        var bilan = await _service.ObtenirAsync(_saisons.Choisie);
        if (IsDisposed)
        {
            return;
        }

        _bilan = bilan;
        for (var i = 0; i < _chiffres.Length; i++)
        {
            _chiffres[i].Afficher(bilan.Chiffres[i]);
        }

        _mois.Afficher(bilan.Mois, bilan.NoteMois);
        _types.Afficher(bilan.Types);
        _tension.Afficher(bilan.Tension);
        _plusUtilises.Afficher(bilan.PlusUtilises.Select(u => new LigneClassement(u.Libelle, u.Code, Libelles.Pluriel(u.Sorties, "sortie"))).ToList(),
            "Aucun matériel sorti.", 0);
        _peuSortis.Afficher(bilan.PeuSortis.Take(CarteClassement.LignesMax).Select(u => new LigneClassement(u.Libelle, u.Code, Libelles.Pluriel(u.Sorties, "sortie"))).ToList(),
            "Tout le matériel est sorti au moins " + ServiceBilan.SeuilPeuSorti + " fois.", bilan.PeuSortis.Count - CarteClassement.LignesMax);
        _pertes.Afficher(bilan.Pertes, bilan.Revisions);
        _page.PerformLayout();
        _page.Invalidate(true);
        _page.Focus(); // La molette fait défiler la page tout de suite.
    }

    private async Task EnregistrerRapportAsync()
    {
        if (_bilan is not { } bilan)
        {
            return;
        }

        var reglages = _reglages.Actuels;
        await Fichiers.EnregistrerPdfAsync(this, "Enregistrer le rapport pour l'AG", ServiceBilan.NomFichierRapport(bilan.Saison),
            chemin => RapportBilan.Enregistrer(bilan, reglages.NomClub, reglages.ResponsableMateriel, chemin));
    }

    // ================================================================ Mise en page

    /// <summary>
    /// Page qui place les cartes selon la largeur (comme les grilles flexibles de la maquette) et prend la hauteur
    /// de son contenu : 4 chiffres (2 sur un écran étroit), mois + types côte à côte, tension, puis 3 cartes (2, ou 1).
    /// </summary>
    private sealed class PageBilan : Panel
    {
        private CarteChiffre[] _chiffres = [];
        private CarteBilan _mois = null!;
        private CarteBilan _types = null!;
        private CarteBilan _tension = null!;
        private CarteBilan[] _bas = [];

        public PageBilan()
        {
            BackColor = Theme.Fond;
            Margin = Padding.Empty;
            DoubleBuffered = true;

            // Focusable : la molette va au contrôle qui a le focus, puis remonte au cadre défilant. Sans cela, avec le
            // focus sur le choix de saison (en-tête), la molette ne faisait pas défiler la page.
            SetStyle(ControlStyles.Selectable, true);
            TabStop = false;
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            e.Control!.MouseDown += (_, _) => Focus();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            base.OnMouseDown(e);
        }

        public void Placer(CarteChiffre[] chiffres, CarteBilan mois, CarteBilan types, CarteBilan tension, CarteBilan[] bas)
        {
            (_chiffres, _mois, _types, _tension, _bas) = (chiffres, mois, types, tension, bas);
            Controls.AddRange([.. chiffres, mois, types, tension, .. bas]);
        }

        private int E(float v) => (int)Dessin.Echelle(this, v);

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (_chiffres.Length == 0)
            {
                return;
            }

            var largeur = ClientSize.Width;
            var ecart = E(18);
            var y = 0;

            // Grands chiffres : 4 colonnes, ou 2 sur un écran étroit.
            var colonnes = largeur >= E(780) ? 4 : 2;
            var ecartChiffres = E(14);
            var lc = (largeur - ((colonnes - 1) * ecartChiffres)) / colonnes;
            var hc = E(CarteChiffre.Hauteur);
            for (var i = 0; i < _chiffres.Length; i++)
            {
                _chiffres[i].Bounds = new Rectangle((i % colonnes) * (lc + ecartChiffres), y + ((i / colonnes) * (hc + ecartChiffres)), lc, hc);
            }

            y += (((_chiffres.Length + colonnes - 1) / colonnes) * (hc + ecartChiffres)) - ecartChiffres + E(22);

            // Sorties par mois (3/5) et par type (2/5), ou l'une sous l'autre.
            if (largeur >= E(860))
            {
                var lm = ((largeur - ecart) * 3) / 5;
                var h = Math.Max(_mois.HauteurPour(lm), _types.HauteurPour(largeur - ecart - lm));
                _mois.Bounds = new Rectangle(0, y, lm, h);
                _types.Bounds = new Rectangle(lm + ecart, y, largeur - ecart - lm, h);
                y += h + ecart;
            }
            else
            {
                y = Empiler(_mois, y, largeur, ecart);
                y = Empiler(_types, y, largeur, ecart);
            }

            y = Empiler(_tension, y, largeur, ecart);

            // Plus utilisés, peu sortis, pertes : 3 colonnes, 2 ou 1 selon la place ; même hauteur sur une rangée.
            var nb = largeur >= E(900) ? 3 : largeur >= E(560) ? 2 : 1;
            var lb = (largeur - ((nb - 1) * ecart)) / nb;
            for (var debut = 0; debut < _bas.Length; debut += nb)
            {
                var rangee = _bas.Skip(debut).Take(nb).ToList();
                // Seule sur sa rangée (troisième carte en 2 colonnes) : toute la largeur.
                var l = rangee.Count == 1 ? largeur : lb;
                var h = rangee.Max(c => c.HauteurPour(l));
                for (var i = 0; i < rangee.Count; i++)
                {
                    rangee[i].Bounds = new Rectangle(i * (lb + ecart), y, l, h);
                }

                y += h + ecart;
            }

            var hauteur = Math.Max(0, y - ecart + E(8));
            if (Height != hauteur)
            {
                Height = hauteur;
            }
        }

        private static int Empiler(CarteBilan carte, int y, int largeur, int ecart)
        {
            var h = carte.HauteurPour(largeur);
            carte.Bounds = new Rectangle(0, y, largeur, h);
            return y + h + ecart;
        }
    }

    // ================================================================ Choix de la saison

    /// <summary>Boutons « 2025-2026 » / « 2026-2027 (en cours) » dans un cadre arrondi ; un seul choisi.</summary>
    private sealed class ChoixSaison : FlowLayoutPanel
    {
        private readonly List<BoutonSaison> _boutons = [];

        public ChoixSaison(IReadOnlyList<(int Annee, string Libelle)> saisons)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            WrapContents = false;
            BackColor = Theme.SurfaceCreuse;
            Padding = new Padding(4);
            Margin = new Padding(0, 0, 0, 0);
            AccessibleRole = AccessibleRole.Grouping;
            AccessibleName = "Saison";
            foreach (var (annee, libelle) in saisons)
            {
                var bouton = new BoutonSaison(annee, libelle) { Margin = new Padding(_boutons.Count == 0 ? 0 : 4, 0, 0, 0) };
                bouton.Click += (_, _) =>
                {
                    if (Choisie != bouton.Annee)
                    {
                        Choisie = bouton.Annee;
                        Changement?.Invoke(this, EventArgs.Empty);
                    }
                };
                _boutons.Add(bouton);
                Controls.Add(bouton);
            }
        }

        public event EventHandler? Changement;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Choisie
        {
            get => _boutons.First(b => b.Selectionne).Annee;
            set
            {
                foreach (var b in _boutons)
                {
                    b.Selectionne = b.Annee == value;
                }
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) =>
            Dessin.PeindreFondArrondi(this, e.Graphics, BackColor, Theme.Bordure, 14);
    }

    private sealed class BoutonSaison : Button
    {
        private bool _selectionne;
        private bool _survol;

        public BoutonSaison(int annee, string libelle)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseMnemonic = false;
            Cursor = Cursors.Hand;
            Annee = annee;
            Text = libelle;
            Font = Theme.PoliceTitre(10.5f);
            Height = Theme.HauteurCliquable;
            Width = TextRenderer.MeasureText(libelle, Font).Width + 28;
        }

        public int Annee { get; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selectionne
        {
            get => _selectionne;
            set
            {
                _selectionne = value;
                AccessibleDescription = value ? "Saison affichée" : null;
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
            g.Clear(Dessin.CouleurParent(this));
            Dessin.Qualite(g);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            var fond = _selectionne ? Theme.Texte : _survol ? Theme.SurfaceActive : Color.Empty;
            if (!fond.IsEmpty)
            {
                using var chemin = Dessin.Arrondi(r, Dessin.Echelle(this, 10));
                using var pinceau = new SolidBrush(fond);
                g.FillPath(pinceau, chemin);
            }

            if (Focused && ShowFocusCues)
            {
                using var chemin = Dessin.Arrondi(RectangleF.Inflate(r, -2, -2), Dessin.Echelle(this, 9));
                using var focus = new Pen(Theme.Accent, Dessin.Echelle(this, 1.5f));
                g.DrawPath(focus, chemin);
            }

            Dessin.Texte(g, Text, Font, ClientRectangle, _selectionne ? Theme.Fond : Theme.TexteDoux, centre: true);
        }
    }

    // ================================================================ Cartes dessinées

    /// <summary>Un grand chiffre : libellé, valeur colorée, précision.</summary>
    private sealed class CarteChiffre : ControleDessine
    {
        public const int Hauteur = 124;
        private readonly Color _couleur;
        private ChiffreBilan _chiffre = new("", "", "");

        public CarteChiffre(Color couleur)
        {
            _couleur = couleur;
            AccessibleRole = AccessibleRole.StaticText;
        }

        public void Afficher(ChiffreBilan chiffre)
        {
            _chiffre = chiffre;
            AccessibleName = $"{chiffre.Libelle} : {chiffre.Valeur}. {chiffre.Precision}";
            Invalidate();
        }

        protected override void Peindre(Graphics g)
        {
            CarteBilan.Fond(this, g);
            var x = (int)E(22);
            var l = Width - (2 * x);
            Dessin.Texte(g, _chiffre.Libelle, Theme.PoliceTitre(9.75f), new Rectangle(x, (int)E(18), l, (int)E(22)), Theme.TexteSecondaireClair);
            Dessin.Texte(g, _chiffre.Valeur, Theme.PoliceGrasse(25f), new Rectangle(x, (int)E(42), l, (int)E(44)), _couleur);
            Dessin.Texte(g, _chiffre.Precision, Theme.Police(9f), new Rectangle(x, (int)E(88), l, (int)E(20)), Theme.TexteSecondaire);
        }
    }

    /// <summary>Carte à fond arrondi, titre (et précision à droite ou dessous), contenu dessiné par la sous-classe.</summary>
    private abstract class CarteBilan : ControleDessine
    {
        private readonly string _titre;
        private readonly bool _precisionDessous;

        protected CarteBilan(string titre, string? precision, bool precisionDessous)
        {
            _titre = titre;
            Precision = precision;
            _precisionDessous = precisionDessous;
            AccessibleRole = AccessibleRole.Grouping;
            AccessibleName = titre;
        }

        protected string? Precision { get; set; }

        private float Marge => E(22);

        private float HauteurEntete => E(26) + (_precisionDessous && Precision is { Length: > 0 } ? E(20) : 0) + E(14);

        /// <summary>Hauteur totale pour cette largeur (pixels de l'écran).</summary>
        public int HauteurPour(int largeur) => (int)((2 * Marge) + HauteurEntete + HauteurContenu(largeur - (2 * Marge)));

        /// <summary>Hauteur du contenu pour cette largeur de contenu (pixels de l'écran).</summary>
        protected abstract float HauteurContenu(float largeur);

        protected abstract void PeindreContenu(Graphics g, Rectangle zone);

        /// <summary>Fond commun des cartes : surface, bordure fine, grands coins arrondis.</summary>
        public static void Fond(Control c, Graphics g)
        {
            var r = new RectangleF(0.5f, 0.5f, c.Width - 2, c.Height - 2);
            using var chemin = Dessin.Arrondi(r, Dessin.Echelle(c, Theme.RayonCarte));
            using var pinceau = new SolidBrush(Theme.Surface);
            using var bord = new Pen(Theme.Bordure, Dessin.Echelle(c, 1));
            g.FillPath(pinceau, chemin);
            g.DrawPath(bord, chemin);
        }

        protected override void Peindre(Graphics g)
        {
            Fond(this, g);
            var m = (int)Marge;
            var largeur = Width - (2 * m);
            var titre = Theme.PoliceTitre(12.75f);
            Dessin.Texte(g, _titre, titre, new Rectangle(m, m, largeur, (int)E(26)), Theme.Texte);
            if (Precision is { Length: > 0 } precision)
            {
                if (_precisionDessous)
                {
                    Dessin.Texte(g, precision, Theme.Police(9.75f), new Rectangle(m, m + (int)E(26), largeur, (int)E(20)), Theme.TexteSecondaire);
                }
                else
                {
                    var lt = TextRenderer.MeasureText(g, _titre, titre).Width;
                    TextRenderer.DrawText(g, precision, Theme.Police(9.75f), new Rectangle(m + lt + (int)E(10), m, largeur - lt - (int)E(10), (int)E(26)), Theme.TexteSecondaire,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
            }

            PeindreContenu(g, new Rectangle(m, m + (int)HauteurEntete, largeur, Height - (2 * m) - (int)HauteurEntete));
        }

        protected void Barre(Graphics g, RectangleF piste, double part, Color couleur)
        {
            using (var chemin = Dessin.Arrondi(piste, piste.Height / 2))
            using (var fond = new SolidBrush(Theme.Teinte(Theme.AccentClair, 0.10f)))
            {
                g.FillPath(fond, chemin);
            }

            if (part > 0)
            {
                var plein = new RectangleF(piste.X, piste.Y, Math.Max(piste.Height, (float)(piste.Width * Math.Min(1, part))), piste.Height);
                using var chemin = Dessin.Arrondi(plein, piste.Height / 2);
                using var pinceau = new SolidBrush(couleur);
                g.FillPath(pinceau, chemin);
            }
        }

        protected void Filet(Graphics g, int x, int y, int largeur)
        {
            using var filet = new Pen(Theme.Sur(Theme.Surface, Theme.AccentClair, 0.08f), E(1));
            g.DrawLine(filet, x, y, x + largeur, y);
        }

        protected static void Vide(Graphics g, string texte, Rectangle zone) =>
            Dessin.Texte(g, texte, Theme.Police(10.5f), new Rectangle(zone.X, zone.Y, zone.Width, Math.Min(zone.Height, 30)), Theme.TexteSecondaire);

        protected float HauteurVide => E(30);
    }

    /// <summary>Histogramme : une colonne par mois, la plus haute en clair.</summary>
    private sealed class CarteMois : CarteBilan
    {
        private IReadOnlyList<MoisBilan> _mois = [];

        public CarteMois()
            : base("Sorties par mois", null, precisionDessous: false)
        {
            AccessibleRole = AccessibleRole.Chart;
        }

        public void Afficher(IReadOnlyList<MoisBilan> mois, string note)
        {
            _mois = mois;
            Precision = note;
            AccessibleDescription = "Sorties par mois : " + string.Join(", ", mois.Select(m => $"{m.LibelleLong} {m.Sorties}"));
            Invalidate();
        }

        protected override float HauteurContenu(float largeur) => E(214);

        protected override void PeindreContenu(Graphics g, Rectangle zone)
        {
            if (_mois.Count == 0)
            {
                return;
            }

            var max = Math.Max(1, _mois.Max(m => m.Sorties));
            var ecart = E(10);
            var lc = (zone.Width - ((_mois.Count - 1) * ecart)) / _mois.Count;
            var hValeur = E(20);
            var hMois = E(22);
            var hMax = zone.Height - hValeur - hMois - E(10);
            var bas = zone.Bottom - hMois - E(4);
            for (var i = 0; i < _mois.Count; i++)
            {
                var m = _mois[i];
                var x = zone.X + (i * (lc + ecart));
                var lb = Math.Min(lc, E(44));
                var xb = x + ((lc - lb) / 2);
                var h = m.Sorties == 0 ? E(4) : Math.Max(E(6), (float)(hMax * m.Sorties / max));
                var couleur = m.Sorties == 0 ? Theme.Teinte(Theme.AccentClair, 0.10f)
                    : m.Sorties == max ? Theme.AccentClair
                    : Theme.Sur(Theme.Surface, Theme.Accent, 0.45f);
                using (var chemin = Dessin.Arrondi(new RectangleF(xb, bas - h, lb, h), Math.Min(E(8), lb / 2)))
                using (var pinceau = new SolidBrush(couleur))
                {
                    g.FillPath(pinceau, chemin);
                }

                var colonne = new Rectangle((int)x - 4, 0, (int)lc + 8, 0);
                if (m.Sorties > 0)
                {
                    Dessin.Texte(g, m.Sorties.ToString(Libelles.Culture), Theme.PoliceTitre(9f), colonne with { Y = (int)(bas - h - hValeur - E(2)), Height = (int)hValeur }, Theme.TexteDoux, centre: true);
                }

                Dessin.Texte(g, m.Libelle, Theme.Police(9f), colonne with { Y = (int)(bas + E(4)), Height = (int)hMois }, Theme.TexteSecondaire, centre: true);
            }
        }
    }

    /// <summary>Barres horizontales : nombre de sorties par type.</summary>
    private sealed class CarteTypes : CarteBilan
    {
        private IReadOnlyList<TypeBilan> _types = [];

        public CarteTypes()
            : base("Par type de sortie", null, precisionDessous: false)
        {
        }

        public void Afficher(IReadOnlyList<TypeBilan> types)
        {
            _types = types;
            AccessibleDescription = string.Join(", ", types.Select(t => $"{t.Libelle} {Libelles.Pluriel(t.Sorties, "sortie")}"));
            Invalidate();
        }

        private float HauteurLigne => E(48);

        protected override float HauteurContenu(float largeur) => _types.Count == 0 ? HauteurVide : (_types.Count * HauteurLigne) - E(14);

        protected override void PeindreContenu(Graphics g, Rectangle zone)
        {
            if (_types.Count == 0)
            {
                Vide(g, "Aucune sortie.", zone);
                return;
            }

            var max = Math.Max(1, _types.Max(t => t.Sorties));
            for (var i = 0; i < _types.Count; i++)
            {
                var t = _types[i];
                var y = zone.Y + (int)(i * HauteurLigne);
                var nombre = Libelles.Pluriel(t.Sorties, "sortie");
                var ln = TextRenderer.MeasureText(g, nombre, Theme.Police(10.5f)).Width;
                Dessin.Texte(g, t.Libelle, Theme.PoliceTitre(10.5f), new Rectangle(zone.X, y, zone.Width - ln - 8, (int)E(22)), Theme.Texte);
                TextRenderer.DrawText(g, nombre, Theme.Police(10.5f), new Rectangle(zone.Right - ln - 4, y, ln + 4, (int)E(22)), Theme.TexteSecondaireClair,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                Barre(g, new RectangleF(zone.X, y + E(26), zone.Width, E(10)), (double)t.Sorties / max, Theme.Accent);
            }
        }
    }

    /// <summary>Tableau « Tension sur le stock » : matériel, stock, pic d'occupation (barre), ruptures.</summary>
    private sealed class CarteTension : CarteBilan
    {
        private IReadOnlyList<TensionBilan> _lignes = [];

        public CarteTension()
            : base("Tension sur le stock", "Part du stock sortie lors des sorties les plus chargées. À 100 %, il n'en restait plus au local.", precisionDessous: true)
        {
            AccessibleRole = AccessibleRole.Table;
        }

        public void Afficher(IReadOnlyList<TensionBilan> lignes)
        {
            _lignes = lignes;
            AccessibleDescription = string.Join(". ", lignes.Select(l =>
                $"{l.Libelle} : stock {l.Stock}, pic {l.Pourcentage} %, {(l.Ruptures == 0 ? "aucune rupture" : $"rupture {l.Ruptures} fois")}"));
            Invalidate();
        }

        private float HauteurLigne => E(46);

        protected override float HauteurContenu(float largeur) => _lignes.Count == 0 ? HauteurVide : E(34) + (_lignes.Count * HauteurLigne);

        /// <summary>Couleur du pic : rouge à 100 %, jaune dès 90 %, grisé sous 30 %.</summary>
        private static Color CouleurPic(int pourcentage) => pourcentage >= 100 ? Theme.Erreur
            : pourcentage >= 90 ? Theme.Avertissement
            : pourcentage <= 30 ? Theme.TexteSecondaire
            : Theme.Accent;

        protected override void PeindreContenu(Graphics g, Rectangle zone)
        {
            if (_lignes.Count == 0)
            {
                Vide(g, "Aucun matériel sorti.", zone);
                return;
            }

            // Colonnes : matériel, stock, pic d'occupation (la plus large), rupture.
            var x0 = zone.X + (int)E(8);
            var l = zone.Width - (int)E(16);
            int[] debut = [x0, x0 + (int)(l * 0.36), x0 + (int)(l * 0.48), x0 + (int)(l * 0.86)];
            var fin = x0 + l;
            string[] entetes = ["MATÉRIEL", "STOCK", "PIC D'OCCUPATION", "RUPTURE"];
            var hEntete = (int)E(34);
            for (var i = 0; i < entetes.Length; i++)
            {
                var droite = i + 1 < debut.Length ? debut[i + 1] : fin;
                Dessin.Texte(g, entetes[i], Theme.PoliceTitre(8.25f), new Rectangle(debut[i], zone.Y, droite - debut[i] - 8, hEntete), Theme.TexteSecondaire);
            }

            for (var i = 0; i < _lignes.Count; i++)
            {
                var t = _lignes[i];
                var y = zone.Y + hEntete + (int)(i * HauteurLigne);
                var h = (int)HauteurLigne;
                Filet(g, zone.X, y, zone.Width);
                Dessin.Texte(g, t.Libelle, Theme.PoliceTitre(10.5f), new Rectangle(debut[0], y, debut[1] - debut[0] - 8, h), Theme.Texte);
                Dessin.Texte(g, t.Stock.ToString(Libelles.Culture), Theme.Police(10.5f), new Rectangle(debut[1], y, debut[2] - debut[1] - 8, h), Theme.TexteDoux);

                var couleur = CouleurPic(t.Pourcentage);
                var lPct = (int)E(52);
                var lBarre = debut[3] - debut[2] - lPct - (int)E(24);
                Barre(g, new RectangleF(debut[2], y + ((h - E(8)) / 2), Math.Max(E(20), lBarre), E(8)), t.Pourcentage / 100.0, couleur);
                TextRenderer.DrawText(g, $"{t.Pourcentage} %", Theme.PoliceGrasse(9.75f), new Rectangle(debut[2] + lBarre + (int)E(6), y, lPct, h), couleur,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

                var rupture = t.Ruptures == 0 ? "—" : $"{t.Ruptures} fois";
                Dessin.Texte(g, rupture, t.Ruptures == 0 ? Theme.Police(10.5f) : Theme.PoliceGrasse(10.5f), new Rectangle(debut[3], y, fin - debut[3], h),
                    t.Ruptures == 0 ? Theme.TexteSecondaire : Theme.Erreur);
            }
        }
    }

    private sealed record LigneClassement(string Nom, string Code, string Valeur);

    /// <summary>Liste d'équipements : rang (optionnel), nom et code, nombre de sorties à droite.</summary>
    private sealed class CarteClassement : CarteBilan
    {
        public const int LignesMax = 5;
        private readonly bool _rang;
        private IReadOnlyList<LigneClassement> _lignes = [];
        private string _vide = "";
        private int _autres;

        public CarteClassement(string titre, string? precision, bool rang)
            : base(titre, precision, precisionDessous: true)
        {
            _rang = rang;
            AccessibleRole = AccessibleRole.List;
        }

        public void Afficher(IReadOnlyList<LigneClassement> lignes, string vide, int autres)
        {
            _lignes = lignes;
            _vide = vide;
            _autres = Math.Max(0, autres);
            AccessibleDescription = lignes.Count == 0 ? vide : string.Join(". ", lignes.Select(l => $"{l.Nom} {l.Code} : {l.Valeur}"));
            Invalidate();
        }

        private float HauteurLigne => E(54);

        protected override float HauteurContenu(float largeur) =>
            _lignes.Count == 0 ? HauteurVide : (_lignes.Count * HauteurLigne) + (_autres > 0 ? E(30) : 0);

        protected override void PeindreContenu(Graphics g, Rectangle zone)
        {
            if (_lignes.Count == 0)
            {
                Vide(g, _vide, zone);
                return;
            }

            for (var i = 0; i < _lignes.Count; i++)
            {
                var ligne = _lignes[i];
                var y = zone.Y + (int)(i * HauteurLigne);
                var h = (int)HauteurLigne;
                Filet(g, zone.X, y, zone.Width);
                var x = zone.X;
                if (_rang)
                {
                    var d = E(26);
                    var cercle = new RectangleF(x, y + ((h - d) / 2), d, d);
                    using (var pinceau = new SolidBrush(Theme.Teinte(Theme.Accent, 0.14f)))
                    {
                        g.FillEllipse(pinceau, cercle);
                    }

                    Dessin.Texte(g, (i + 1).ToString(Libelles.Culture), Theme.PoliceGrasse(9f), Rectangle.Round(cercle), Theme.AccentClair, centre: true);
                    x += (int)(d + E(12));
                }

                // À droite : nombre en gras (classement) ou dans une pastille (peu sortis).
                var police = Theme.PoliceTitre(9.75f);
                var lv = TextRenderer.MeasureText(g, ligne.Valeur, police).Width;
                var droite = zone.Right;
                if (_rang)
                {
                    TextRenderer.DrawText(g, ligne.Valeur, police, new Rectangle(droite - lv, y, lv, h), Theme.TexteDoux,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                }
                else
                {
                    var pastille = new RectangleF(droite - lv - E(20), y + ((h - E(26)) / 2), lv + E(20), E(26));
                    using (var chemin = Dessin.Arrondi(pastille, pastille.Height / 2))
                    using (var pinceau = new SolidBrush(Theme.Teinte(Theme.AccentClair, 0.10f)))
                    {
                        g.FillPath(pinceau, chemin);
                    }

                    Dessin.Texte(g, ligne.Valeur, police, Rectangle.Round(pastille), Theme.TexteDoux, centre: true);
                    lv += (int)E(20);
                }

                var lt = droite - lv - x - (int)E(10);
                Dessin.Texte(g, ligne.Nom, Theme.PoliceTitre(10.5f), new Rectangle(x, y + (int)E(7), lt, (int)E(22)), Theme.Texte);
                Dessin.Texte(g, ligne.Code, Theme.PoliceCode(9f), new Rectangle(x, y + (int)E(28), lt, (int)E(18)), Theme.Accent);
            }

            if (_autres > 0)
            {
                var y = zone.Y + (int)(_lignes.Count * HauteurLigne);
                Filet(g, zone.X, y, zone.Width);
                Dessin.Texte(g, $"et {Libelles.Pluriel(_autres, "autre")}", Theme.Police(9.75f), new Rectangle(zone.X, y + (int)E(6), zone.Width, (int)E(22)), Theme.TexteSecondaire);
            }
        }
    }

    /// <summary>Manquants de la saison (perdu / retrouvé) puis contrôles faits par famille.</summary>
    private sealed class CartePertes : CarteBilan
    {
        private const int PertesMax = 4;
        private IReadOnlyList<PerteBilan> _pertes = [];
        private IReadOnlyList<RevisionsBilan> _revisions = [];

        public CartePertes()
            : base("Pertes et révisions", null, precisionDessous: true)
        {
            AccessibleRole = AccessibleRole.List;
        }

        public void Afficher(IReadOnlyList<PerteBilan> pertes, IReadOnlyList<RevisionsBilan> revisions)
        {
            _pertes = pertes;
            _revisions = revisions;
            AccessibleDescription = (pertes.Count == 0 ? "Aucun manquant." : string.Join(". ", pertes.Select(p => $"{Nom(p)} : {(p.Retrouve ? "retrouvé" : "perdu")}")))
                + " Contrôles : " + (revisions.Count == 0 ? "aucun." : string.Join(", ", revisions.Select(r => $"{r.Nombre} {r.Libelle}")));
            Invalidate();
        }

        private static string Nom(PerteBilan p) => p.Libelle.Length > 0 ? $"{p.Libelle} · {p.Code}" : p.Code;

        private static string Detail(PerteBilan p)
        {
            var sortie = $"{p.Sortie} du {p.DateSortie.ToString("dd/MM", Libelles.Culture)}";
            return p.Retrouve && p.RetrouveLe is { } le
                ? $"{sortie} · retrouvé le {le.ToString("dd/MM", Libelles.Culture)}"
                : string.IsNullOrWhiteSpace(p.Moniteur) ? sortie : $"{sortie} · {p.Moniteur}";
        }

        private float HauteurLigne => E(54);

        private int NbPertes => Math.Min(PertesMax, _pertes.Count);

        private float HauteurPertes => _pertes.Count == 0 ? HauteurVide : (NbPertes * HauteurLigne) + (_pertes.Count > PertesMax ? E(30) : 0);

        protected override float HauteurContenu(float largeur) => HauteurPertes + E(14) + E(64);

        protected override void PeindreContenu(Graphics g, Rectangle zone)
        {
            if (_pertes.Count == 0)
            {
                Vide(g, "Aucun manquant cette saison.", zone);
            }

            var pill = Theme.PoliceGrasse(8.25f);
            for (var i = 0; i < NbPertes; i++)
            {
                var p = _pertes[i];
                var y = zone.Y + (int)(i * HauteurLigne);
                var h = (int)HauteurLigne;
                Filet(g, zone.X, y, zone.Width);
                var etat = p.Retrouve ? "Retrouvé" : "Perdu";
                var (fond, texte) = p.Retrouve ? (Theme.FondRetour, Theme.Retour) : (Theme.FondErreur, Theme.Erreur);
                var lp = TextRenderer.MeasureText(g, etat, pill).Width + E(20);
                var pastille = new RectangleF(zone.Right - lp, y + ((h - E(24)) / 2), lp, E(24));
                using (var chemin = Dessin.Arrondi(pastille, pastille.Height / 2))
                using (var pinceau = new SolidBrush(fond))
                {
                    g.FillPath(pinceau, chemin);
                }

                Dessin.Texte(g, etat, pill, Rectangle.Round(pastille), texte, centre: true);
                var lt = zone.Width - (int)lp - (int)E(10);
                Dessin.Texte(g, Nom(p), Theme.PoliceTitre(10.5f), new Rectangle(zone.X, y + (int)E(7), lt, (int)E(22)), Theme.Texte);
                Dessin.Texte(g, Detail(p), Theme.Police(9f), new Rectangle(zone.X, y + (int)E(28), lt, (int)E(18)), Theme.TexteSecondaire);
            }

            if (_pertes.Count > PertesMax)
            {
                var y = zone.Y + (int)(NbPertes * HauteurLigne);
                Filet(g, zone.X, y, zone.Width);
                Dessin.Texte(g, $"et {Libelles.Pluriel(_pertes.Count - PertesMax, "autre")}", Theme.Police(9.75f), new Rectangle(zone.X, y + (int)E(6), zone.Width, (int)E(22)), Theme.TexteSecondaire);
            }

            // Contrôles faits : trois familles au plus (les plus nombreuses).
            var haut = zone.Y + HauteurPertes + E(14);
            if (_revisions.Count == 0)
            {
                Dessin.Texte(g, "Aucun contrôle enregistré cette saison.", Theme.Police(9.75f), new Rectangle(zone.X, (int)haut, zone.Width, (int)E(30)), Theme.TexteSecondaire);
                return;
            }

            var ecart = E(8);
            var lc = (zone.Width - (2 * ecart)) / 3;
            for (var i = 0; i < Math.Min(3, _revisions.Count); i++)
            {
                var r = _revisions[i];
                var cadre = new RectangleF(zone.X + (i * (lc + ecart)), haut, lc, E(64));
                using (var chemin = Dessin.Arrondi(cadre, E(12)))
                using (var pinceau = new SolidBrush(Theme.FondAvertissement))
                {
                    g.FillPath(pinceau, chemin);
                }

                var x = (int)(cadre.X + E(12));
                var l = (int)(cadre.Width - E(16));
                Dessin.Texte(g, r.Nombre.ToString(Libelles.Culture), Theme.PoliceGrasse(13.5f), new Rectangle(x, (int)(cadre.Y + E(8)), l, (int)E(28)), Theme.Avertissement);
                Dessin.Texte(g, r.Libelle, Theme.Police(8.25f), new Rectangle(x, (int)(cadre.Y + E(38)), l, (int)E(18)), Theme.TexteDoux);
            }
        }
    }
}
