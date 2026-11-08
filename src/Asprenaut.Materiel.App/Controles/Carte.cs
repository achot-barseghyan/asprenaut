using System.ComponentModel;
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
        Padding = new Padding(20);
        Margin = new Padding(0, 0, 16, 16);
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

/// <summary>Barre de progression arrondie.</summary>
public sealed class BarreProgression : Control
{
    private double _valeur;
    private Color _couleur = Theme.Retour;

    public BarreProgression()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 10;
        Margin = new Padding(0, 6, 0, 6);
    }

    /// <summary>Valeur entre 0 et 1.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Valeur
    {
        get => _valeur;
        set
        {
            _valeur = Math.Clamp(value, 0, 1);
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Couleur
    {
        get => _couleur;
        set
        {
            _couleur = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Dessin.CouleurParent(this));
        Dessin.Qualite(g);
        var rayon = Height / 2f;
        using (var fond = Dessin.Arrondi(new RectangleF(0, 0, Width - 1, Height - 1), rayon))
        using (var pinceau = new SolidBrush(Theme.SurfaceActive))
        {
            g.FillPath(pinceau, fond);
        }

        if (_valeur > 0)
        {
            var largeur = Math.Max(Height, (float)((Width - 1) * _valeur));
            using var plein = Dessin.Arrondi(new RectangleF(0, 0, largeur, Height - 1), rayon);
            using var pinceau = new SolidBrush(_couleur);
            g.FillPath(pinceau, plein);
        }
    }
}

/// <summary>Indicateur d'étapes "1 Départ / 2 Retour".</summary>
public sealed class IndicateurEtapes : Control
{
    private int _etape = 1;

    public IndicateurEtapes()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(340, Theme.HauteurCliquable);
        Font = Theme.PoliceTitre(10.5f);
        Margin = Padding.Empty;
    }

    /// <summary>1 = Départ, 2 = Retour.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int EtapeActive
    {
        get => _etape;
        set
        {
            _etape = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Dessin.CouleurParent(this));
        Dessin.Qualite(g);
        var ecart = Dessin.Echelle(this, 8);
        var largeur = (Width - ecart - 1) / 2f;
        Dessiner(g, new RectangleF(0, 0, largeur, Height - 1), "1   Départ", 1, Theme.Depart, Theme.FondDepart);
        Dessiner(g, new RectangleF(largeur + ecart, 0, largeur, Height - 1), "2   Retour", 2, Theme.Retour, Theme.FondRetour);
    }

    private void Dessiner(Graphics g, RectangleF r, string texte, int numero, Color couleur, Color fond)
    {
        var active = numero == _etape;
        var terminee = numero < _etape;
        using var chemin = Dessin.Arrondi(r, r.Height / 2);
        if (active)
        {
            using var pinceau = new SolidBrush(fond);
            g.FillPath(pinceau, chemin);
        }

        using (var stylo = new Pen(active ? couleur : Theme.BordureForte, Dessin.Echelle(this, 1.5f)))
        {
            g.DrawPath(stylo, chemin);
        }

        var libelle = terminee ? $"✓   {texte[4..]}" : texte;
        TextRenderer.DrawText(g, libelle, Font, Rectangle.Round(r), active ? couleur : Theme.TexteSecondaire,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}
