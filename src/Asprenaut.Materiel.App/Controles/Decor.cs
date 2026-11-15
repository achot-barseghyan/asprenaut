using System.Drawing.Drawing2D;
using Asprenaut.Materiel.App.Apparence;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>
/// Bandeau d'accueil de l'écran Sorties : dégradé "eau profonde", rayons de lumière, fond marin,
/// coraux, poissons et bulles dessinés à droite. Les enfants (textes, boutons) se posent dans le Padding.
/// </summary>
public sealed class BandeauAccueil : Panel, IFondDessine
{
    // Décor de la maquette, dans un repère 1100 × 230 aligné en bas à droite.
    private const string FondMarin = "M560 230 C 640 190, 700 205, 760 178 S 880 150, 960 172 S 1060 160, 1100 150 V230 Z";
    private const string FondMarinProche = "M640 230 C 720 205, 800 214, 860 196 S 990 186, 1100 196 V230 Z";
    private const string CorailBleu = "M930 230 V176 M930 196 C 916 188, 912 176, 914 166 M930 186 C 944 180, 948 168, 946 158";
    private const string CorailOrange = "M1010 230 V160 M1010 186 C 994 176, 990 162, 994 150 M1010 172 C 1026 164, 1030 150, 1026 138";
    private const string CorailVert = "M790 230 V194 M790 208 C 780 202, 778 194, 780 186";
    private const string Poisson1 = "M860 92 q 18 -14 36 0 q -18 14 -36 0 z M896 92 l 10 -8 v16 z";
    private const string Poisson2 = "M930 122 q 12 -9 24 0 q -12 9 -24 0 z M954 122 l 7 -6 v12 z";
    private const string Poisson3 = "M806 128 q 10 -8 20 0 q -10 8 -20 0 z M826 128 l 6 -5 v10 z";

    public BandeauAccueil()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.BandeauMilieu;
        Padding = new Padding(40, 30, 40, 30);
        Margin = Padding.Empty;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        Dessin.EffacerAvecParent(this, g);
        Dessin.Qualite(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
        using var forme = Dessin.Arrondi(r, Dessin.Echelle(this, 28));

        // Dégradé à 160° : du bleu lagon en haut à gauche vers le bleu profond en bas à droite.
        using (var degrade = new LinearGradientBrush(new PointF(0, 0), new PointF(Width * 0.35f, Height + 1), Theme.BandeauHaut, Theme.BandeauBas))
        {
            degrade.InterpolationColors = new ColorBlend
            {
                Colors = [Theme.BandeauHaut, Theme.BandeauMilieu, Theme.BandeauBas],
                Positions = [0f, 0.45f, 1f],
            };
            g.FillPath(degrade, forme);
        }

        g.SetClip(forme);
        var etat = g.Save();
        var echelle = Math.Max(Width / 1100f, Height / 230f);
        g.TranslateTransform(Width - (1100 * echelle), Height - (230 * echelle));
        g.ScaleTransform(echelle, echelle);

        Remplir(g, [new(640, 0), new(700, 0), new(860, 230), new(760, 230)], Theme.Alpha(Theme.Lumiere, 0.06f));
        Remplir(g, [new(820, 0), new(900, 0), new(1060, 230), new(940, 230)], Theme.Alpha(Theme.Lumiere, 0.05f));
        Remplir(g, FondMarin, Theme.Alpha(Theme.FondMarin, 0.9f));
        Remplir(g, FondMarinProche, Theme.FondMarinProfond);
        Tracer(g, CorailBleu, Theme.Alpha(Theme.LogoCentre, 0.7f), 5);
        Tracer(g, CorailOrange, Theme.Alpha(Theme.Depart, 0.65f), 5);
        Tracer(g, CorailVert, Theme.Alpha(Theme.Retour, 0.6f), 4);
        Remplir(g, Poisson1, Theme.Alpha(Theme.AccentClair, 0.75f));
        Remplir(g, Poisson2, Theme.Alpha(Theme.AccentClair, 0.55f));
        Remplir(g, Poisson3, Theme.Alpha(Theme.AccentClair, 0.45f));
        using (var bulle = new Pen(Theme.Alpha(Theme.Lumiere, 0.45f), 1.5f))
        {
            g.DrawEllipse(bulle, 696, 66, 8, 8);
            g.DrawEllipse(bulle, 706, 40, 12, 12);
            g.DrawEllipse(bulle, 699, 17, 6, 6);
        }

        g.Restore(etat);
        g.ResetClip();
        using var bord = new Pen(Theme.Sur(Theme.BandeauMilieu, Theme.AccentClair, 0.18f), Dessin.Echelle(this, 1));
        g.DrawPath(bord, forme);
    }

