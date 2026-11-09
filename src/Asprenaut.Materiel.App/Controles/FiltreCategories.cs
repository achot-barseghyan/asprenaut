using System.ComponentModel;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>
/// Puces de filtre par catégorie : « Tout 12 », « Blocs 3 »... qui passent à la ligne.
/// Si le filtre est dans une ligne "Absolute" d'un TableLayoutPanel, il en ajuste la hauteur.
/// </summary>
public sealed class FiltreCategories : Panel
{
    private const int HauteurPuce = 44; // Zone cliquable ; la puce dessinée fait 32 px.
    private const int Ecart = 6;

    private readonly Label? _intitule;
    private readonly Color _couleurBadge;

    public FiltreCategories(Color couleurBadge, string? intitule = null)
    {
        _couleurBadge = couleurBadge;
        BackColor = Color.Transparent;
        Margin = Padding.Empty;
        DoubleBuffered = true;
        if (intitule is not null)
        {
            _intitule = Ui.Libelle(intitule, Theme.PoliceTitre(9.75f), Theme.TexteSecondaire);
            _intitule.Margin = Padding.Empty;
            Controls.Add(_intitule);
        }
    }

    /// <summary>La sélection a changé (clic sur une puce).</summary>
    public event EventHandler? Changement;

    /// <summary>Catégorie choisie ; null = « Tout ».</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Selection { get; private set; }

    /// <summary>Unité lue par le lecteur d'écran : « Blocs : 3 équipements ».</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Unite { get; set; } = "équipement";

    /// <summary>Couleur du petit point devant le libellé d'une catégorie (aucun point si null).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string, Color?>? CouleurPoint { get; set; }

    /// <summary>Vrai si l'élément de cette catégorie passe le filtre.</summary>
    public bool Accepte(string? categorie) => Selection is null || string.Equals(categorie, Selection, StringComparison.Ordinal);

    /// <summary>
    /// Remplace les puces. La sélection est gardée si la catégorie existe encore, sinon on revient à « Tout ».
    /// </summary>
    public void Afficher(int total, IEnumerable<(string Categorie, int Nombre)> categories)
    {
        var liste = categories.ToList();
        if (Selection is not null && liste.All(c => c.Categorie != Selection))
        {
            Selection = null;
        }

        SuspendLayout();
        foreach (var puce in Controls.OfType<PuceFiltre>().ToList())
        {
            Controls.Remove(puce);
            puce.Dispose();
        }

        Ajouter("Tout", null, total);
        foreach (var (categorie, nombre) in liste)
        {
            Ajouter(categorie, categorie, nombre);
        }

        ResumeLayout(true);
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        if (e.Control is { } enfant)
        {
            Dessin.MettreALEchelleSiAjoutTardif(this, enfant);
        }

        base.OnControlAdded(e);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var hauteur = (int)Dessin.Echelle(this, HauteurPuce);
        var ecart = (int)Dessin.Echelle(this, Ecart);
        int x = 0, y = 0;
        if (_intitule is not null)
        {
            var t = _intitule.PreferredSize;
            _intitule.SetBounds(0, (hauteur - t.Height) / 2, t.Width, t.Height);
            x = t.Width + (int)Dessin.Echelle(this, 12);
        }

        foreach (var puce in Controls.OfType<PuceFiltre>())
        {
            var largeur = puce.GetPreferredSize(Size.Empty).Width;
            if (x > 0 && x + largeur > Width)
            {
                x = 0;
                y += hauteur;
            }

            puce.SetBounds(x, y, largeur, hauteur);
            x += largeur + ecart;
        }

        AjusterLigne(y + hauteur);
    }

    private void Ajouter(string libelle, string? categorie, int nombre)
    {
        var point = categorie is null ? null : CouleurPoint?.Invoke(categorie);
        var puce = new PuceFiltre(libelle, nombre, _couleurBadge, point, Unite) { Selectionne = categorie == Selection };
        puce.Click += (_, _) =>
        {
            Selection = categorie;
            foreach (var p in Controls.OfType<PuceFiltre>())
            {
                p.Selectionne = p == puce;
            }

            Changement?.Invoke(this, EventArgs.Empty);
        };
        Controls.Add(puce);
    }

    private void AjusterLigne(int hauteur)
    {
        if (Parent is not TableLayoutPanel table)
        {
            return;
        }

        var ligne = table.GetRow(this);
        if (ligne < 0 || ligne >= table.RowStyles.Count || table.RowStyles[ligne].SizeType != SizeType.Absolute)
        {
            return;
        }

        _hauteurVoulue = hauteur + Margin.Vertical;
        if (Math.Abs(table.RowStyles[ligne].Height - _hauteurVoulue) > 0.5f && IsHandleCreated)
        {
            // Relit la dernière hauteur calculée : un calcul fait à une largeur provisoire ne doit pas gagner.
            BeginInvoke(() =>
            {
                var l = table.GetRow(this);
                if (l >= 0 && l < table.RowStyles.Count)
                {
                    table.RowStyles[l].Height = _hauteurVoulue;
                }
            });
        }
    }

