using System.ComponentModel;
using System.Drawing.Drawing2D;
using Asprenaut.Materiel.App.Apparence;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>Panneau arrondi (surface + bordure). Les enfants se placent dans le Padding.</summary>
public class Carte : Panel
{
    private Color _bordure = Theme.Bordure;

    public Carte()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        Padding = new Padding(22);
        Margin = Padding.Empty;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color CouleurBordure
    {
        get => _bordure;
        set
        {
            _bordure = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Rayon { get; set; } = Theme.RayonCarte;

    protected override void OnPaintBackground(PaintEventArgs e) =>
        Dessin.PeindreFondArrondi(this, e.Graphics, BackColor, _bordure, Rayon);
}

/// <summary>Contrôle dessiné à fond transparent (efface avec le fond de son parent).</summary>
public abstract class ControleDessine : Control
{
    protected ControleDessine()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Margin = Padding.Empty;
    }

    protected float E(float pixelsLogiques) => Dessin.Echelle(this, pixelsLogiques);

    protected override void OnPaint(PaintEventArgs e)
    {
        Dessin.EffacerAvecParent(this, e.Graphics);
        Dessin.Qualite(e.Graphics);
        Peindre(e.Graphics);
    }

    protected abstract void Peindre(Graphics g);

    protected void Invalider<T>(ref T champ, T valeur)
    {
        champ = valeur;
        Invalidate();
    }
}

/// <summary>Anneau de progression "4/12 rendus".</summary>
public sealed class AnneauProgression : ControleDessine
{
    private double _valeur;
    private Color _couleur = Theme.Retour;
    private string _nombre = "0";
    private string _total = "";
    private string _legende = "rendus";
    private float _epaisseur = 8;

    public AnneauProgression(int taille = 84)
    {
        Size = new Size(taille, taille);
        _epaisseur = taille >= 100 ? 10 : 8;
    }

    /// <summary>Part remplie, entre 0 et 1.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Valeur { get => _valeur; set => Invalider(ref _valeur, Math.Clamp(value, 0, 1)); }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Couleur { get => _couleur; set => Invalider(ref _couleur, value); }

    /// <summary>Gros chiffre au centre.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Nombre { get => _nombre; set => Invalider(ref _nombre, value); }

    /// <summary>Total affiché en petit après le chiffre ("/12"), vide pour aucun.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Total { get => _total; set => Invalider(ref _total, value); }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Legende { get => _legende; set => Invalider(ref _legende, value); }

    protected override void Peindre(Graphics g)
    {
        var cote = Math.Min(Width, Height);
        var ep = E(_epaisseur);
        var r = new RectangleF(((Width - cote) / 2f) + (ep / 2), ((Height - cote) / 2f) + (ep / 2), cote - ep - 1, cote - ep - 1);
        using (var piste = new Pen(Theme.Teinte(Theme.AccentClair, 0.12f), ep))
        {
            g.DrawEllipse(piste, r);
        }

        if (_valeur > 0)
        {
            using var stylo = new Pen(_couleur, ep) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(stylo, r, -90, (float)(360 * _valeur));
        }

        var grand = Theme.PoliceGrasse(cote >= 100 ? 18f : 13.5f);
        var petit = Theme.PoliceTitre(cote >= 100 ? 10.5f : 9f);
        var legende = Theme.PoliceTitre(7.5f);
        var drapeaux = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        var tGrand = TextRenderer.MeasureText(g, _nombre, grand, Size.Empty, drapeaux);
        var tPetit = _total.Length == 0 ? Size.Empty : TextRenderer.MeasureText(g, _total, petit, Size.Empty, drapeaux);
        var tLegende = TextRenderer.MeasureText(g, _legende.ToUpperInvariant(), legende, Size.Empty, drapeaux);
        var hauteur = tGrand.Height + E(2) + tLegende.Height;
        var y = (Height - hauteur) / 2f;
        var x = (Width - tGrand.Width - tPetit.Width) / 2f;
        TextRenderer.DrawText(g, _nombre, grand, new Point((int)x, (int)y), Theme.Texte, drapeaux);
        if (_total.Length > 0)
        {
            TextRenderer.DrawText(g, _total, petit, new Point((int)(x + tGrand.Width), (int)(y + tGrand.Height - tPetit.Height - E(1))), Theme.TexteSecondaire, drapeaux);
        }

        TextRenderer.DrawText(g, _legende.ToUpperInvariant(), legende, new Point((Width - tLegende.Width) / 2, (int)(y + tGrand.Height + E(2))), Theme.TexteSecondaire, drapeaux);
    }
}

/// <summary>Indicateur d'étapes "① Départ —— ② Retour".</summary>
public sealed class IndicateurEtapes : ControleDessine
{
    private int _etape = 1;