    private static void Remplir(Graphics g, PointF[] points, Color couleur)
    {
        using var pinceau = new SolidBrush(couleur);
        g.FillPolygon(pinceau, points);
    }

    private static void Remplir(Graphics g, string chemin, Color couleur)
    {
        using var pinceau = new SolidBrush(couleur);
        g.FillPath(pinceau, Icones.Chemin(chemin));
    }

    private static void Tracer(Graphics g, string chemin, Color couleur, float epaisseur)
    {
        using var stylo = new Pen(couleur, epaisseur) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawPath(stylo, Icones.Chemin(chemin));
    }
}

/// <summary>Pastille ronde du logo (masque et tuba) en tête du menu.</summary>
public sealed class Logo : ControleDessine
{
    public Logo()
    {
        Size = new Size(46, 46);
        AccessibleName = "Asprenaut";
        AccessibleRole = AccessibleRole.Graphic;
    }

    protected override void Peindre(Graphics g)
    {
        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        using (var forme = new GraphicsPath())
        {
            forme.AddEllipse(r);
            using var degrade = new PathGradientBrush(forme)
            {
                CenterPoint = new PointF(Width * 0.3f, Height * 0.25f),
                CenterColor = Theme.LogoClair,
                SurroundColors = [Theme.LogoFonce],
            };
            degrade.InterpolationColors = new ColorBlend
            {
                Colors = [Theme.LogoFonce, Theme.LogoCentre, Theme.LogoClair],
                Positions = [0f, 0.55f, 1f],
            };
            g.FillPath(degrade, forme);
        }

        Icones.Dessiner(g, Icones.Logo, RectangleF.Inflate(r, -E(10), -E(10)), Theme.TexteSurLogo, 1.9f);
    }
}

/// <summary>
/// Logo du club dans une tuile blanche arrondie, entourée d'un halo clair (bandeau d'accueil de l'écran Sorties, aperçu de
/// Paramètres › Club). Sans image : tuile translucide bordée de tirets avec les initiales du club.
/// </summary>
public sealed class LogoClub : ControleDessine
{
    private Image? _image;
    private string _initiales = "?";

    public LogoClub()
    {
        Size = new Size(150, 150);
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = "Logo du club";
    }

    /// <summary>Image du logo (non disposée ici), ou null : les initiales sont dessinées à la place.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Image? Image { get => _image; set => Invalider(ref _image, value); }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Initiales { get => _initiales; set => Invalider(ref _initiales, value); }

    protected override void Peindre(Graphics g)
    {
        // Tuile de 150 : halo de 5 px, rayon 26, image dans une marge de 8 px ; proportions gardées pour les autres tailles.
        var k = Width / E(150);
        Peindre(g, new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), _image, _initiales, E(26) * k, E(5) * k, E(8) * k);
    }

    /// <summary>Dessine une tuile de logo dans <paramref name="r"/> (halo compris).</summary>
    public static void Peindre(Graphics g, RectangleF r, Image? image, string initiales, float rayon, float halo, float marge)
    {
        var tuile = RectangleF.Inflate(r, -halo, -halo);
        if (image is null)
        {
            using (var fond = new SolidBrush(Theme.Teinte(Theme.AccentClair, 0.12f)))
            using (var chemin = Dessin.Arrondi(tuile, rayon))
            using (var trait = new Pen(Theme.AccentClair, Math.Max(1.5f, halo / 3f)) { DashStyle = DashStyle.Dash })
            {
                g.FillPath(fond, chemin);
                g.DrawPath(trait, chemin);
            }

            var police = Theme.PoliceGrasse(Math.Max(8f, tuile.Height * 0.3f * 72f / g.DpiY));
            TextRenderer.DrawText(g, initiales, police, Rectangle.Round(tuile), Theme.AccentClair,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            return;
        }

        using (var chemin = Dessin.Arrondi(r, rayon + halo))
        using (var pinceau = new SolidBrush(Theme.HaloLogoClub))
        {
            g.FillPath(pinceau, chemin);
        }

        using (var chemin = Dessin.Arrondi(tuile, rayon))
        using (var fond = new SolidBrush(Theme.FondLogoClub))
        {
            g.FillPath(fond, chemin);
        }

        var zone = RectangleF.Inflate(tuile, -marge, -marge);
        var echelle = Math.Min(zone.Width / image.Width, zone.Height / image.Height);
        var l = image.Width * echelle;
        var h = image.Height * echelle;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(image, zone.X + ((zone.Width - l) / 2), zone.Y + ((zone.Height - h) / 2), l, h);
    }
}

