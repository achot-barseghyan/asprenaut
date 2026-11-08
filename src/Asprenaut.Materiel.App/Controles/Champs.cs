using System.ComponentModel;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>Zone de texte thémée dans un cadre arrondi de 52 px (ou plus en multiligne).</summary>
public sealed class ChampSaisie : Panel
{
    /// <summary>Fond des champs : plus sombre que la carte qui les porte.</summary>
    public static readonly Color FondChamp = Theme.Sur(Theme.Surface, Theme.Fond, 0.6f);

    public ChampSaisie(bool multiligne = false)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = FondChamp;
        Height = multiligne ? 96 : Theme.HauteurChamp;
        Margin = Padding.Empty;
        Padding = new Padding(16, multiligne ? 14 : 0, 16, multiligne ? 14 : 0);
        Champ.BorderStyle = BorderStyle.None;
        Champ.BackColor = FondChamp;
        Champ.ForeColor = Theme.Texte;
        Champ.Font = Theme.Police(11.25f);
        Champ.Multiline = multiligne;
        Champ.ScrollBars = multiligne ? ScrollBars.Vertical : ScrollBars.None;
        if (multiligne)
        {
            Champ.Dock = DockStyle.Fill;
            Natif.ScrollbarsSombres(Champ);
        }

        Controls.Add(Champ);
        Champ.Enter += (_, _) => Invalidate();
        Champ.Leave += (_, _) => Invalidate();
        Click += (_, _) => Champ.Focus();
    }

    // Initialisé avant le constructeur : OnLayout peut être appelé dès le premier changement de taille.
    public TextBox Champ { get; } = new();

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Valeur
    {
        get => Champ.Text;
        set => Champ.Text = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string TexteIndicatif
    {
        get => Champ.PlaceholderText;
        set => Champ.PlaceholderText = value;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (!Champ.Multiline)
        {
            // Centre verticalement la zone de texte mono-ligne.
            Champ.SetBounds(Padding.Left, (Height - Champ.Height) / 2, Width - Padding.Horizontal, Champ.Height);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e) =>
        Dessin.PeindreFondArrondi(this, e.Graphics, BackColor, Champ.Focused ? Theme.Accent : Theme.BordureForte, Theme.RayonBouton, 1.2f);
}

/// <summary>Liste déroulante thémée (sélection seule), d'environ 52 px de haut.</summary>
public sealed class ListeDeroulante : ComboBox
{
    public ListeDeroulante()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        BackColor = ChampSaisie.FondChamp;
        ForeColor = Theme.Texte;
        Font = Theme.Police(11.25f);
        ItemHeight = 44;
        Margin = Padding.Empty;
        Cursor = Cursors.Hand;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ItemHeight = (int)Dessin.Echelle(this, 44); // La hauteur de la liste suit celle des éléments.
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var selectionne = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        using (var fond = new SolidBrush(selectionne ? Theme.SurfaceActive : ChampSaisie.FondChamp))
        {
            e.Graphics.FillRectangle(fond, e.Bounds);
        }

        if (e.Index >= 0)
        {
            var zone = Rectangle.FromLTRB(e.Bounds.Left + 14, e.Bounds.Top, e.Bounds.Right, e.Bounds.Bottom);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, zone, Theme.Texte,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }
}

/// <summary>
/// Champ de date sombre : saisie "jj/mm/aaaa", jour de la semaine en français, flèches jour précédent / suivant.
/// (Le DateTimePicker de Windows reste blanc et suit la langue du système.)
/// </summary>
public sealed class ChampDate : Panel
{
    private static readonly string[] Formats = ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "ddMMyyyy"];
    private readonly TextBox _texte = new();
    private readonly Label _jour = Ui.Libelle("", Theme.Police(10.5f), Theme.TexteSecondaire);
    private readonly BoutonArrondi _precedent = new("Jour précédent", StyleBouton.Icone, Icones.ChevronGauche) { Margin = Padding.Empty };
    private readonly BoutonArrondi _suivant = new("Jour suivant", StyleBouton.Icone, Icones.ChevronDroite) { Margin = Padding.Empty };
    private DateOnly? _date;

    public ChampDate()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = ChampSaisie.FondChamp;
        Height = Theme.HauteurChamp;
        Margin = Padding.Empty;
        _texte.BorderStyle = BorderStyle.None;
        _texte.BackColor = ChampSaisie.FondChamp;
        _texte.ForeColor = Theme.Texte;
        _texte.Font = Theme.Police(11.25f);
        _texte.PlaceholderText = "jj/mm/aaaa";
        _texte.AccessibleName = "Date";
        _jour.AutoSize = false;
        _jour.TextAlign = ContentAlignment.MiddleRight;
        _precedent.Click += (_, _) => Decaler(-1);
        _suivant.Click += (_, _) => Decaler(1);
        _texte.TextChanged += (_, _) => Lire();
        _texte.Leave += (_, _) =>
        {
            if (_date is { } d)
            {
                _texte.Text = d.ToString("dd/MM/yyyy", Libelles.Culture);
            }
        };
        _texte.Enter += (_, _) => Invalidate();
        _texte.Leave += (_, _) => Invalidate();
        Click += (_, _) => _texte.Focus();
        Controls.AddRange([_texte, _jour, _precedent, _suivant]);
    }

    /// <summary>Date saisie, ou null si le texte n'est pas une date.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DateOnly? Date
    {
        get => _date;
        set
        {
            _texte.Text = value?.ToString("dd/MM/yyyy", Libelles.Culture) ?? "";
            Lire();
        }
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_suivant is null)
        {
            return;
        }

        var marge = (int)Dessin.Echelle(this, 4);
        var cote = Height - (2 * marge);
        _suivant.SetBounds(Width - marge - cote, marge, cote, cote);
        _precedent.SetBounds(_suivant.Left - cote, marge, cote, cote);
        var largeurJour = (int)Dessin.Echelle(this, 130);
        _jour.SetBounds(_precedent.Left - largeurJour - marge, 0, largeurJour, Height);
        var gauche = (int)Dessin.Echelle(this, 16);
        _texte.SetBounds(gauche, (Height - _texte.Height) / 2, Math.Max(20, _jour.Left - gauche), _texte.Height);
    }

    protected override void OnPaintBackground(PaintEventArgs e) =>
        Dessin.PeindreFondArrondi(this, e.Graphics, BackColor, _texte.Focused ? Theme.Accent : Theme.BordureForte, Theme.RayonBouton, 1.2f);

    private void Decaler(int jours)
    {
        Date = (_date ?? DateOnly.FromDateTime(DateTime.Today)).AddDays(jours);
    }

    private void Lire()
    {
        var texte = _texte.Text.Trim().Replace('.', '/').Replace('-', '/');
        _date = DateOnly.TryParseExact(texte, Formats, Libelles.Culture, System.Globalization.DateTimeStyles.None, out var d) ? d : null;
        _jour.Text = _date?.ToString("dddd", Libelles.Culture) ?? (texte.Length == 0 ? "" : "date invalide");
        _jour.ForeColor = _date is null ? Theme.Erreur : Theme.TexteSecondaire;
    }
}

/// <summary>Élément de liste de choix : valeur + texte affiché.</summary>
public sealed record Choix<T>(T Valeur, string Texte)
{
    public override string ToString() => Texte;
}
