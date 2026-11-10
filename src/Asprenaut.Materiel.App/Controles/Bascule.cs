using System.ComponentModel;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>
/// Choix entre quelques options côte à côte (« Moniteurs 4 | Classes 4 », « Moniteur | Classe ») : un cadre arrondi,
/// l'option choisie en pastille claire. Chaque option est un vrai bouton (clavier, lecteur d'écran) de 44 px de haut.
/// </summary>
public sealed class Bascule : Panel
{
    private const int Marge = 4;
    private readonly List<Segment> _segments = [];
    private int _index;

    public Bascule(params string[] options)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Margin = Padding.Empty;
        AccessibleRole = AccessibleRole.Grouping;
        for (var i = 0; i < options.Length; i++)
        {
            var segment = new Segment(options[i]) { Selectionne = i == 0 };
            var indice = i;
            segment.Click += (_, _) => SelectedIndex = indice;
            _segments.Add(segment);
            Controls.Add(segment);
        }

        Height = Theme.HauteurCliquable + (2 * Marge);
    }

    /// <summary>L'option choisie a changé (clic, ou <see cref="SelectedIndex"/> modifié par le code).</summary>
    public event EventHandler? Changement;

    /// <summary>Texte plus petit et marges réduites (dans une rangée de formulaire, à côté d'une liste).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Compact
    {
        get => _segments.Count > 0 && _segments[0].Compact;
        set
        {
            foreach (var s in _segments)
            {
                s.Compact = value;
            }

            PerformLayout();
        }
    }

    /// <summary>Options de même largeur, qui remplissent le contrôle (sinon chacune à la largeur de son texte).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Etirer { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _index;
        set
        {
            value = Math.Clamp(value, 0, Math.Max(0, _segments.Count - 1));
            if (value == _index)
            {
                return;
            }

            _index = value;
            for (var i = 0; i < _segments.Count; i++)
            {
                _segments[i].Selectionne = i == value;
            }

            Changement?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Nombre affiché dans une petite pastille après le libellé (null pour l'enlever).</summary>
    public void DefinirNombre(int index, int? nombre)
    {
        _segments[index].Nombre = nombre;
        PerformLayout();
    }

    private int E(float v) => (int)Dessin.Echelle(this, v);

    public override Size GetPreferredSize(Size proposedSize) =>
        new(_segments.Sum(s => s.Largeur()) + E(2 * Marge), E(Theme.HauteurCliquable + (2 * Marge)));

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_segments.Count == 0)
        {
            return;
        }

        // Largeur suivant le contenu (nombres ajoutés après coup, passage à l'échelle de l'écran), sauf si étirée.
        if (!Etirer)
        {
            var voulue = GetPreferredSize(Size.Empty).Width;
            if (Width != voulue)
            {
                Width = voulue; // La mise en page relancée par ce changement serait ignorée : on continue ici.
            }
        }

        var marge = E(Marge);
        var h = Height - (2 * marge);
        var x = marge;
        var egale = (Width - (2 * marge)) / _segments.Count;
        foreach (var s in _segments)
        {
            var l = Etirer ? egale : s.Largeur();
            s.SetBounds(x, marge, l, h);
            x += l;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Dessin.EffacerAvecParent(this, g);
        Dessin.Qualite(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
        using var chemin = Dessin.Arrondi(r, Dessin.Echelle(this, 14));
        using var fond = new SolidBrush(Theme.Sur(Dessin.CouleurParent(this), Theme.Fond, 0.55f));
        using var bord = new Pen(Theme.Bordure, Dessin.Echelle(this, 1));
        g.FillPath(fond, chemin);
        g.DrawPath(bord, chemin);
    }

    /// <summary>Une option : texte (et nombre) ; pastille claire quand elle est choisie.</summary>
    private sealed class Segment : Button
    {
        private bool _selectionne;
        private bool _survol;
        private int? _nombre;

        public Segment(string libelle)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseMnemonic = false;
            Cursor = Cursors.Hand;
            Margin = Padding.Empty;
            Text = libelle;
            AccessibleName = libelle;
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selectionne
        {
            get => _selectionne;
            set
            {
                _selectionne = value;
                AccessibleDescription = value ? "Choisi" : null;
                Invalidate();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int? Nombre
        {
            get => _nombre;
            set
            {
                _nombre = value;
                AccessibleName = value is { } n ? $"{Text} : {n}" : Text;
                Invalidate();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Compact { get; set; }

        private float Taille => Compact ? 9.75f : 10.5f;

        private Font PoliceTexte => _selectionne ? Theme.PoliceGrasse(Taille) : Theme.PoliceTitre(Taille);

        private static Font PoliceNombre => Theme.PoliceGrasse(8.25f);

        private float E(float v) => Dessin.Echelle(this, v);

        private int LargeurNombre() => _nombre is { } n
            ? TextRenderer.MeasureText(n.ToString(Libelles.Culture), PoliceNombre, Size.Empty, TextFormatFlags.NoPadding).Width + (int)E(16)
            : 0;

        /// <summary>Largeur voulue : texte en gras (pour ne pas bouger au choix), nombre, marges.</summary>
        public int Largeur() =>
            TextRenderer.MeasureText(Text, Theme.PoliceGrasse(Taille), Size.Empty, TextFormatFlags.NoPadding).Width
            + (_nombre is null ? 0 : LargeurNombre() + (int)E(8))
            + (int)E(Compact ? 24 : 36);

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
            Dessin.EffacerAvecParent(this, g);
            Dessin.Qualite(g);
            var fondParent = Dessin.CouleurParent(this);
            var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            if (_selectionne || _survol)
            {
                using var chemin = Dessin.Arrondi(r, E(11));
                using var fond = new SolidBrush(_selectionne ? Theme.Texte : Theme.Sur(fondParent, Theme.AccentClair, 0.08f));
                g.FillPath(fond, chemin);
            }

            if (Focused && ShowFocusCues)
            {
                using var chemin = Dessin.Arrondi(RectangleF.Inflate(r, -2, -2), E(9));
                using var focus = new Pen(_selectionne ? Theme.Fond : Theme.Accent, E(1.5f));
                g.DrawPath(focus, chemin);
            }

            var couleur = _selectionne ? Theme.Fond : Theme.TexteDoux;
            var police = PoliceTexte;
            var lTexte = TextRenderer.MeasureText(g, Text, police, Size.Empty, TextFormatFlags.NoPadding).Width;
            var lNombre = LargeurNombre();
            var total = lTexte + (lNombre > 0 ? lNombre + (int)E(8) : 0);
            var x = (Width - total) / 2;
            TextRenderer.DrawText(g, Text, police, new Rectangle(x, 0, lTexte + 2, Height), couleur,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            if (_nombre is { } n)
            {
                var rn = new RectangleF(x + lTexte + E(8), (Height - E(22)) / 2, lNombre, E(22));
                using var chemin = Dessin.Arrondi(rn, rn.Height / 2);
                using var fond = new SolidBrush(_selectionne ? Theme.Sur(Theme.Texte, Theme.Fond, 0.14f) : Theme.Sur(fondParent, Theme.AccentClair, 0.14f));
                g.FillPath(fond, chemin);
                Dessin.Texte(g, n.ToString(Libelles.Culture), PoliceNombre, Rectangle.Round(rn), _selectionne ? Theme.Fond : Theme.AccentClair, centre: true);
            }
        }
    }
}