/// <summary>
/// Aperçu du bandeau d'accueil dans Paramètres › Club (maquette « Paramètres · Club ») : dégradé du bandeau, tuile du logo,
/// « BANNIÈRE DE L'ACCUEIL », nom du club et responsable matériel. Suit la saisie.
/// </summary>
public sealed class ApercuBandeau : ControleDessine
{
    private Image? _image;
    private string _nom = "";
    private string _responsable = "";
    private string _initiales = "?";

    public ApercuBandeau()
    {
        Height = 112;
        AccessibleRole = AccessibleRole.Graphic;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Image? Image { get => _image; set => Invalider(ref _image, value); }

    /// <summary>Met à jour le texte (nom du club, ligne du responsable) et les initiales.</summary>
    public void Afficher(string nom, string responsable, string initiales)
    {
        _nom = nom;
        _responsable = responsable;
        _initiales = initiales;
        AccessibleName = $"Aperçu de la bannière de l'accueil : {nom}, {responsable}";
        Invalidate();
    }

    protected override void Peindre(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
        using (var forme = Dessin.Arrondi(r, E(20)))
        {
            using var degrade = new LinearGradientBrush(new PointF(0, 0), new PointF(Width * 0.35f, Height + 1), Theme.BandeauHaut, Theme.BandeauBas)
            {
                InterpolationColors = new ColorBlend
                {
                    Colors = [Theme.BandeauHaut, Theme.BandeauMilieu, Theme.BandeauBas],
                    Positions = [0f, 0.45f, 1f],
                },
            };
            g.FillPath(degrade, forme);
            using var bord = new Pen(Theme.Bordure);
            g.DrawPath(bord, forme);
        }

        var cote = E(76);
        var tuile = new RectangleF(E(20), (Height - cote) / 2, cote, cote);
        LogoClub.Peindre(g, tuile, _image, _initiales, E(16), E(4), E(5));

        var x = (int)(tuile.Right + E(18));
        var largeur = Math.Max(0, Width - x - (int)E(20));
        var drapeaux = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        var kicker = Theme.PoliceTitre(8.25f);
        var titre = Theme.PoliceGrasse(14.25f);
        var detail = Theme.Police(9.75f);
        var hk = TextRenderer.MeasureText("A", kicker, Size.Empty, drapeaux).Height;
        var ht = TextRenderer.MeasureText("A", titre, Size.Empty, drapeaux).Height;
        var hd = TextRenderer.MeasureText("A", detail, Size.Empty, drapeaux).Height;
        var y = (int)((Height - (hk + E(4) + ht + E(3) + hd)) / 2);
        TextRenderer.DrawText(g, "BANNIÈRE DE L'ACCUEIL", kicker, new Rectangle(x, y, largeur, hk), Theme.AccentClair, drapeaux);
        y += hk + (int)E(4);
        TextRenderer.DrawText(g, _nom, titre, new Rectangle(x, y, largeur, ht), Theme.Texte, drapeaux);
        y += ht + (int)E(3);
        TextRenderer.DrawText(g, _responsable, detail, new Rectangle(x, y, largeur, hd), Theme.TexteSecondaireClair, drapeaux);
    }
}

/// <summary>
/// Zone du logo de Paramètres › Club : le logo dans un cadre blanc, ou « Glissez votre logo ici » sur un cadre en tirets.
/// Un clic, Entrée ou Espace demandent un fichier (<see cref="Choisir"/>) ; une image glissée dessus le remplace (<see cref="Deposer"/>).
/// </summary>
public sealed class ZoneLogo : ControleDessine
{
    private Image? _image;
    private bool _survol;
    private bool _glisser;

