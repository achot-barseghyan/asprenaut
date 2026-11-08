using System.ComponentModel;
using Asprenaut.Materiel.App.Apparence;

namespace Asprenaut.Materiel.App.Controles;

public enum StyleBouton
{
    /// <summary>Action principale (turquoise).</summary>
    Principal,

    /// <summary>Action secondaire (surface + bordure).</summary>
    Secondaire,

    /// <summary>Action liée au départ (orange).</summary>
    Depart,

    /// <summary>Action liée au retour (vert).</summary>
    Retour,

    /// <summary>Lien textuel sans fond.</summary>
    Lien,

    /// <summary>Entrée du menu latéral.</summary>
    Menu,
}

/// <summary>Bouton dessiné à coins arrondis, au moins 44 px de haut. Garde le clavier et l'accessibilité de Button.</summary>
public class BoutonArrondi : Button
{
    private bool _survol;
    private bool _appui;
    private StyleBouton _style;
    private bool _selectionne;

    public BoutonArrondi()
        : this("", StyleBouton.Principal)
    {
    }

    public BoutonArrondi(string texte, StyleBouton style = StyleBouton.Principal)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        UseMnemonic = false;
        Text = texte;
        _style = style;
        Font = style == StyleBouton.Menu ? Theme.Police(11f) : Theme.PoliceTitre(10.5f);
        Cursor = Cursors.Hand;
        AutoSize = style != StyleBouton.Menu;
        AutoSizeMode = AutoSizeMode.GrowOnly;
        Height = Theme.HauteurBouton;
        MinimumSize = new Size(0, Theme.HauteurCliquable);
        Padding = new Padding(20, 0, 20, 0);
        Margin = new Padding(12, 0, 0, 0);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public StyleBouton Style
    {
        get => _style;
        set
        {
            _style = value;
            Invalidate();
        }
    }

    /// <summary>Entrée de menu sélectionnée (écran courant).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selectionne
    {
        get => _selectionne;
        set
        {
            _selectionne = value;
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
        _appui = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _appui = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _appui = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        var (fond, texte, bordure) = Couleurs();
        Dessin.PeindreFondArrondi(this, g, fond ?? Dessin.CouleurParent(this), bordure, Theme.RayonBouton, 1.5f);

        if (_style == StyleBouton.Menu && _selectionne)
        {
            // Barre d'accent à gauche de l'entrée sélectionnée.
            var largeur = Dessin.Echelle(this, 4);
            var marge = Dessin.Echelle(this, 10);
            using var chemin = Dessin.Arrondi(new RectangleF(0, marge, largeur, Height - (2 * marge)), largeur / 2);
            using var pinceau = new SolidBrush(Theme.Accent);
            g.FillPath(pinceau, chemin);
        }

        if (Focused && ShowFocusCues)
        {
            var retrait = Dessin.Echelle(this, 3);
            using var chemin = Dessin.Arrondi(new RectangleF(retrait, retrait, Width - (2 * retrait) - 1, Height - (2 * retrait) - 1), Dessin.Echelle(this, Theme.RayonBouton - 3));
            using var stylo = new Pen(_style == StyleBouton.Principal ? Theme.TexteSurAccent : Theme.Accent, Dessin.Echelle(this, 1.5f));
            g.DrawPath(stylo, chemin);
        }

        var zone = ClientRectangle;
        var drapeaux = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        if (_style == StyleBouton.Menu)
        {
            zone = Rectangle.FromLTRB((int)Dessin.Echelle(this, 20), 0, Width, Height);
            drapeaux |= TextFormatFlags.Left;
        }
        else
        {
            drapeaux |= TextFormatFlags.HorizontalCenter;
        }

        TextRenderer.DrawText(g, Text, Font, zone, texte, drapeaux);
    }

    private (Color? Fond, Color Texte, Color? Bordure) Couleurs()
    {
        if (!Enabled)
        {
            return (Theme.Surface, Theme.TexteSecondaire, Theme.Bordure);
        }

        Color Etat(Color c) => _appui ? Theme.Assombrir(c) : _survol ? Theme.Eclaircir(c) : c;

        return _style switch
        {
            StyleBouton.Principal => (Etat(Theme.Accent), Theme.TexteSurAccent, null),
            StyleBouton.Depart => (Etat(Theme.Depart), Theme.FondDepart, null),
            StyleBouton.Retour => (Etat(Theme.Retour), Theme.FondRetour, null),
            StyleBouton.Secondaire => (Etat(Theme.SurfaceActive), Theme.Texte, Theme.BordureForte),
            StyleBouton.Lien => (_survol ? Theme.SurfaceActive : null, Theme.Accent, null),
            _ => (_selectionne ? Theme.SurfaceActive : _survol ? Theme.Bordure : null,
                  _selectionne ? Theme.Texte : Theme.TexteSecondaireClair,
                  null),
        };
    }
}
