using System.ComponentModel;
using Asprenaut.Materiel.App.Apparence;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>
/// Grille à colonnes de même largeur, dont le nombre dépend de la place (équivalent de
/// "repeat(auto-fill, minmax(400px, 1fr))"). Si elle est dans une ligne "Absolute" d'un TableLayoutPanel,
/// elle ajuste elle-même la hauteur de cette ligne ; au-delà de <see cref="RangeesVisibles"/>, elle défile.
/// </summary>
public sealed class GrilleAdaptative : Panel
{
    public GrilleAdaptative()
    {
        BackColor = Color.Transparent;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        DoubleBuffered = true;
        Natif.ScrollbarsSombres(this);
    }

    /// <summary>Largeur minimale d'une colonne (pixels logiques).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int LargeurMin { get; set; } = 400;

    /// <summary>Hauteur de chaque élément (pixels logiques).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int HauteurElement { get; set; } = 200;

    /// <summary>Espace entre éléments (pixels logiques).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Ecart { get; set; } = 18;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ColonnesMax { get; set; } = int.MaxValue;

    /// <summary>Nombre de rangées affichées avant défilement (0 = toutes).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int RangeesVisibles { get; set; }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var elements = Controls.Cast<Control>().Where(c => c.Visible).ToList();
        var ecart = (int)Dessin.Echelle(this, Ecart);
        var hauteur = (int)Dessin.Echelle(this, HauteurElement);
        var largeurDispo = ClientSize.Width;
        var colonnes = Math.Clamp((largeurDispo + ecart) / ((int)Dessin.Echelle(this, LargeurMin) + ecart), 1, Math.Max(1, ColonnesMax));
        var rangees = (elements.Count + colonnes - 1) / colonnes;
        var totale = Math.Max(0, (rangees * (hauteur + ecart)) - ecart);
        var visible = RangeesVisibles > 0 && rangees > RangeesVisibles
            ? (RangeesVisibles * (hauteur + ecart)) - ecart
            : totale;

        AutoScroll = visible < totale;
        if (AutoScroll)
        {
            largeurDispo = ClientSize.Width - SystemInformation.VerticalScrollBarWidth - (int)Dessin.Echelle(this, 4);
            AutoScrollMinSize = new Size(0, totale);
        }

        var largeur = Math.Max(1, (largeurDispo - ((colonnes - 1) * ecart)) / colonnes);
        for (var i = 0; i < elements.Count; i++)
        {
            var x = (i % colonnes) * (largeur + ecart);
            var y = ((i / colonnes) * (hauteur + ecart)) + AutoScrollPosition.Y;
            elements[i].SetBounds(x, y, largeur, hauteur);
        }

        AjusterLigne(visible);
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        if (e.Control is { } enfant)
        {
            Dessin.MettreALEchelleSiAjoutTardif(this, enfant);
        }

        base.OnControlAdded(e);
        PerformLayout();
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

        var voulue = hauteur + Margin.Vertical;
        if (Math.Abs(table.RowStyles[ligne].Height - voulue) > 0.5f)
        {
            // Différé : on ne change pas la mise en page du parent pendant sa propre mise en page.
            BeginInvokeSiPossible(() => table.RowStyles[ligne].Height = voulue);
        }
    }

    private void BeginInvokeSiPossible(Action action)
    {
        if (IsHandleCreated)
        {
            BeginInvoke(action);
        }
        else
        {
            action();
        }
    }
}

public enum DispositionTuile
{
    /// <summary>Pictogramme au-dessus du libellé (types de sortie).</summary>
    Verticale,

    /// <summary>Pictogramme à gauche, nombre et libellé à droite (catégories de l'inventaire).</summary>
    Horizontale,
}

/// <summary>Tuile sélectionnable (type de sortie, filtre de catégorie). Bouton accessible au clavier.</summary>
public sealed class Tuile : Button
{
    private bool _selectionne;
    private bool _survol;
    private int? _compte;

