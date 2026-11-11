using System.ComponentModel;
using System.Drawing.Drawing2D;
using Asprenaut.Materiel.App.Apparence;

namespace Asprenaut.Materiel.App.Controles;

public enum StyleBouton
{
    /// <summary>Action principale (turquoise).</summary>
    Principal,

    /// <summary>Action secondaire (fond creux + bordure).</summary>
    Secondaire,

    /// <summary>Action liée au départ (dégradé orange).</summary>
    Depart,

    /// <summary>Action liée au retour (vert).</summary>
    Retour,

    /// <summary>Lien textuel sans fond.</summary>
    Lien,

    /// <summary>Entrée du menu latéral.</summary>
    Menu,

    /// <summary>Bouton translucide posé sur le bandeau d'accueil.</summary>
    Verre,

    /// <summary>Petit bouton avec seulement un pictogramme (retirer une ligne...).</summary>
    Icone,

    /// <summary>Bouton jaune « Déverrouiller » du bandeau Mode consultation.</summary>
    Verrou,
}

/// <summary>Bouton dessiné à coins arrondis, au moins 44 px de haut. Garde le clavier et l'accessibilité de Button.</summary>
public class BoutonArrondi : Button, IVerrouillable
{
    private bool _survol;
    private bool _appui;
    private StyleBouton _style;
    private bool _selectionne;
    private string? _icone;
    private bool _iconeApres;

    public BoutonArrondi()
        : this("", StyleBouton.Principal)
    {
    }

    public BoutonArrondi(string texte, StyleBouton style = StyleBouton.Principal, string? icone = null, bool iconeApres = false)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        UseMnemonic = false;
        Text = texte;
        _style = style;
        _icone = icone;
        _iconeApres = iconeApres;
        Font = style switch
        {
            StyleBouton.Menu => Theme.Police(10.5f),
            StyleBouton.Lien => Theme.PoliceTitre(10.5f),
            _ => Theme.PoliceTitre(11f),
        };
        Cursor = Cursors.Hand;
        AutoSize = style is not (StyleBouton.Menu or StyleBouton.Icone);
        AutoSizeMode = AutoSizeMode.GrowOnly;
        Height = style == StyleBouton.Menu ? 46 : Theme.HauteurBouton;
        MinimumSize = new Size(0, Theme.HauteurCliquable);
        Padding = style == StyleBouton.Lien ? new Padding(4, 0, 4, 0) : new Padding(22, 0, 22, 0);
        Margin = new Padding(12, 0, 0, 0);
        if (style == StyleBouton.Icone)
        {
            Size = new Size(Theme.HauteurCliquable, Theme.HauteurCliquable);
            AccessibleName = texte;
        }
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

    /// <summary>Couleur du pictogramme d'un bouton <see cref="StyleBouton.Icone"/> (corbeille rouge...) ; null = couleur du thème.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color? CouleurIcone { get; set; }

    /// <summary>Pictogramme (chemin SVG de <see cref="Icones"/>) ; null = aucun.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Icone
    {
        get => _icone;
        set
        {
            _icone = value;
            Invalidate();
        }
    }

    /// <summary>Pictogramme après le texte (flèche "suivant") plutôt qu'avant.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IconeApres
    {
        get => _iconeApres;
        set
        {
            _iconeApres = value;
            Invalidate();
        }
    }

    private bool _verrouille;
    private PastilleVerrou? _pastille;

    /// <summary>
    /// Action protégée en mode consultation : pastille jaune au cadenas accrochée au coin haut droit et info-bulle
    /// « Code gestionnaire requis » (<see cref="PastilleVerrou"/>). Mise à jour par <see cref="GardeGestion.Lier{T}"/>.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Verrouille
    {
        get => _verrouille;
        set
        {
            if (_verrouille == value)
            {
                return;
            }

            _verrouille = value;
            AccessibleDescription = value ? "Demande le code gestionnaire" : null;
            if (AfficherPastille && (value || _pastille is not null))
            {
                _pastille ??= new PastilleVerrou(this);
                _pastille.Visible = value;
            }

            Invalidate();
        }
    }

    /// <summary>
    /// Faux sur une page qui a le bandeau « Mode consultation » (Révisions, Paramètres) : le bandeau suffit,
    /// le bouton garde seulement sa description accessible.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AfficherPastille { get; set; } = true;

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