    public ZoneLogo()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        AllowDrop = true;
        Cursor = Cursors.Hand;
        Size = new Size(260, 260);
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = "Logo du club : choisir une image";
    }

    public event EventHandler? Choisir;

    /// <summary>Fichier glissé sur la zone.</summary>
    public event EventHandler<string>? Deposer;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Image? Image { get => _image; set => Invalider(ref _image, value); }

    /// <summary>Carré : la hauteur suit la largeur.</summary>
    public override Size GetPreferredSize(Size proposedSize) => new(Width, Width);

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Height != Width)
        {
            Height = Width;
        }
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalider(ref _survol, true); }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalider(ref _survol, false); }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }

    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Focus();
        Choisir?.Invoke(this, EventArgs.Empty);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            e.Handled = true;
            Choisir?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string? FichierGlisse(DragEventArgs e) =>
        e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } fichiers ? fichiers[0] : null;

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        e.Effect = FichierGlisse(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        Invalider(ref _glisser, e.Effect != DragDropEffects.None);
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        Invalider(ref _glisser, false);
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        Invalider(ref _glisser, false);
        if (FichierGlisse(e) is { } fichier)
        {
            Deposer?.Invoke(this, fichier);
        }
    }

    protected override void Peindre(Graphics g)
    {
        var r = new RectangleF(1, 1, Width - 3, Height - 3);
        var actif = _glisser || _survol || Focused;
        var fond = _glisser ? Theme.Teinte(Theme.Accent, 0.10f) : ChampSaisie.FondChamp;
        var couleurBord = actif ? Theme.Accent : _image is null ? Theme.BordureForte : Theme.Bordure;
        using (var forme = Dessin.Arrondi(r, E(22)))
        using (var pinceau = new SolidBrush(fond))
        using (var trait = new Pen(couleurBord, E(1.5f)) { DashStyle = _image is null && !_glisser ? DashStyle.Dash : DashStyle.Solid })
        {
            g.FillPath(pinceau, forme);
            g.DrawPath(trait, forme);
        }

        if (_image is not null)
        {
            var cote = r.Width * 0.72f;
            var cadre = new RectangleF(r.X + ((r.Width - cote) / 2), r.Y + ((r.Height - cote) / 2), cote, cote);
            LogoClub.Peindre(g, RectangleF.Inflate(cadre, E(5), E(5)), _image, "", E(26), E(5), E(10));
            return;
        }

        // Pas de logo : pictogramme image, « Glissez votre logo ici », « ou cliquez pour choisir un fichier ».
        var picto = E(34);
        var titre = Theme.PoliceTitre(10.5f);
        var detail = Theme.Police(9f);
        var drapeaux = TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak;
        var largeur = (int)(r.Width - E(40));
        var ht = TextRenderer.MeasureText(g, "Glissez votre logo ici", titre, new Size(largeur, 0), drapeaux).Height;
        var hd = TextRenderer.MeasureText(g, "ou cliquez pour choisir un fichier", detail, new Size(largeur, 0), drapeaux).Height;
        var y = r.Y + ((r.Height - (picto + E(10) + ht + E(6) + hd)) / 2);
        Icones.Dessiner(g, Icones.Image, new RectangleF(r.X + ((r.Width - picto) / 2), y, picto, picto), Theme.AccentClair, 1.8f);
        y += picto + E(10);
        var x = (int)(r.X + E(20));
        TextRenderer.DrawText(g, "Glissez votre logo ici", titre, new Rectangle(x, (int)y, largeur, ht), Theme.Texte, drapeaux);
        y += ht + E(6);
        TextRenderer.DrawText(g, "ou cliquez pour choisir un fichier", detail, new Rectangle(x, (int)y, largeur, hd), Theme.TexteSecondaire, drapeaux);
    }
}
