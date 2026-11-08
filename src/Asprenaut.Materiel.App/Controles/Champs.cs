using System.ComponentModel;
using Asprenaut.Materiel.App.Apparence;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>Zone de texte thémée dans un cadre arrondi de 44 px (ou plus en multiligne).</summary>
public sealed class ChampSaisie : Panel
{
    public ChampSaisie(bool multiligne = false)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Fond;
        Height = multiligne ? 100 : Theme.HauteurCliquable;
        Margin = new Padding(0, 0, 0, 12);
        Padding = new Padding(12, multiligne ? 10 : 0, 12, multiligne ? 10 : 0);
        Champ.BorderStyle = BorderStyle.None;
        Champ.BackColor = Theme.Fond;
        Champ.ForeColor = Theme.Texte;
        Champ.Font = Theme.Police(11f);
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
        Dessin.PeindreFondArrondi(this, e.Graphics, BackColor, Champ.Focused ? Theme.Accent : Theme.BordureForte, Theme.RayonBouton, 1.5f);
}

/// <summary>Liste déroulante thémée (sélection seule), éléments de 44 px.</summary>
public sealed class ListeDeroulante : ComboBox
{
    public ListeDeroulante()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        BackColor = Theme.Fond;
        ForeColor = Theme.Texte;
        Font = Theme.Police(11f);
        ItemHeight = 36;
        Margin = new Padding(0, 0, 0, 12);
        Cursor = Cursors.Hand;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var selectionne = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        using (var fond = new SolidBrush(selectionne ? Theme.SurfaceActive : Theme.Fond))
        {
            e.Graphics.FillRectangle(fond, e.Bounds);
        }

        if (e.Index >= 0)
        {
            var zone = Rectangle.FromLTRB(e.Bounds.Left + 10, e.Bounds.Top, e.Bounds.Right, e.Bounds.Bottom);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, zone, Theme.Texte,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }
}

/// <summary>Élément de liste de choix : valeur + texte affiché.</summary>
public sealed record Choix<T>(T Valeur, string Texte)
{
    public override string ToString() => Texte;
}