    /// <summary>
    /// Largeur = texte + Padding (+ pictogramme) ; hauteur = hauteur actuelle. Calculée sans passer par
    /// base.GetPreferredSize, qui (en GrowOnly) repart de la taille actuelle : la largeur grandirait à chaque mise en page.
    /// </summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        var texte = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        var largeur = texte.Width + Padding.Horizontal + (int)Dessin.Echelle(this, 4);
        if (_icone is not null && Text.Length > 0)
        {
            largeur += (int)Dessin.Echelle(this, 28);
        }

        return new Size(largeur, Height);
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
        if (_style == StyleBouton.Menu)
        {
            PeindreMenu(g);
            return;
        }

        var (haut, bas, texte, bordure) = Couleurs();
        Dessin.EffacerAvecParent(this, g);
        Dessin.Qualite(g);
        var ep = bordure is null ? 0 : Dessin.Echelle(this, 1);
        var r = new RectangleF(ep / 2, ep / 2, Width - ep - 1, Height - ep - 1);
        using (var chemin = Dessin.Arrondi(r, Dessin.Echelle(this, _style == StyleBouton.Icone ? 12 : Theme.RayonBouton)))
        {
            if (haut is { } h)
            {
                using Brush pinceau = bas is { } b && b != h
                    ? new LinearGradientBrush(new PointF(0, 0), new PointF(0, Height), h, b)
                    : new SolidBrush(h);
                g.FillPath(pinceau, chemin);
            }

            if (bordure is { } couleur)
            {
                using var stylo = new Pen(couleur, ep);
                g.DrawPath(stylo, chemin);
            }
        }

        if (Focused && ShowFocusCues)
        {
            var retrait = Dessin.Echelle(this, 3);
            using var chemin = Dessin.Arrondi(new RectangleF(retrait, retrait, Width - (2 * retrait) - 1, Height - (2 * retrait) - 1), Dessin.Echelle(this, Theme.RayonBouton - 3));
            using var stylo = new Pen(haut is null || _style is StyleBouton.Secondaire or StyleBouton.Verre ? Theme.Accent : texte, Dessin.Echelle(this, 1.5f));
            g.DrawPath(stylo, chemin);
        }

