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

/// <summary>Carte du bas du menu : rappel que le pistolet est prêt (point vert qui pulse).</summary>
public sealed class CarteScanner : ControleDessine
{
    private readonly System.Windows.Forms.Timer _pulsation = new() { Interval = 60 };

    public CarteScanner()
    {
        Height = 66;
        AccessibleName = "Prêt à scanner";
        AccessibleDescription = "Le pistolet tape le code de l'étiquette puis Entrée.";
        _pulsation.Tick += (_, _) => Invalidate(new Rectangle(0, 0, (int)E(44), Height));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _pulsation.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pulsation.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void Peindre(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
        var fond = Theme.Sur(Theme.FondMenu, Theme.FondRetour, 0.75f);
        using (var forme = Dessin.Arrondi(r, E(16)))
        using (var pinceau = new SolidBrush(fond))
        using (var bord = new Pen(Theme.Sur(fond, Theme.Retour, 0.22f), E(1)))
        {
            g.FillPath(pinceau, forme);
            g.DrawPath(bord, forme);
        }

        // Pulsation : un halo qui s'élargit et s'efface toutes les 2,4 s.
        var phase = (Environment.TickCount64 % 2400) / 2400f;
        var halo = (float)Math.Sin(phase * Math.PI);
        var d = E(10);
        var cx = E(16) + (d / 2);
        var cy = Height / 2f;
        var rayonHalo = (d / 2) + (E(6) * halo);
        using (var lueur = new SolidBrush(Theme.Alpha(Theme.Retour, 0.55f * (1 - halo))))
        {
            g.FillEllipse(lueur, cx - rayonHalo, cy - rayonHalo, rayonHalo * 2, rayonHalo * 2);
        }

        using (var point = new SolidBrush(Theme.Retour))
        {
            g.FillEllipse(point, cx - (d / 2), cy - (d / 2), d, d);
        }

        var gauche = (int)(cx + E(17));
        var titre = Theme.PoliceTitre(9.75f);
        var sous = Theme.Police(9f);
        var hTitre = TextRenderer.MeasureText(g, "Ag", titre, Size.Empty, TextFormatFlags.NoPadding).Height;
        var hSous = TextRenderer.MeasureText(g, "Ag", sous, Size.Empty, TextFormatFlags.NoPadding).Height;
        var y = (int)((Height - hTitre - hSous - E(2)) / 2);
        Dessin.Texte(g, "Prêt à scanner", titre, new Rectangle(gauche, y, Width - gauche - 8, hTitre), Theme.Texte);
        Dessin.Texte(g, "Visez l'étiquette QR", sous, new Rectangle(gauche, y + hTitre + (int)E(2), Width - gauche - 8, hSous), Theme.RetourClair);
    }
}

/// <summary>
/// Logo du club dans une tuile blanche arrondie, entourée d'un halo clair (bandeau d'accueil de l'écran Sorties).
/// L'image (Ressources/LogoClub.png, ressource LogoClub.png) est chargée une fois et gardée.
/// </summary>
public sealed class LogoClub : ControleDessine
{
    private static Image? _image;

    public LogoClub()
    {
        Size = new Size(150, 150);
        AccessibleName = "Logo Asprenaut, Chalon-sur-Saône";
        AccessibleRole = AccessibleRole.Graphic;
    }

    private static Image Image
    {
        get
        {
            if (_image is null)
            {
                using var flux = typeof(LogoClub).Assembly.GetManifestResourceStream("LogoClub.png")
                    ?? throw new InvalidOperationException("Logo du club introuvable (ressource LogoClub.png).");
                using var lu = Image.FromStream(flux);
                _image = new Bitmap(lu); // Copie : l'image reste valable après la fermeture du flux.
            }

            return _image;
        }
    }

    protected override void Peindre(Graphics g)
    {
        // Halo de 5 px, puis tuile blanche (rayon 26) et l'image dans une marge de 8 px.
        var halo = E(5);
        var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
        using (var chemin = Dessin.Arrondi(r, E(26) + halo))
        using (var pinceau = new SolidBrush(Theme.HaloLogoClub))
        {
            g.FillPath(pinceau, chemin);
        }

        var tuile = RectangleF.Inflate(r, -halo, -halo);
        using (var chemin = Dessin.Arrondi(tuile, E(26)))
        using (var fond = new SolidBrush(Theme.FondLogoClub))
        {
            g.FillPath(fond, chemin);
        }

        var zone = RectangleF.Inflate(tuile, -E(8), -E(8));
        var image = Image;
        var echelle = Math.Min(zone.Width / image.Width, zone.Height / image.Height);
        var l = image.Width * echelle;
        var h = image.Height * echelle;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(image, zone.X + ((zone.Width - l) / 2), zone.Y + ((zone.Height - h) / 2), l, h);
    }
}