    public IndicateurEtapes()
    {
        Size = new Size(320, Theme.HauteurCliquable);
        AccessibleRole = AccessibleRole.ProgressBar;
    }

    /// <summary>1 = Départ, 2 = Retour.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int EtapeActive
    {
        get => _etape;
        set
        {
            Invalider(ref _etape, value);
            AccessibleName = value == 1 ? "Étape 1 sur 2 : départ" : "Étape 2 sur 2 : retour";
        }
    }

    protected override void Peindre(Graphics g)
    {
        var police = Theme.PoliceTitre(10.5f);
        var d = E(34);
        var cy = Height / 2f;
        var x = E(6);

        // Étape 1 : Départ (active en orange, ou terminée en vert).
        var departFini = _etape > 1;
        var finX = Etape(g, x, cy, d, "1", "Départ", departFini ? Theme.Retour : Theme.Depart, departFini ? Theme.RetourClair : Theme.DepartClair, active: !departFini, terminee: departFini, police);

        // Trait de liaison.
        var debutTrait = finX + E(14);
        var finTrait = debutTrait + E(56);
        using (var trait = new LinearGradientBrush(new PointF(debutTrait, 0), new PointF(finTrait + 1, 0),
            departFini ? Theme.Sur(Theme.Surface, Theme.Retour, 0.6f) : Theme.Depart,
            departFini ? Theme.Sur(Theme.Surface, Theme.Retour, 0.6f) : Theme.BordureForte))
        {
            g.FillRectangle(trait, debutTrait, cy - E(1), finTrait - debutTrait, E(2));
        }

        // Étape 2 : Retour.
        Etape(g, finTrait + E(14), cy, d, "2", "Retour", _etape == 2 ? Theme.Retour : Theme.BordureForte, _etape == 2 ? Theme.Retour : Theme.TexteSecondaire, active: _etape == 2, terminee: false, police);
    }

    private float Etape(Graphics g, float x, float cy, float d, string numero, string libelle, Color couleur, Color couleurTexte, bool active, bool terminee, Font police)
    {
        var cercle = new RectangleF(x, cy - (d / 2), d, d);
        if (active)
        {
            using var halo = new Pen(Theme.Sur(Theme.Surface, couleur, 0.18f), E(5));
            g.DrawEllipse(halo, RectangleF.Inflate(cercle, E(2.5f), E(2.5f)));
            using var plein = new SolidBrush(couleur);
            g.FillEllipse(plein, cercle);
            var texteSur = couleur == Theme.Depart ? Theme.TexteSurDepart : Theme.TexteSurRetour;
            Dessin.Texte(g, numero, Theme.PoliceGrasse(10.5f), Rectangle.Round(cercle), texteSur, centre: true);
        }
        else if (terminee)
        {
            using var fond = new SolidBrush(Theme.Sur(Theme.Surface, couleur, 0.16f));
            g.FillEllipse(fond, cercle);
            Icones.Dessiner(g, Icones.Coche, RectangleF.Inflate(cercle, -E(9), -E(9)), couleur, 2.8f);
        }
        else
        {
            using var bord = new Pen(couleur, E(1.5f));
            g.DrawEllipse(bord, cercle);
            Dessin.Texte(g, numero, Theme.PoliceGrasse(10.5f), Rectangle.Round(cercle), Theme.TexteSecondaire, centre: true);
        }

        var tx = x + d + E(8);
        var taille = TextRenderer.MeasureText(g, libelle, police);
        Dessin.Texte(g, libelle, active ? police : Theme.Police(10.5f), new Rectangle((int)tx, 0, taille.Width + 2, Height), couleurTexte);
        return tx + taille.Width;
    }
}

/// <summary>Pavé de date "06 / NOV." des cartes de sortie.</summary>
public sealed class PastilleDate : ControleDessine
{
    private DateOnly _date;

