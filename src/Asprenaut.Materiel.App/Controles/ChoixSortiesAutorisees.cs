using System.ComponentModel;
using System.Drawing.Drawing2D;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>
/// « Sorties autorisées » d'un équipement (maquette « Ajouter un équipement ») : un bouton par type de sortie, coloré
/// (case cochée) s'il est autorisé, en pointillés et barré avec une croix rouge s'il est interdit ; un clic bascule.
/// « Tout autoriser » au bout de la ligne dès qu'un type est interdit. Clavier : flèches, Espace. Un seul contrôle dessiné.
/// </summary>
public sealed class ChoixSortiesAutorisees : ControleDessine
{
    private const int HauteurPuce = 42;
    private const int Ecart = 8;
    private const string ToutAutoriser = "Tout autoriser";

    private readonly List<TypeDeSortie> _types = [];
    private readonly HashSet<long> _interdits = [];
    private readonly List<(RectangleF Zone, int Index)> _zones = []; // Index -1 : « Tout autoriser ».
    private int _survol = -2;
    private int _focus;
    private int _largeurPlacee = -1;
    private int _hauteur = HauteurPuce;

    public ChoixSortiesAutorisees()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        AutoSize = true;
        AccessibleName = "Sorties autorisées";
        AccessibleRole = AccessibleRole.List;
        Height = HauteurPuce;
    }

    /// <summary>Un type a été autorisé ou interdit.</summary>
    public event EventHandler? Changement;

    /// <summary>Types proposés, dans l'ordre d'affichage.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<TypeDeSortie> Types => _types;

    /// <summary>Types interdits (ids), triés.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<long> Interdits => [.. _interdits.Order()];

    public void Definir(IEnumerable<TypeDeSortie> types, IEnumerable<long> interdits)
    {
        _types.Clear();
        _types.AddRange(types);
        _interdits.Clear();
        _interdits.UnionWith(interdits.Where(id => _types.Any(t => t.Id == id)));
        Replacer();
        Changement?.Invoke(this, EventArgs.Empty);
    }

    public void ToutAutoriserTypes()
    {
        if (_interdits.Count == 0)
        {
            return;
        }

        _interdits.Clear();
        Replacer();
        Changement?.Invoke(this, EventArgs.Empty);
    }

    private void Basculer(int index)
    {
        if (index < 0)
        {
            ToutAutoriserTypes();
            return;
        }

        var id = _types[index].Id;
        if (!_interdits.Remove(id))
        {
            _interdits.Add(id);
        }

        Replacer();
        AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
        Changement?.Invoke(this, EventArgs.Empty);
    }

    private void Replacer()
    {
        _largeurPlacee = -1;
        Placer(Width);
        Parent?.PerformLayout(this, nameof(Height));
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var largeur = MesureFormulaire.Largeur(this, proposedSize);
        Placer(largeur);
        return new Size(largeur, _hauteur);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Placer(Width);
        if (Height != _hauteur)
        {
            Parent?.PerformLayout(this, nameof(Height));
        }
    }

    private static Font PolicePuce => Theme.PoliceGrasse(10.5f);

    private void Placer(int largeur)
    {
        if (largeur == _largeurPlacee || largeur <= 0)
        {
            return;
        }

        _largeurPlacee = largeur;
        _zones.Clear();
        var hauteur = E(HauteurPuce);
        var x = 0f;
        var y = 0f;
        void Poser(float l, int index)
        {
            if (x > 0 && x + l > largeur)
            {
                x = 0;
                y += hauteur + E(Ecart);
            }

            _zones.Add((new RectangleF(x, y, l, hauteur), index));
            x += l + E(Ecart);
        }

        for (var i = 0; i < _types.Count; i++)
        {
            Poser(E(10 + 20 + 8 + 14) + TextRenderer.MeasureText(_types[i].Nom, PolicePuce).Width, i);
        }

        if (_interdits.Count > 0)
        {
            Poser(E(24) + TextRenderer.MeasureText(ToutAutoriser, Theme.PoliceGrasse(9.75f)).Width, -1);
        }

        _hauteur = (int)Math.Ceiling(y + hauteur) + 1;
        _focus = Math.Clamp(_focus, 0, Math.Max(0, _zones.Count - 1));
    }

    protected override void Peindre(Graphics g)
    {
        Placer(Width);
        var fondParent = Parent?.BackColor ?? Theme.Surface;
        for (var z = 0; z < _zones.Count; z++)
        {
            var (zone, index) = _zones[z];
            var focus = Focused && z == _focus;
            var survol = z == _survol;
            if (index < 0)
            {
                // « Tout autoriser » : lien turquoise, sans fond (souligné au survol ou au focus).
                var police = Theme.PoliceGrasse(9.75f);
                Dessin.Texte(g, ToutAutoriser, police, Rectangle.Round(zone), Theme.AccentClair, centre: true);
                if (survol || focus)
                {
                    var l = TextRenderer.MeasureText(ToutAutoriser, police).Width;
                    using var trait = new Pen(Theme.AccentClair, E(1));
                    var yTrait = zone.Top + (zone.Height / 2) + E(10);
                    g.DrawLine(trait, zone.Left + ((zone.Width - l) / 2), yTrait, zone.Left + ((zone.Width + l) / 2), yTrait);
                }

                continue;
            }

            var type = _types[index];
            var autorise = !_interdits.Contains(type.Id);
            var couleur = Theme.CouleurTypeSortie(type);
            var r = new RectangleF(zone.X + 0.5f, zone.Y + 0.5f, zone.Width - 1, zone.Height - 1);
            using (var chemin = Dessin.Arrondi(r, E(12)))
            {
                if (autorise)
                {
                    using var fond = new SolidBrush(Theme.Sur(fondParent, couleur, survol ? 0.18f : 0.12f));
                    g.FillPath(fond, chemin);
                }
                else if (survol)
                {
                    using var fond = new SolidBrush(Theme.Sur(fondParent, Theme.Erreur, 0.06f));
                    g.FillPath(fond, chemin);
                }

                using var bord = new Pen(focus ? Theme.AccentClair : autorise ? Theme.Sur(fondParent, couleur, 0.45f) : Theme.BordureForte, focus ? E(2) : E(1));
                if (!autorise && !focus)
                {
                    bord.DashStyle = DashStyle.Dash;
                }

                g.DrawPath(bord, chemin);
            }

            // Case : cochée de la couleur du type, ou croix rouge.
            var caseZone = new RectangleF(zone.X + E(10), zone.Y + ((zone.Height - E(20)) / 2), E(20), E(20));
            using (var cheminCase = Dessin.Arrondi(caseZone, E(6)))
            using (var fondCase = new SolidBrush(autorise ? couleur : Theme.Sur(fondParent, Theme.Erreur, 0.16f)))
            {
                g.FillPath(fondCase, cheminCase);
            }

            Icones.Dessiner(g, autorise ? Icones.Coche : Icones.Croix, RectangleF.Inflate(caseZone, -E(4), -E(4)),
                autorise ? Theme.TexteSurAccent : Theme.Erreur, 3f);

            // Nom, barré si interdit.
            var police2 = PolicePuce;
            var xTexte = caseZone.Right + E(8);
            var texte = new Rectangle((int)xTexte, (int)zone.Y, (int)(zone.Right - xTexte), (int)zone.Height);
            var couleurTexte = autorise ? Theme.Texte : Theme.TexteSecondaire;
            Dessin.Texte(g, type.Nom, police2, texte, couleurTexte);
            if (!autorise)
            {
                var l = TextRenderer.MeasureText(type.Nom, police2, Size.Empty, TextFormatFlags.NoPadding).Width;
                using var barre = new Pen(couleurTexte, E(1.4f));
                var yBarre = zone.Top + (zone.Height / 2) + E(1);
                g.DrawLine(barre, xTexte + E(2), yBarre, xTexte + E(4) + l, yBarre);
            }
        }
    }

    private int Sous(Point p) => _zones.FindIndex(z => z.Zone.Contains(p));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var z = Sous(e.Location);
        Cursor = z >= 0 ? Cursors.Hand : Cursors.Default;
        if (z != _survol)
        {
            _survol = z;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _survol = -2;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (Sous(e.Location) is var z and >= 0)
        {
            _focus = z;
            Basculer(_zones[z].Index);
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_zones.Count == 0)
        {
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Up:
                _focus = Math.Max(0, _focus - 1);
                break;
            case Keys.Right or Keys.Down:
                _focus = Math.Min(_zones.Count - 1, _focus + 1);
                break;
            case Keys.Space:
                Basculer(_zones[_focus].Index);
                break;
            default:
                return;
        }

        e.Handled = true;
        Invalidate();
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
}
