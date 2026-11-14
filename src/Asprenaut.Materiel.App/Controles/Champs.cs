using System.ComponentModel;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>Zone de texte thémée dans un cadre arrondi de 52 px (ou plus en multiligne).</summary>
public sealed class ChampSaisie : Panel
{
    /// <summary>Fond des champs : plus sombre que la carte qui les porte.</summary>
    public static Color FondChamp => Theme.Sur(Theme.Surface, Theme.Fond, 0.6f);

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
            Natif.ScrollbarsDuTheme(Champ);
        }

        Controls.Add(Champ);
        Champ.Enter += (_, _) => Invalidate();
        Champ.Leave += (_, _) => Invalidate();
        Click += (_, _) => Champ.Focus();
    }

    // Initialisé avant le constructeur : OnLayout peut être appelé dès le premier changement de taille.
    public TextBox Champ { get; } = new ZoneTexte();

    /// <summary>
    /// Texte indicatif gardé quand le champ a le focus (« Rechercher un adhérent… » sous le curseur, comme la maquette) :
    /// Windows l'efface dès que le champ est actif.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IndicatifSiFocus
    {
        get => ((ZoneTexte)Champ).IndicatifSiFocus;
        set => ((ZoneTexte)Champ).IndicatifSiFocus = value;
    }

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

    /// <summary>Pictogramme à gauche du texte (champ mono-ligne), ou null.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Icone { get; set; }

    private Control? _aDroite;

    /// <summary>Petit contrôle placé à droite dans le champ mono-ligne (ex. pastille « Prêt à scanner »), ou null.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Control? ADroite
    {
        get => _aDroite;
        set
        {
            if (_aDroite is not null)
            {
                Controls.Remove(_aDroite);
            }

            _aDroite = value;
            if (value is not null)
            {
                Controls.Add(value);
            }

            PerformLayout();
        }
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (!Champ.Multiline)
        {
            // Centre verticalement la zone de texte mono-ligne.
            var icone = Icone is null ? 0 : (int)Dessin.Echelle(this, 30);
            var droite = 0;
            if (_aDroite is { Visible: true } accessoire)
            {
                var taille = accessoire.GetPreferredSize(Size.Empty);
                droite = taille.Width + (int)Dessin.Echelle(this, 8);
                accessoire.SetBounds(Width - (int)Dessin.Echelle(this, 10) - taille.Width, (Height - taille.Height) / 2, taille.Width, taille.Height);
            }

            Champ.SetBounds(Padding.Left + icone, (Height - Champ.Height) / 2, Width - Padding.Horizontal - icone - droite, Champ.Height);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Dessin.PeindreFondArrondi(this, e.Graphics, BackColor, Champ.Focused ? Theme.Accent : Theme.BordureForte, Theme.RayonBouton, 1.2f);
        if (Icone is { } icone && !Champ.Multiline)
        {
            var t = Dessin.Echelle(this, 20);
            Icones.Dessiner(e.Graphics, icone, new RectangleF(Padding.Left, (Height - t) / 2, t, t), Theme.AccentClair);
        }
    }
}

