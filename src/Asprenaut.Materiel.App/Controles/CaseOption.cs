using System.ComponentModel;
using Asprenaut.Materiel.App.Apparence;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>
/// Case à cocher dans un cadre (maquette « Types de sortie » : « Nom de l'adhérent à chaque scan ») : case, titre en gras,
/// texte d'aide dessous. Cochée, le cadre prend la teinte turquoise. Toute la carte est cliquable ; Espace au clavier.
/// La hauteur suit le texte d'aide (retour à la ligne) : à placer dans une ligne AutoSize.
/// </summary>
public sealed class CaseOption : CheckBox
{
    private const int MargeX = 16;
    private const int MargeY = 12;
    private const int Case = 22;
    private const int EcartTexte = 14;
    private string _aide;

    public CaseOption(string titre, string aide)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Appearance = Appearance.Button;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        AutoSize = false;
        Cursor = Cursors.Hand;
        Margin = Padding.Empty;
        Text = titre;
        _aide = aide;
        AccessibleName = titre;
        AccessibleDescription = aide;
        Height = 64;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Aide
    {
        get => _aide;
        set
        {
            _aide = value;
            AccessibleDescription = value;
            Invalidate();
        }
    }

    private float E(float v) => Dessin.Echelle(this, v);

    private static Font PoliceTitre => Theme.PoliceTitre(9.75f);

    private static Font PoliceAide => Theme.Police(9f);

    private const TextFormatFlags Drapeaux = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.Top;

    private int LargeurTexte(int largeur) => Math.Max(40, largeur - (int)E(MargeX + Case + EcartTexte + MargeX));

    /// <summary>Hauteur pour une largeur donnée : titre puis aide sur autant de lignes qu'il faut.</summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        var largeur = proposedSize.Width is > 0 and < int.MaxValue ? proposedSize.Width : Math.Max(Width, (int)E(320));
        var l = LargeurTexte(largeur);
        var hTitre = TextRenderer.MeasureText(Text, PoliceTitre, new Size(l, int.MaxValue), Drapeaux).Height;
        var hAide = _aide.Length == 0 ? 0 : TextRenderer.MeasureText(_aide, PoliceAide, new Size(l, int.MaxValue), Drapeaux).Height + (int)E(2);
        return new Size(largeur, Math.Max((int)E(Theme.HauteurCliquable), hTitre + hAide + (int)E(2 * MargeY)));
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        // La hauteur dépend de la largeur (retour à la ligne de l'aide) : on la corrige quand la largeur change.
        var voulue = GetPreferredSize(new Size(Width, 0)).Height;
        if (Height != voulue && Width > 0)
        {
            Height = voulue;
        }
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        base.OnCheckedChanged(e);
        AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        Dessin.EffacerAvecParent(this, g);
        Dessin.Qualite(g);
        var parent = Dessin.CouleurParent(this);
        var cadre = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        var fond = Checked ? Theme.Sur(parent, Theme.Accent, 0.12f) : Theme.Sur(parent, Theme.Fond, 0.45f);
        var bord = Checked ? Theme.Sur(parent, Theme.Accent, 0.55f) : Theme.Sur(parent, Theme.AccentClair, 0.14f);
        using (var chemin = Dessin.Arrondi(cadre, E(14)))
        using (var pinceau = new SolidBrush(fond))
        using (var stylo = new Pen(Focused && ShowFocusCues ? Theme.Accent : bord, Focused && ShowFocusCues ? E(2) : E(1)))
        {
            g.FillPath(pinceau, chemin);
            g.DrawPath(stylo, chemin);
        }

        // Case : pleine et cochée (turquoise), sinon simple contour.
        var c = E(Case);
        var boite = new RectangleF(E(MargeX), E(MargeY) + E(1), c, c);
        using (var chemin = Dessin.Arrondi(boite, E(6)))
        {
            if (Checked)
            {
                using var plein = new SolidBrush(Theme.Accent);
                g.FillPath(plein, chemin);
                Icones.Dessiner(g, Icones.Coche, RectangleF.Inflate(boite, -E(4), -E(4)), Theme.TexteSurAccent, 3f);
            }
            else
            {
                using var contour = new Pen(Theme.BordureForte, E(1.5f));
                g.DrawPath(contour, chemin);
            }
        }

        var x = (int)(boite.Right + E(EcartTexte));
        var l = LargeurTexte(Width);
        var hTitre = TextRenderer.MeasureText(g, Text, PoliceTitre, new Size(l, int.MaxValue), Drapeaux).Height;
        var y = (int)E(MargeY);
        TextRenderer.DrawText(g, Text, PoliceTitre, new Rectangle(x, y, l, hTitre), Theme.Texte, Drapeaux);
        if (_aide.Length > 0)
        {
            TextRenderer.DrawText(g, _aide, PoliceAide, new Rectangle(x, y + hTitre + (int)E(2), l, Height - y - hTitre), Theme.TexteSecondaireClair, Drapeaux);
        }
    }
}
