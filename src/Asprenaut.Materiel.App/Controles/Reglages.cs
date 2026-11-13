using System.ComponentModel;
using Asprenaut.Materiel.App.Apparence;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>Interrupteur marche / arrêt (piste verte, pastille blanche). Case à cocher accessible au clavier.</summary>
public sealed class Interrupteur : CheckBox, IVerrouillable
{
    private bool _verrouille;
    private PastilleVerrou? _pastille;

    /// <summary>Changer la valeur demande le code gestionnaire : pastille au cadenas sur le coin (GardeGestion.Lier).</summary>
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
            AccessibleDescription = value ? PastilleVerrou.TexteBulle : null;
            if (value || _pastille is not null)
            {
                _pastille ??= new PastilleVerrou(this);
                _pastille.Visible = value;
            }

            Invalidate();
        }
    }

    public Interrupteur(string nomAccessible)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Appearance = Appearance.Button;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        AutoSize = false;
        Size = new Size(60, Theme.HauteurCliquable);
        Margin = Padding.Empty;
        Cursor = Cursors.Hand;
        AccessibleName = nomAccessible;
        Text = "";
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        base.OnCheckedChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        Dessin.EffacerAvecParent(this, g);
        Dessin.Qualite(g);
        var l = Dessin.Echelle(this, 52);
        var h = Dessin.Echelle(this, 30);
        var piste = new RectangleF(Width - l - 1, (Height - h) / 2, l, h);
        using (var chemin = Dessin.Arrondi(piste, h / 2))
        using (var fond = new SolidBrush(Checked ? Theme.Retour : Theme.Teinte(Theme.AccentClair, 0.18f)))
        {
            g.FillPath(fond, chemin);
            if (Focused && ShowFocusCues)
            {
                using var focus = new Pen(Theme.Accent, Dessin.Echelle(this, 2));
                using var autour = Dessin.Arrondi(RectangleF.Inflate(piste, 3, 3), (h / 2) + 3);
                g.DrawPath(focus, autour);
            }
        }

        var d = Dessin.Echelle(this, 24);
        var marge = Dessin.Echelle(this, 3);
        var x = Checked ? piste.Right - marge - d : piste.Left + marge;
        using (var ombre = new SolidBrush(Theme.Alpha(Color.Black, 0.25f)))
        {
            g.FillEllipse(ombre, x, piste.Top + marge + 1.5f, d, d);
        }

        using (var pastille = new SolidBrush(Color.White))
        {
            g.FillEllipse(pastille, x, piste.Top + marge, d, d);
        }

        _pastille?.PeindreInterieur(g);
    }
}

/// <summary>
/// Sélecteur numérique « − 3 j + » dans un cadre arrondi. <see cref="IFondDessine"/> : les boutons − et + recopient
/// son fond, pastille « verrouillé » comprise.
/// </summary>
public sealed class SelecteurNombre : Panel, IVerrouillable, IFondDessine
{
    private readonly BoutonArrondi _moins;
    private readonly BoutonArrondi _plus;
    private readonly Label _valeur;
    private readonly Func<int, string> _format;
    private int _nombre;

    public SelecteurNombre(string nomAccessible, Func<int, string> format)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _format = format;
        BackColor = ChampSaisie.FondChamp;
        Margin = Padding.Empty;
        AccessibleName = nomAccessible;
        _moins = new BoutonArrondi($"{nomAccessible} : moins", StyleBouton.Icone, Icones.Moins) { Margin = Padding.Empty };
        _plus = new BoutonArrondi($"{nomAccessible} : plus", StyleBouton.Icone, Icones.Plus) { Margin = Padding.Empty };
        _valeur = Ui.Libelle("", Theme.PoliceGrasse(12f));
        _valeur.AutoSize = false;
        _valeur.TextAlign = ContentAlignment.MiddleCenter;
        _moins.Click += (_, _) => Demande?.Invoke(this, _nombre - 1);
        _plus.Click += (_, _) => Demande?.Invoke(this, _nombre + 1);
        Controls.AddRange([_moins, _valeur, _plus]);
        Size = new Size(44 + 72 + 44 + 10, 52);
    }

    private bool _verrouille;
    private PastilleVerrou? _pastille;

    /// <summary>
    /// Changer la valeur demande le code gestionnaire : bordure dorée et pastille au cadenas sur le coin
    /// (voir GardeGestion.Lier). Les clics lèvent toujours <see cref="Demande"/> : l'écran demande le code avant d'accepter.
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
            _moins.AccessibleDescription = _plus.AccessibleDescription = value ? PastilleVerrou.TexteBulle : null;
            if (value || _pastille is not null)
            {
                _pastille ??= new PastilleVerrou(this);
                _pastille.Visible = value;
            }

            Invalidate(true);
        }
    }

    /// <summary>L'utilisateur demande une nouvelle valeur ; l'écran la fait valider puis appelle <see cref="Nombre"/>.</summary>
    public event EventHandler<int>? Demande;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Nombre
    {
        get => _nombre;
        set
        {
            _nombre = value;
            _valeur.Text = _format(value);
            _valeur.AccessibleName = $"{AccessibleName} : {_format(value)}";
        }
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_plus is null)
        {
            return;
        }

        var m = (int)Dessin.Echelle(this, 4);
        var c = Height - (2 * m);
        _moins.SetBounds(m, m, c, c);
        _plus.SetBounds(Width - m - c, m, c, c);
        _valeur.SetBounds(_moins.Right, 0, _plus.Left - _moins.Right, Height);
    }

    protected override void OnPaintBackground(PaintEventArgs e) =>
        Dessin.PeindreFondArrondi(this, e.Graphics, BackColor,
            _verrouille ? Theme.Sur(BackColor, Theme.Avertissement, 0.6f) : Theme.BordureForte, Theme.RayonBouton, _verrouille ? 1.5f : 1.2f);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        _pastille?.PeindreInterieur(e.Graphics);
    }
}