    public Tuile(string libelle, string icone, DispositionTuile disposition)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseMnemonic = false;
        Text = libelle;
        Icone = icone;
        Disposition = disposition;
        Cursor = Cursors.Hand;
        Margin = Padding.Empty;
        MinimumSize = new Size(0, Theme.HauteurCliquable);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Icone { get; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DispositionTuile Disposition { get; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selectionne
    {
        get => _selectionne;
        set
        {
            _selectionne = value;
            AccessibleDescription = value ? "Sélectionné" : null;
            Invalidate();
        }
    }

    /// <summary>Nombre affiché en gros (disposition horizontale).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int? Compte
    {
        get => _compte;
        set
        {
            _compte = value;
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
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        var fondParent = Dessin.CouleurParent(this);
        Color haut, bas, texte, icone;
        Color? bordure;
        if (_selectionne)
        {
            (haut, bas, texte, icone, bordure) = (Theme.AccentClair, Theme.Accent, Theme.TexteSurAccent, Theme.TexteSurAccent, null);
        }
        else
        {
            var fond = Disposition == DispositionTuile.Verticale ? Theme.Sur(fondParent, Theme.Fond, 0.45f) : Theme.Surface;
            if (_survol)
            {
                fond = Theme.Sur(fond, Theme.AccentClair, 0.06f);
            }

            (haut, bas, texte, icone, bordure) = (fond, fond, Theme.TexteDoux, Theme.AccentClair, Theme.Bordure);
        }

        Dessin.EffacerAvecParent(this, g);
        Dessin.Qualite(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
        using (var chemin = Dessin.Arrondi(r, Dessin.Echelle(this, 18)))
        {
            using var pinceau = new System.Drawing.Drawing2D.LinearGradientBrush(new PointF(0, 0), new PointF(0, Height + 1), haut, bas);
            g.FillPath(pinceau, chemin);
            if (bordure is { } b)
            {
                using var stylo = new Pen(b, Dessin.Echelle(this, 1));
                g.DrawPath(stylo, chemin);
            }

            if (Focused && ShowFocusCues)
            {
                using var focus = new Pen(_selectionne ? Theme.TexteSurAccent : Theme.Accent, Dessin.Echelle(this, 1.5f));
                using var interieur = Dessin.Arrondi(RectangleF.Inflate(r, -3, -3), Dessin.Echelle(this, 15));
                g.DrawPath(focus, interieur);
            }
        }

        if (Disposition == DispositionTuile.Verticale)
        {
            var t = Dessin.Echelle(this, 26);
            var police = _selectionne ? Theme.PoliceGrasse(10.5f) : Theme.PoliceTitre(10.5f);
            var hTexte = TextRenderer.MeasureText(Text, police).Height;
            var y = (Height - t - Dessin.Echelle(this, 8) - hTexte) / 2;
            Icones.Dessiner(g, Icone, new RectangleF((Width - t) / 2, y, t, t), icone);
            Dessin.Texte(g, Text, police, new Rectangle(0, (int)(y + t + Dessin.Echelle(this, 8)), Width, hTexte), texte, centre: true);
            return;
        }

        var cote = Dessin.Echelle(this, 42);
        var x = Dessin.Echelle(this, 14);
        var boite = new RectangleF(x, (Height - cote) / 2, cote, cote);
        using (var cheminBoite = Dessin.Arrondi(boite, Dessin.Echelle(this, 12)))
        using (var fondBoite = new SolidBrush(_selectionne ? Theme.Sur(Theme.Accent, Theme.TexteSurAccent, 0.12f) : Theme.FondAccent))
        {
            g.FillPath(fondBoite, cheminBoite);
        }

        Icones.Dessiner(g, Icone, RectangleF.Inflate(boite, -Dessin.Echelle(this, 10), -Dessin.Echelle(this, 10)), icone);
        var gauche = (int)(boite.Right + Dessin.Echelle(this, 12));
        var nombre = Theme.PoliceGrasse(15f);
        var libelle = Theme.PoliceTitre(9.75f);
        var hNombre = TextRenderer.MeasureText(g, "0", nombre, Size.Empty, TextFormatFlags.NoPadding).Height;
        var hLibelle = TextRenderer.MeasureText(g, Text, libelle, Size.Empty, TextFormatFlags.NoPadding).Height;
        var haute = (int)((Height - hNombre - hLibelle) / 2f);
        var couleurNombre = _selectionne ? Theme.TexteSurAccent : Theme.Texte;
        Dessin.Texte(g, _compte?.ToString(Core.Modeles.Libelles.Culture) ?? "", nombre, new Rectangle(gauche, haute, Width - gauche - 6, hNombre), couleurNombre);
        Dessin.Texte(g, Text, libelle, new Rectangle(gauche, haute + hNombre, Width - gauche - 6, hLibelle), texte);
    }
}

/// <summary>Fil d'Ariane "Sorties › Nouvelle sortie" : le premier élément est un lien.</summary>
public sealed class FilAriane : FlowLayoutPanel
{
    private readonly Label _courant;

    public FilAriane(string lien, Action surLien)
    {
        FlowDirection = FlowDirection.LeftToRight;
        WrapContents = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Color.Transparent;
        Margin = new Padding(0, 0, 0, 6);
        var bouton = new BoutonArrondi(lien, StyleBouton.Lien) { Margin = new Padding(0), Font = Theme.PoliceTitre(9.75f) };
        bouton.Click += (_, _) => surLien();
        var separateur = Ui.Libelle("›", Theme.Police(10.5f), Theme.TexteSecondaire);
        separateur.Margin = new Padding(4, 12, 4, 0);
        _courant = Ui.Libelle("", Theme.Police(9.75f), Theme.TexteSecondaire);
        _courant.Margin = new Padding(0, 13, 0, 0);
        Controls.AddRange([bouton, separateur, _courant]);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Courant
    {
        get => _courant.Text;
        set => _courant.Text = value;
    }
}

/// <summary>Barre de recherche arrondie : loupe, zone de texte, indication "ou scannez".</summary>
public sealed class BarreRecherche : Panel
{
    private readonly Pastille _indication;

    public BarreRecherche(string texteIndicatif, string indication)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        Margin = Padding.Empty;
        Champ.BorderStyle = BorderStyle.None;
        Champ.BackColor = Theme.Surface;
        Champ.ForeColor = Theme.Texte;
        Champ.Font = Theme.Police(11.25f);
        Champ.PlaceholderText = texteIndicatif;
        Champ.AccessibleName = "Rechercher";
        _indication = new Pastille(indication, Theme.Surface, Theme.TexteSecondaire, police: Theme.Police(9f), hauteur: 26) { Bordure = Theme.BordureForte };
        Controls.Add(Champ);
        Controls.Add(_indication);
        Champ.Enter += (_, _) => Invalidate();
        Champ.Leave += (_, _) => Invalidate();
        Click += (_, _) => Champ.Focus();
        Height = 54;
    }

    // Initialisé avant le constructeur : OnLayout peut être appelé dès le premier changement de taille.
    public TextBox Champ { get; } = new();

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_indication is null)
        {
            return;
        }

        var marge = (int)Dessin.Echelle(this, 18);
        var taille = _indication.GetPreferredSize(Size.Empty);
        _indication.SetBounds(Width - marge - taille.Width, (Height - taille.Height) / 2, taille.Width, taille.Height);
        var gauche = marge + (int)Dessin.Echelle(this, 32);
        Champ.SetBounds(gauche, (Height - Champ.Height) / 2, Math.Max(10, _indication.Left - gauche - marge), Champ.Height);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        Dessin.PeindreFondArrondi(this, g, BackColor, Champ.Focused ? Theme.Accent : Theme.BordureForte, 16);
        var t = Dessin.Echelle(this, 20);
        Icones.Dessiner(g, Icones.Recherche, new RectangleF(Dessin.Echelle(this, 18), (Height - t) / 2, t, t), Theme.TexteSecondaire, 1.9f);
    }
}
