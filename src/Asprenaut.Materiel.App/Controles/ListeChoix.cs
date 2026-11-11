using System.ComponentModel;
using Asprenaut.Materiel.App.Apparence;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>
/// Un élément de <see cref="ListeChoix"/> : badge (« M » ou un code), titre, détail.
/// <paramref name="BadgeCode"/> : badge large en police de code (A07), sinon carré à une lettre.
/// </summary>
public sealed record ElementChoix(string Cle, string Badge, bool BadgeCode, string Titre, string Detail);

/// <summary>
/// Liste de choix défilante (« Modèles de l'inventaire », « Équipements de l'inventaire ») : une carte par élément,
/// badge à gauche, titre et détail. Tout est dessiné dans un seul contrôle (seule la partie visible est peinte) :
/// l'inventaire entier reste fluide sans créer un contrôle par ligne. Clic ou flèches pour choisir.
/// </summary>
public sealed class ListeChoix : ScrollableControl
{
    // Pixels logiques (96 dpi).
    private const int HauteurLigne = 58;
    private const int Ecart = 8;
    private const int HauteurVisible = 290;

    private IReadOnlyList<ElementChoix> _elements = [];
    private string _messageVide = "";
    private int _survol = -1;
    private int _choix = -1;

    public ListeChoix()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        AutoScroll = true;
        Margin = Padding.Empty;
        AccessibleRole = AccessibleRole.List;
        TabStop = true;
        Natif.ScrollbarsDuTheme(this);
        Height = HauteurVisible; // Pixels logiques, mis à l'échelle avec l'écran.
    }

    /// <summary>L'élément choisi a changé (null : aucun).</summary>
    public event EventHandler? ChoixChange;

    /// <summary>Élément choisi, ou null.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ElementChoix? Choix => _choix >= 0 && _choix < _elements.Count ? _elements[_choix] : null;

    /// <summary>Remplace les éléments ; le choix est gardé si sa clé est encore là.</summary>
    public void Afficher(IReadOnlyList<ElementChoix> elements, string messageVide)
    {
        var cle = Choix?.Cle;
        _elements = elements;
        _largeurBadge = 0;
        _messageVide = messageVide;
        _survol = -1;
        _choix = cle is null ? -1 : IndexDe(cle);
        AccessibleDescription = elements.Count == 0 ? messageVide : $"{elements.Count} éléments";
        MettreAJourTaille();
        Invalidate();
        if (cle is not null && _choix < 0)
        {
            ChoixChange?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Choisit l'élément de cette clé (code scanné) et le fait apparaître ; faux s'il n'est pas dans la liste.</summary>
    public bool Choisir(string cle)
    {
        var index = IndexDe(cle);
        if (index < 0)
        {
            return false;
        }

        Selectionner(index);
        return true;
    }

    public void EffacerChoix()
    {
        if (_choix >= 0)
        {
            _choix = -1;
            Invalidate();
            ChoixChange?.Invoke(this, EventArgs.Empty);
        }
    }

    private int IndexDe(string cle)
    {
        for (var i = 0; i < _elements.Count; i++)
        {
            if (string.Equals(_elements[i].Cle, cle, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private float E(float v) => Dessin.Echelle(this, v);

    private float Pas => E(HauteurLigne + Ecart);

    /// <summary>Hauteur fixe (environ 4 lignes et demie) : la liste défile, la carte ne grandit pas.</summary>
    public override Size GetPreferredSize(Size proposedSize) =>
        new(proposedSize.Width is > 0 and < int.MaxValue ? proposedSize.Width : Width, (int)E(HauteurVisible));

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        BackColor = Dessin.CouleurParent(this);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        MettreAJourTaille();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        _largeurBadge = 0;
        MettreAJourTaille();
    }

    private void MettreAJourTaille() =>
        AutoScrollMinSize = new Size(0, _elements.Count == 0 ? 0 : (int)((_elements.Count * Pas) - E(Ecart)));

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

    private int IndexSous(Point p)
    {
        var y = p.Y - AutoScrollPosition.Y;
        var i = (int)(y / Pas);
        return i >= 0 && i < _elements.Count && y - (i * Pas) <= E(HauteurLigne) ? i : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var i = IndexSous(e.Location);
        if (i != _survol)
        {
            _survol = i;
            Cursor = i < 0 ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_survol >= 0)
        {
            _survol = -1;
            Invalidate();
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left && IndexSous(e.Location) is var i and >= 0)
        {
            Focus();
            Selectionner(i);
        }
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_elements.Count > 0 && e.KeyCode is Keys.Up or Keys.Down)
        {
            Selectionner(Math.Clamp(_choix + (e.KeyCode == Keys.Down ? 1 : -1), 0, _elements.Count - 1));
            e.Handled = true;
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    private void Selectionner(int index)
    {
        var change = index != _choix;
        _choix = index;

        // Fait apparaître la ligne choisie.
        var haut = index * Pas;
        var bas = haut + E(HauteurLigne);
        var vue = -AutoScrollPosition.Y;
        if (haut < vue)
        {
            AutoScrollPosition = new Point(0, (int)haut);
        }
        else if (bas > vue + ClientSize.Height)
        {
            AutoScrollPosition = new Point(0, (int)(bas - ClientSize.Height));
        }

        Invalidate();
        if (change)
        {
            AccessibilityNotifyClients(AccessibleEvents.Selection, index);
            ChoixChange?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Dessin.Qualite(g);
        var largeur = ClientSize.Width;
        if (_elements.Count == 0)
        {
            Dessin.Texte(g, _messageVide, Theme.Police(10f), new Rectangle(0, 0, largeur, (int)E(HauteurLigne)), Theme.TexteSecondaire, centre: true);
            return;
        }

        var premier = Math.Max(0, (int)((e.ClipRectangle.Top - AutoScrollPosition.Y) / Pas));
        for (var i = premier; i < _elements.Count; i++)
        {
            var y = AutoScrollPosition.Y + (i * Pas);
            if (y > e.ClipRectangle.Bottom)
            {
                break;
            }

            PeindreLigne(g, _elements[i], new RectangleF(0.5f, y + 0.5f, largeur - E(4) - 1, E(HauteurLigne) - 1), i == _choix, i == _survol);
        }
    }

    private float _largeurBadge;

    /// <summary>Badges de code à la largeur du plus long (A07 comme BLOC-031), pour aligner les titres.</summary>
    private float LargeurBadgeCode()
    {
        if (_largeurBadge <= 0)
        {
            var police = Theme.PoliceCode(9.75f);
            var plusLong = _elements.Where(e => e.BadgeCode).Select(e => TextRenderer.MeasureText(e.Badge, police, Size.Empty, TextFormatFlags.NoPadding).Width).DefaultIfEmpty(0).Max();
            _largeurBadge = Math.Max(E(58), plusLong + E(20));
        }

        return _largeurBadge;
    }

    private void PeindreLigne(Graphics g, ElementChoix el, RectangleF r, bool choisi, bool survol)
    {
        var fond = choisi ? Theme.Sur(BackColor, Theme.Accent, 0.16f)
            : survol ? Theme.Sur(BackColor, Theme.Surface, 0.9f)
            : Theme.Sur(BackColor, Theme.Surface, 0.6f);
        using (var chemin = Dessin.Arrondi(r, E(12)))
        using (var pinceau = new SolidBrush(fond))
        {
            g.FillPath(pinceau, chemin);
            if (choisi)
            {
                using var bord = new Pen(Theme.Accent, E(Focused ? 2 : 1.5f));
                g.DrawPath(bord, chemin);
            }
        }

        // Badge : carré « M », ou pastille large avec le code (A07).
        var hb = E(32);
        var lb = el.BadgeCode ? LargeurBadgeCode() : hb;
        var badge = new RectangleF(r.Left + E(14), r.Top + ((r.Height - hb) / 2), lb, hb);
        using (var chemin = Dessin.Arrondi(badge, E(9)))
        using (var pinceau = new SolidBrush(Theme.FondAccent))
        {
            g.FillPath(pinceau, chemin);
        }

        Dessin.Texte(g, el.Badge, el.BadgeCode ? Theme.PoliceCode(9.75f) : Theme.PoliceGrasse(10.5f), Rectangle.Round(badge),
            el.BadgeCode ? Theme.Accent : Theme.AccentClair, centre: true);

        var gauche = (int)(badge.Right + E(14));
        var droite = (int)(r.Right - E(12));
        var milieu = r.Top + (r.Height / 2);
        Dessin.Texte(g, el.Titre, Theme.PoliceTitre(10.5f), Rectangle.FromLTRB(gauche, (int)(milieu - E(22)), droite, (int)(milieu + E(1))), Theme.Texte);
        Dessin.Texte(g, el.Detail, Theme.Police(9f), Rectangle.FromLTRB(gauche, (int)(milieu + E(1)), droite, (int)(milieu + E(21))), Theme.TexteSecondaire);
    }
}