/// <summary>
/// Carte d'une section de réglages : pictogramme teinté + titre, puis un contenu vertical.
/// Sa hauteur suit son contenu (lignes AutoSize d'un TableLayoutPanel).
/// </summary>
public sealed class CarteSection : Carte, IVerrouillable
{
    private readonly TableLayoutPanel _contenu;
    private readonly PastilleCodeRequis _codeRequis = new() { Visible = false };

    /// <summary>Réglages protégés en mode consultation : badge « Code requis » à côté du titre.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Verrouille
    {
        get => _codeRequis.Visible;
        set => _codeRequis.Visible = value;
    }

    public CarteSection(string titre, string? icone, Color couleurIcone)
    {
        Padding = new Padding(24);
        Margin = Padding.Empty;
        AutoSize = false;
        _contenu = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };
        _contenu.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(_contenu);
        AccessibleName = titre;

        var libelle = Ui.Libelle(titre, Theme.PoliceTitre(13.5f));
        if (icone is null)
        {
            libelle.Margin = new Padding(0, 0, 0, 14);
            Ajouter(libelle);
        }
        else
        {
            var picto = new Picto(icone, 42, couleurIcone, Theme.Teinte(couleurIcone, 0.14f)) { Margin = Padding.Empty };
            libelle.Margin = new Padding(12, 10, 0, 0);
            _codeRequis.Margin = new Padding(12, 9, 0, 0);
            var entete = Ui.Rangee(true, picto, libelle, _codeRequis);
            entete.Margin = new Padding(0, 0, 0, 18);
            Ajouter(entete);
        }

        _contenu.SizeChanged += (_, _) => Parent?.PerformLayout(this, nameof(Height));
    }

    /// <summary>Ajoute une ligne de contenu (pleine largeur).</summary>
    public T Ajouter<T>(T controle, bool pleineLargeur = true)
        where T : Control
    {
        _contenu.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (pleineLargeur)
        {
            controle.Dock = DockStyle.Fill;
        }

        _contenu.Controls.Add(controle, 0, _contenu.RowStyles.Count - 1);
        return controle;
    }

    /// <summary>Hauteur pour une largeur donnée : contenu + Padding.</summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        var largeur = proposedSize.Width is > 0 and < int.MaxValue ? proposedSize.Width : Width;
        var hauteur = _contenu.GetPreferredSize(new Size(Math.Max(1, largeur - Padding.Horizontal), 0)).Height;
        return new Size(largeur, hauteur + Padding.Vertical);
    }
}

/// <summary>
/// Ligne de réglage : libellé et explication à gauche, contrôle à droite ; filet au-dessus sauf pour la première.
/// </summary>
public sealed class LigneReglage : TableLayoutPanel
{
    private readonly bool _filet;

    public LigneReglage(string libelle, string? aide, Control controle, bool filet = true)
    {
        _filet = filet;
        ColumnCount = 2;
        RowCount = 1;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Color.Transparent;
        Margin = Padding.Empty;
        Padding = new Padding(0, 12, 0, 12);
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        var titre = Ui.Libelle(libelle, Theme.PoliceTitre(11.25f));
        titre.Margin = new Padding(0, 0, 0, 2);
        var textes = Ui.Pile(titre);
        if (aide is not null)
        {
            var explication = Ui.Libelle(aide, Theme.Police(9.75f), Theme.TexteSecondaire);
            explication.Margin = Padding.Empty;
            textes.Controls.Add(explication);
        }

        textes.Anchor = AnchorStyles.Left;
        textes.Margin = new Padding(0, 0, 16, 0);
        controle.Anchor = AnchorStyles.Right;
        controle.Margin = Padding.Empty;
        Controls.Add(textes, 0, 0);
        Controls.Add(controle, 1, 0);
        AccessibleName = libelle;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_filet)
        {
            using var stylo = new Pen(Theme.Sur(Theme.Surface, Theme.AccentClair, 0.08f));
            e.Graphics.DrawLine(stylo, 0, 0, Width, 0);
        }
    }
}