    private int _hauteurVoulue;

    /// <summary>Une puce : libellé et compteur ; claire quand elle est choisie.</summary>
    private sealed class PuceFiltre : Button
    {
        private readonly int _nombre;
        private const int Point = 8;
        private readonly Color _couleurBadge;
        private readonly Color? _point;
        private bool _selectionne;
        private bool _survol;

        public PuceFiltre(string libelle, int nombre, Color couleurBadge, Color? point, string unite)
        {
            _point = point;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseMnemonic = false;
            Cursor = Cursors.Hand;
            Margin = Padding.Empty;
            Text = libelle;
            Font = Theme.PoliceTitre(9f);
            _nombre = nombre;
            _couleurBadge = couleurBadge;
            AccessibleName = $"{libelle} : {Libelles.Pluriel(nombre, unite)}";
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selectionne
        {
            get => _selectionne;
            set
            {
                _selectionne = value;
                AccessibleDescription = value ? "Filtre choisi" : null;
                Invalidate();
            }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var texte = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            var badge = LargeurBadge();
            var point = _point is null ? 0 : Dessin.Echelle(this, Point + 8);
            return new Size((int)(texte + badge + point + Dessin.Echelle(this, 12 + 8 + 6)), Height);
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
            Dessin.EffacerAvecParent(this, g);
            Dessin.Qualite(g);
            var fondParent = Dessin.CouleurParent(this);
            var h = Dessin.Echelle(this, 32);
            var r = new RectangleF(0.5f, (Height - h) / 2, Width - 1.5f, h);

            var fond = _selectionne ? Theme.Texte : _survol ? Theme.Sur(fondParent, Theme.AccentClair, 0.10f) : Theme.Sur(fondParent, Theme.Fond, 0.35f);
            var texte = _selectionne ? Theme.Fond : Theme.TexteDoux;
            using (var chemin = Dessin.Arrondi(r, h / 2))
            {
                using var pinceau = new SolidBrush(fond);
                g.FillPath(pinceau, chemin);
                if (!_selectionne)
                {
                    using var bord = new Pen(Theme.Bordure, Dessin.Echelle(this, 1));
                    g.DrawPath(bord, chemin);
                }

                if (Focused && ShowFocusCues)
                {
                    using var focus = new Pen(Theme.Accent, Dessin.Echelle(this, 1.5f));
                    g.DrawPath(focus, chemin);
                }
            }

            var x = (int)Dessin.Echelle(this, 12);
            if (_point is { } couleurPoint)
            {
                var d = Dessin.Echelle(this, Point);
                using var pinceau = new SolidBrush(_selectionne ? Theme.Sur(Theme.Texte, couleurPoint, 0.85f) : couleurPoint);
                g.FillEllipse(pinceau, x, (Height - d) / 2, d, d);
                x += (int)Dessin.Echelle(this, Point + 8);
            }

            // Mesure et dessin avec les mêmes options (sans marge interne), sinon le texte est coupé par « … ».
            const TextFormatFlags drapeaux = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            var tailleTexte = TextRenderer.MeasureText(g, Text, Font, Size.Empty, drapeaux);
            var largeurTexte = tailleTexte.Width;
            TextRenderer.DrawText(g, Text, Font, new Point(x, (Height - tailleTexte.Height) / 2), texte, drapeaux);

            // Compteur dans une petite pastille.
            var lb = LargeurBadge();
            var hb = Dessin.Echelle(this, 20);
            var badge = new RectangleF(x + largeurTexte + Dessin.Echelle(this, 6), (Height - hb) / 2, lb, hb);
            var couleur = _nombre == 0 ? Theme.TexteSecondaire : _couleurBadge;
            var fondBadge = _selectionne ? Theme.Sur(Theme.Texte, Theme.Fond, 0.12f) : Theme.Sur(fond, couleur, 0.18f);
            using (var chemin = Dessin.Arrondi(badge, hb / 2))
            using (var pinceau = new SolidBrush(fondBadge))
            {
                g.FillPath(pinceau, chemin);
            }

            Dessin.Texte(g, _nombre.ToString(Libelles.Culture), Theme.PoliceTitre(7.5f), Rectangle.Round(badge), _selectionne ? Theme.Fond : couleur, centre: true);
        }

        private float LargeurBadge() =>
            Math.Max(Dessin.Echelle(this, 22), TextRenderer.MeasureText(_nombre.ToString(Libelles.Culture), Theme.PoliceTitre(7.5f)).Width + Dessin.Echelle(this, 4));
    }
}