    public PastilleDate()
    {
        Size = new Size(62, 70);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DateOnly Date
    {
        get => _date;
        set
        {
            Invalider(ref _date, value);
            AccessibleName = Core.Modeles.Libelles.DateLongue(value);
        }
    }

    protected override void Peindre(Graphics g)
    {
        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        using (var chemin = Dessin.Arrondi(r, E(16)))
        using (var fond = new SolidBrush(Theme.FondAccent))
        using (var bord = new Pen(Theme.Teinte(Theme.Accent, 0.22f), E(1)))
        {
            g.FillPath(fond, chemin);
            g.DrawPath(bord, chemin);
        }

        var jour = _date.Day.ToString("00", Core.Modeles.Libelles.Culture);
        var mois = _date.ToString("MMM", Core.Modeles.Libelles.Culture).ToUpperInvariant();
        var milieu = Height / 2;
        Dessin.Texte(g, jour, Theme.PoliceGrasse(18.5f), new Rectangle(0, (int)(milieu - E(26)), Width, (int)E(32)), Theme.Texte, centre: true);
        Dessin.Texte(g, mois, Theme.PoliceTitre(9f), new Rectangle(0, (int)(milieu + E(6)), Width, (int)E(18)), Theme.AccentClair, centre: true);
    }
}

/// <summary>Pastille arrondie (type de sortie, compteur, statut), avec point coloré optionnel.</summary>
public sealed class Pastille : ControleDessine
{
    private Color _fond;
    private Color _texte;
    private Color? _point;
    private Font _police;
    private int _hauteur;
    private int _largeurMin;

    public Pastille(string texte, Color fond, Color couleurTexte, Color? point = null, Font? police = null, int hauteur = 28, int largeurMin = 0)
    {
        _fond = fond;
        _texte = couleurTexte;
        _point = point;
        _police = police ?? Theme.PoliceTitre(8.5f);
        _hauteur = hauteur;
        _largeurMin = largeurMin;
        AutoSize = true;
        Text = texte;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Fond { get => _fond; set => Invalider(ref _fond, value); }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color CouleurTexte { get => _texte; set => Invalider(ref _texte, value); }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color? Point { get => _point; set => Invalider(ref _point, value); }

    /// <summary>Bordure fine optionnelle.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color? Bordure { get; set; }

    /// <summary>Change le texte et les couleurs d'un coup.</summary>
    public void Definir(string texte, Color fond, Color couleurTexte, Color? point = null)
    {
        _fond = fond;
        _texte = couleurTexte;
        _point = point;
        Text = texte;
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var texte = TextRenderer.MeasureText(Text, _police, Size.Empty, TextFormatFlags.NoPadding).Width;
        var largeur = (int)(texte + E(26) + (_point is null ? 0 : E(15)));
        return new Size(Math.Max(largeur, (int)E(_largeurMin)), (int)E(_hauteur));
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        AccessibleName = Text;
        Parent?.PerformLayout(this, nameof(Text));
        Invalidate();
    }

    protected override void Peindre(Graphics g)
    {
        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        using (var chemin = Dessin.Arrondi(r, r.Height / 2))
        using (var fond = new SolidBrush(_fond))
        {
            g.FillPath(fond, chemin);
            if (Bordure is { } bordure)
            {
                using var stylo = new Pen(bordure, E(1));
                g.DrawPath(stylo, chemin);
            }
        }

        var x = (int)E(12);
        if (_point is { } point)
        {
            var d = E(8);
            using var pinceau = new SolidBrush(point);
            g.FillEllipse(pinceau, x, (Height - d) / 2f, d, d);
            x += (int)(d + E(7));
        }

        var largeurTexte = TextRenderer.MeasureText(Text, _police, Size.Empty, TextFormatFlags.NoPadding).Width;
        var gauche = _point is null ? Math.Max(x, (Width - largeurTexte) / 2) : x;
        Dessin.Texte(g, Text, _police, new Rectangle(gauche, 0, Width - gauche, Height), _texte);
    }
}

/// <summary>Pictogramme dans un carré arrondi teinté.</summary>
public sealed class Picto : ControleDessine
{
    private string _chemin;
    private Color _couleur;
    private Color? _fond;

    public Picto(string chemin, int taille = 42, Color? couleur = null, Color? fond = null)
    {
        _chemin = chemin;
        _couleur = couleur ?? Theme.AccentClair;
        _fond = fond ?? Theme.FondAccent;
        Size = new Size(taille, taille);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Chemin { get => _chemin; set => Invalider(ref _chemin, value); }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Couleur { get => _couleur; set => Invalider(ref _couleur, value); }

    protected override void Peindre(Graphics g)
    {
        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        if (_fond is { } fond)
        {
            using var chemin = Dessin.Arrondi(r, Width * 0.29f);
            using var pinceau = new SolidBrush(fond);
            g.FillPath(pinceau, chemin);
        }

        Icones.Dessiner(g, _chemin, RectangleF.Inflate(r, -Width * 0.24f, -Height * 0.24f), _couleur);
    }
}