        PeindreContenu(g, texte);
        _pastille?.PeindreInterieur(g);
    }

    private void PeindreContenu(Graphics g, Color texte)
    {
        var tailleIcone = Dessin.Echelle(this, 18);
        if (_style == StyleBouton.Icone || Text.Length == 0)
        {
            if (_icone is not null)
            {
                Icones.Dessiner(g, _icone, new RectangleF((Width - tailleIcone) / 2, (Height - tailleIcone) / 2, tailleIcone, tailleIcone), texte, 2f);
            }

            return;
        }

        var drapeaux = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        var mesure = TextRenderer.MeasureText(g, Text, Font, Size.Empty, drapeaux);
        var ecart = _icone is null ? 0 : Dessin.Echelle(this, 10);
        var largeur = mesure.Width + (_icone is null ? 0 : tailleIcone + ecart);
        var x = _style == StyleBouton.Lien ? Padding.Left : Math.Max(Padding.Left / 2f, (Width - largeur) / 2);
        var yIcone = (Height - tailleIcone) / 2;
        if (_icone is not null && !_iconeApres)
        {
            Icones.Dessiner(g, _icone, new RectangleF(x, yIcone, tailleIcone, tailleIcone), texte, 2.2f);
            x += tailleIcone + ecart;
        }

        var zoneTexte = Rectangle.FromLTRB((int)x, 0, Width - (int)(Padding.Right / 2f), Height);
        TextRenderer.DrawText(g, Text, Font, zoneTexte, texte, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        var fin = Math.Min(x + mesure.Width, zoneTexte.Right);
        if (_icone is not null && _iconeApres)
        {
            Icones.Dessiner(g, _icone, new RectangleF(fin + ecart, yIcone, tailleIcone, tailleIcone), texte, 2.2f);
        }
    }

    private void PeindreMenu(Graphics g)
    {
        Dessin.EffacerAvecParent(this, g);
        Dessin.Qualite(g);
        var fond = Dessin.CouleurParent(this);
        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        if (_selectionne || _survol)
        {
            using var chemin = Dessin.Arrondi(r, Dessin.Echelle(this, Theme.RayonBouton));
            using var pinceau = _selectionne
                ? new LinearGradientBrush(new PointF(0, 0), new PointF(Width, 0), Theme.Sur(fond, Theme.Accent, 0.18f), Theme.Sur(fond, Theme.Accent, 0.03f))
                : new LinearGradientBrush(new PointF(0, 0), new PointF(Width, 0), Theme.Sur(fond, Theme.AccentClair, 0.07f), Theme.Sur(fond, Theme.AccentClair, 0.02f));
            g.FillPath(pinceau, chemin);
        }

        if (Focused && ShowFocusCues)
        {
            using var chemin = Dessin.Arrondi(RectangleF.Inflate(r, -2, -2), Dessin.Echelle(this, Theme.RayonBouton - 2));
            using var stylo = new Pen(Theme.Accent, Dessin.Echelle(this, 1.5f));
            g.DrawPath(stylo, chemin);
        }

        var couleur = _selectionne ? Theme.TexteActif : Theme.TexteSecondaireClair;
        var t = Dessin.Echelle(this, 20);
        var x = Dessin.Echelle(this, 14);
        if (_icone is not null)
        {
            Icones.Dessiner(g, _icone, new RectangleF(x, (Height - t) / 2, t, t), _selectionne ? Theme.Accent : couleur);
        }

        var police = _selectionne ? Theme.PoliceTitre(10.5f) : Font;
        var gauche = (int)(x + t + Dessin.Echelle(this, 12));
        var droite = Width - (int)Dessin.Echelle(this, _selectionne ? 26 : 6);
        Dessin.Texte(g, Text, police, Rectangle.FromLTRB(gauche, 0, droite, Height), couleur);

        if (_selectionne)
        {
            // Point lumineux à droite de l'entrée active.
            var d = Dessin.Echelle(this, 6);
            var cx = Width - Dessin.Echelle(this, 16);
            var cy = Height / 2f;
            for (var i = 3; i >= 1; i--)
            {
                var halo = d + (i * Dessin.Echelle(this, 3));
                using var lueur = new SolidBrush(Theme.Alpha(Theme.Accent, 0.12f));
                g.FillEllipse(lueur, cx - (halo / 2), cy - (halo / 2), halo, halo);
            }

            using var point = new SolidBrush(Theme.Accent);
            g.FillEllipse(point, cx - (d / 2), cy - (d / 2), d, d);
        }
    }

    private (Color? Haut, Color? Bas, Color Texte, Color? Bordure) Couleurs()
    {
        if (!Enabled)
        {
            return (Theme.SurfaceCreuse, null, Theme.TexteSecondaire, Theme.Bordure);
        }

        Color Etat(Color c) => _appui ? Theme.Assombrir(c) : _survol ? Theme.Eclaircir(c) : c;
        var parent = Dessin.CouleurParent(this);

        return _style switch
        {
            StyleBouton.Principal => (Etat(Theme.Accent), null, Theme.TexteSurAccent, null),
            StyleBouton.Depart => (Etat(Theme.DepartClair), Etat(Theme.Depart), Theme.TexteSurDepart, null),
            StyleBouton.Verrou => (Etat(Theme.Avertissement), null, Theme.TexteSurAvertissement, null),
            StyleBouton.Retour => (Etat(Theme.Sur(Theme.Retour, Color.White, 0.2f)), Etat(Theme.Retour), Theme.TexteSurRetour, null),
            StyleBouton.Secondaire => (_survol ? Theme.SurfaceActive : Theme.Sur(parent, Theme.Fond, 0.45f), null, Theme.Texte, Theme.BordureForte),
            StyleBouton.Verre => (Theme.Sur(Theme.BandeauMilieu, Theme.Fond, _survol ? 0.15f : 0.35f), null, Theme.Texte, Theme.Sur(Theme.BandeauMilieu, Theme.Lumiere, 0.25f)),
            StyleBouton.Icone => (_survol ? Theme.SurfaceActive : null, null, CouleurIcone ?? (_survol ? Theme.Texte : Theme.TexteSecondaire), null),
            _ => (_survol ? Theme.Sur(parent, Theme.Accent, 0.10f) : null, null, Theme.Accent, null),
        };
    }
}