/// <summary>Zone de texte d'un <see cref="ChampSaisie"/> : peut garder son texte indicatif quand elle a le focus.</summary>
internal sealed class ZoneTexte : TextBox
{
    private bool _indicatifSiFocus;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IndicatifSiFocus
    {
        get => _indicatifSiFocus;
        set
        {
            _indicatifSiFocus = value;
            Invalidate();
        }
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (_indicatifSiFocus && TextLength <= 1)
        {
            Invalidate(); // Premier caractère tapé ou dernier effacé : l'indicatif disparaît ou revient.
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        const int WmPaint = 0x000F;
        if (m.Msg == WmPaint && _indicatifSiFocus && Focused && TextLength == 0 && PlaceholderText.Length > 0 && IsHandleCreated)
        {
            using var g = Graphics.FromHwnd(Handle);
            TextRenderer.DrawText(g, PlaceholderText, Font, new Rectangle(1, 0, ClientSize.Width - 1, ClientSize.Height), Theme.TexteSecondaire,
                TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
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

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        const int WmPaint = 0x000F;
        if (m.Msg == WmPaint && IsHandleCreated)
        {
            using var g = Graphics.FromHwnd(Handle);
            PeindreCadre(g);
        }
    }

    /// <summary>
    /// Par-dessus le dessin de Windows (cadre et bouton clairs en style Flat) : coins arrondis, bordure du thème,
    /// bouton sombre et chevron, comme les champs de saisie.
    /// </summary>
    private void PeindreCadre(Graphics g)
    {
        Dessin.Qualite(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var chemin = Dessin.Arrondi(r, Dessin.Echelle(this, Theme.RayonBouton));
        using (var exterieur = new Region(ClientRectangle))
        using (var parent = new SolidBrush(Dessin.CouleurParent(this)))
        {
            exterieur.Exclude(chemin);
            g.FillRegion(parent, exterieur);
        }

        var largeurBouton = Dessin.Echelle(this, 34);
        using (var interieur = new Region(chemin))
        using (var fond = new SolidBrush(BackColor))
        {
            interieur.Intersect(new RectangleF(Width - largeurBouton - 2, 0, largeurBouton + 2, Height));
            g.FillRegion(fond, interieur);
        }

        var t = Dessin.Echelle(this, 18);
        Icones.Dessiner(g, Icones.ChevronBas, new RectangleF(Width - largeurBouton + ((largeurBouton - t) / 2) - Dessin.Echelle(this, 4), (Height - t) / 2, t, t),
            Enabled ? Theme.TexteSecondaireClair : Theme.TexteSecondaire, 2f);
        using var bord = new Pen(Focused || DroppedDown ? Theme.Accent : Theme.BordureForte, Dessin.Echelle(this, 1.2f));
        g.DrawPath(bord, chemin);
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
/// Champ de date sombre : saisie "jj/mm/aaaa" ou calendrier déroulant (<see cref="Calendrier"/> : bouton, Alt + flèche bas
/// ou F4), jour de la semaine en français. (Le DateTimePicker de Windows reste blanc et suit la langue du système.)
/// </summary>
public sealed class ChampDate : Panel
{
    private static readonly string[] Formats = ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "ddMMyyyy"];
    private readonly TextBox _texte = new();
    private readonly Label _jour = Ui.Libelle("", Theme.Police(10.5f), Theme.TexteSecondaire);
    private readonly BoutonArrondi _calendrier = new("Choisir dans le calendrier", StyleBouton.Icone, Icones.Agenda) { Margin = Padding.Empty };
    private ToolStripDropDown? _ouvert;
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
        _calendrier.Click += (_, _) => OuvrirCalendrier();
        _texte.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F4 || (e.Alt && e.KeyCode == Keys.Down))
            {
                e.Handled = e.SuppressKeyPress = true;
                OuvrirCalendrier();
            }
        };
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
        Controls.AddRange([_texte, _jour, _calendrier]);
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

    /// <summary>Vrai si le champ est vide ou contient une date lisible (faux pour une saisie incorrecte).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Valide => _date is not null || _texte.Text.Trim().Length == 0;

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        _texte.BackColor = Enabled ? ChampSaisie.FondChamp : Theme.Surface;
        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_calendrier is null)
        {
            return;
        }

        var marge = (int)Dessin.Echelle(this, 4);
        var cote = Height - (2 * marge);
        _calendrier.SetBounds(Width - marge - cote, marge, cote, cote);
        // Champ étroit : le jour de la semaine laisse la place à la date.
        var largeurJour = Width >= Dessin.Echelle(this, 330) ? (int)Dessin.Echelle(this, 130) : 0;
        _jour.Visible = largeurJour > 0;
        _jour.SetBounds(_calendrier.Left - largeurJour - marge, 0, largeurJour, Height);
        var gauche = (int)Dessin.Echelle(this, 16);
        _texte.SetBounds(gauche, (Height - _texte.Height) / 2, Math.Max(20, _jour.Left - gauche), _texte.Height);
    }

    protected override void OnPaintBackground(PaintEventArgs e) =>
        Dessin.PeindreFondArrondi(this, e.Graphics, BackColor, _texte.Focused || _ouvert is not null ? Theme.Accent : Theme.BordureForte, Theme.RayonBouton, 1.2f);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ouvert?.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Calendrier sous le champ, sur la date saisie (ou aujourd'hui) ; « Effacer » vide le champ.</summary>
    private void OuvrirCalendrier()
    {
        if (_ouvert is not null || !Enabled)
        {
            return;
        }

        _ouvert = Calendrier.Ouvrir(this, _date, d =>
        {
            Date = d;
            _texte.Focus();
            _texte.SelectionStart = _texte.TextLength;
        });
        _ouvert.Closed += (_, _) =>
        {
            _ouvert = null;
            Invalidate();
        };
        Invalidate();
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
