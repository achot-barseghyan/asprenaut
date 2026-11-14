using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>
/// Calendrier d'un mois, dessiné aux couleurs du thème (le MonthCalendar de Windows reste blanc et suit la langue du système) :
/// mois et année précédents / suivants, semaine du lundi au dimanche, aujourd'hui entouré, date choisie en couleur,
/// « Aujourd'hui » et « Effacer » en bas. Souris ou clavier (flèches, Page précédente / suivante, Maj pour l'année, Entrée,
/// Échap). Ouvert sous un <see cref="ChampDate"/> par <see cref="Ouvrir"/>.
/// </summary>
public sealed class Calendrier : Control
{
    private const int Marge = 12, HauteurEnTete = 44, HauteurJours = 28, LargeurCase = 40, HauteurCase = 36, HauteurPied = 40, EcartPied = 8;

    private readonly float _echelle;
    private readonly DateOnly _aujourdhui = DateOnly.FromDateTime(DateTime.Today);
    private readonly DateOnly? _selection;
    private DateOnly _mois;
    private DateOnly _curseur;
    private string? _survol;
    private bool _clavier;

    private Calendrier(DateOnly? selection, float echelle)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        _echelle = echelle;
        _selection = selection;
        _curseur = selection ?? _aujourdhui;
        _mois = new DateOnly(_curseur.Year, _curseur.Month, 1);
        BackColor = Theme.Surface;
        Font = Theme.Police(10.5f);
        Cursor = Cursors.Hand;
        AccessibleName = "Calendrier";
        AccessibleRole = AccessibleRole.Table;
        Size = new Size(E((2 * Marge) + (7 * LargeurCase)), E(Marge + HauteurEnTete + HauteurJours + (6 * HauteurCase) + EcartPied + HauteurPied + Marge));
    }

    /// <summary>Une date a été choisie, ou effacée (null).</summary>
    private event EventHandler<DateOnly?>? Choisi;

    /// <summary>Fermé sans choix (Échap).</summary>
    private event EventHandler? Annule;

    /// <summary>
    /// Ouvre le calendrier sous <paramref name="champ"/> (au-dessus s'il n'y a pas la place), sur la date actuelle ou
    /// aujourd'hui. <paramref name="choisi"/> reçoit la date choisie, ou null pour « Effacer ».
    /// </summary>
    public static ToolStripDropDown Ouvrir(Control champ, DateOnly? date, Action<DateOnly?> choisi)
    {
        var calendrier = new Calendrier(date, Dessin.Echelle(champ, 1));
        var hote = new ToolStripControlHost(calendrier) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = calendrier.Size };
        var menu = new ToolStripDropDown { Padding = Padding.Empty, AutoClose = true, DropShadowEnabled = true, BackColor = Theme.BordureForte };
        menu.Items.Add(hote);
        calendrier.Choisi += (_, d) =>
        {
            menu.Close(ToolStripDropDownCloseReason.ItemClicked);
            choisi(d);
        };
        calendrier.Annule += (_, _) => menu.Close(ToolStripDropDownCloseReason.Keyboard);
        menu.Closed += (_, _) => champ.BeginInvoke(menu.Dispose);
        // Sous le champ ; au-dessus si l'écran n'a pas la place en dessous.
        var ecart = calendrier.E(4);
        var bas = champ.PointToScreen(new Point(0, champ.Height)).Y + ecart + calendrier.Height + 2;
        if (bas > Screen.FromControl(champ).WorkingArea.Bottom)
        {
            menu.Show(champ, new Point(0, -ecart), ToolStripDropDownDirection.AboveRight);
        }
        else
        {
            menu.Show(champ, new Point(0, champ.Height + ecart));
        }

        calendrier.Focus();
        return menu;
    }

    private int E(float v) => (int)Math.Round(v * _echelle);

    // ---------------------------------------------------------------- Zones cliquables

    private DateOnly PremiereCase => _mois.AddDays(-(((int)_mois.DayOfWeek + 6) % 7));

    private Rectangle Case(int index) =>
        new(E(Marge + (index % 7 * LargeurCase)), E(Marge + HauteurEnTete + HauteurJours + (index / 7 * HauteurCase)), E(LargeurCase), E(HauteurCase));

    private Rectangle BoutonEnTete(int rang)
    {
        // Rangs 0 et 1 à gauche (« année », ‹ mois), 2 et 3 à droite (mois ›, année »).
        var cote = E(34);
        var y = E(Marge) + ((E(HauteurEnTete) - cote) / 2);
        return rang < 2
            ? new Rectangle(E(Marge) + (rang * cote), y, cote, cote)
            : new Rectangle(Width - E(Marge) - ((4 - rang) * cote), y, cote, cote);
    }

    private Rectangle BoutonPied(bool droite)
    {
        var y = E(Marge + HauteurEnTete + HauteurJours + (6 * HauteurCase) + EcartPied);
        var largeur = (Width - E(2 * Marge) - E(8)) / 2;
        return new Rectangle(droite ? Width - E(Marge) - largeur : E(Marge), y, largeur, E(HauteurPied));
    }

    private static readonly string[] Cles = ["an-", "mois-", "mois+", "an+"];

    private string? Cible(Point p)
    {
        for (var i = 0; i < 4; i++)
        {
            if (BoutonEnTete(i).Contains(p))
            {
                return Cles[i];
            }
        }

        for (var i = 0; i < 42; i++)
        {
            if (Case(i).Contains(p))
            {
                return $"jour{i}";
            }
        }

        return BoutonPied(false).Contains(p) ? "auj" : BoutonPied(true).Contains(p) ? "eff" : null;
    }

    private void Agir(string cible)
    {
        switch (cible)
        {
            case "an-": AllerAu(_curseur.AddYears(-1)); break;
            case "mois-": AllerAu(_curseur.AddMonths(-1)); break;
            case "mois+": AllerAu(_curseur.AddMonths(1)); break;
            case "an+": AllerAu(_curseur.AddYears(1)); break;
            case "auj": Choisi?.Invoke(this, _aujourdhui); break;
            case "eff": Choisi?.Invoke(this, null); break;
            default:
                Choisi?.Invoke(this, PremiereCase.AddDays(int.Parse(cible[4..], Libelles.Culture)));
                break;
        }
    }

    /// <summary>Place le curseur sur une date et affiche son mois.</summary>
    private void AllerAu(DateOnly date)
    {
        _curseur = date;
        _mois = new DateOnly(date.Year, date.Month, 1);
        AccessibleDescription = date.ToString("dddd d MMMM yyyy", Libelles.Culture);
        Invalidate();
    }

    // ---------------------------------------------------------------- Souris et clavier

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var cible = Cible(e.Location);
        if (cible != _survol)
        {
            _survol = cible;
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _survol = null;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && Cible(e.Location) is { } cible)
        {
            _clavier = false;
            Agir(cible);
        }

        base.OnMouseUp(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End or Keys.Enter or Keys.Escape
        || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        _clavier = true;
        switch (e.KeyCode)
        {
            case Keys.Left: AllerAu(_curseur.AddDays(-1)); break;
            case Keys.Right: AllerAu(_curseur.AddDays(1)); break;
            case Keys.Up: AllerAu(_curseur.AddDays(-7)); break;
            case Keys.Down: AllerAu(_curseur.AddDays(7)); break;
            case Keys.PageUp: AllerAu(e.Shift ? _curseur.AddYears(-1) : _curseur.AddMonths(-1)); break;
            case Keys.PageDown: AllerAu(e.Shift ? _curseur.AddYears(1) : _curseur.AddMonths(1)); break;
            case Keys.Home: AllerAu(_mois); break;
            case Keys.End: AllerAu(_mois.AddMonths(1).AddDays(-1)); break;
            case Keys.Enter: Choisi?.Invoke(this, _curseur); break;
            case Keys.Escape: Annule?.Invoke(this, EventArgs.Empty); break;
            default: base.OnKeyDown(e); return;
        }

        e.Handled = true;
    }

    // ---------------------------------------------------------------- Dessin

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Dessin.Qualite(g);
        g.Clear(Theme.Surface);
        using (var bord = new Pen(Theme.BordureForte, 1))
        {
            g.DrawRectangle(bord, 0, 0, Width - 1, Height - 1);
        }

        // En-tête : « Novembre 2026 » entre les flèches.
        var titre = _mois.ToString("MMMM yyyy", Libelles.Culture);
        titre = char.ToUpper(titre[0], Libelles.Culture) + titre[1..];
        var zoneTitre = Rectangle.FromLTRB(BoutonEnTete(1).Right, E(Marge), BoutonEnTete(2).Left, E(Marge + HauteurEnTete));
        Dessin.Texte(g, titre, Theme.PoliceTitre(11.25f), zoneTitre, Theme.Texte, centre: true);
        string[] pictos = [Icones.ChevronsGauche, Icones.ChevronGauche, Icones.ChevronDroite, Icones.ChevronsDroite];
        for (var i = 0; i < 4; i++)
        {
            var r = BoutonEnTete(i);
            if (_survol == Cles[i])
            {
                RemplirArrondi(g, r, Theme.Sur(Theme.Surface, Theme.AccentClair, 0.14f));
            }

            var c = E(18);
            Icones.Dessiner(g, pictos[i], new RectangleF(r.X + ((r.Width - c) / 2f), r.Y + ((r.Height - c) / 2f), c, c), Theme.TexteSecondaireClair);
        }

        // Jours de la semaine.
        string[] jours = ["L", "M", "M", "J", "V", "S", "D"];
        for (var i = 0; i < 7; i++)
        {
            var r = new Rectangle(E(Marge + (i * LargeurCase)), E(Marge + HauteurEnTete), E(LargeurCase), E(HauteurJours));
            Dessin.Texte(g, jours[i], Theme.PoliceTitre(8.25f), r, Theme.TexteSecondaire, centre: true);
        }

        // Cases du mois (6 semaines), jours des mois voisins atténués.
        var premier = PremiereCase;
        for (var i = 0; i < 42; i++)
        {
            var jour = premier.AddDays(i);
            var r = Rectangle.Inflate(Case(i), -E(2), -E(2));
            var choisi = jour == _selection;
            var duMois = jour.Month == _mois.Month;
            if (choisi)
            {
                RemplirArrondi(g, r, Theme.Accent);
            }
            else if (_survol == $"jour{i}")
            {
                RemplirArrondi(g, r, Theme.Sur(Theme.Surface, Theme.AccentClair, 0.16f));
            }

            if (jour == _aujourdhui && !choisi)
            {
                using var anneau = new Pen(Theme.Accent, E(1.5f));
                using var chemin = Dessin.Arrondi(RectangleF.Inflate(r, -0.5f, -0.5f), E(10));
                g.DrawPath(anneau, chemin);
            }

            if (_clavier && jour == _curseur && Focused)
            {
                using var focus = new Pen(choisi ? Theme.TexteSurAccent : Theme.Texte, E(1.5f)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
                using var chemin = Dessin.Arrondi(RectangleF.Inflate(r, -E(3), -E(3)), E(8));
                g.DrawPath(focus, chemin);
            }

            var couleur = choisi ? Theme.TexteSurAccent : duMois ? Theme.Texte : Theme.TexteSecondaire;
            var police = choisi || jour == _aujourdhui ? Theme.PoliceTitre(10.5f) : Font;
            Dessin.Texte(g, jour.Day.ToString(Libelles.Culture), police, r, couleur, centre: true);
        }

        // Pied : Aujourd'hui | Effacer.
        PeindrePied(g, BoutonPied(false), "Aujourd'hui", "auj", Theme.AccentClair);
        PeindrePied(g, BoutonPied(true), "Effacer", "eff", Theme.TexteSecondaireClair);
    }

    private void PeindrePied(Graphics g, Rectangle r, string texte, string cle, Color couleur)
    {
        var fond = _survol == cle ? Theme.Sur(Theme.Surface, Theme.AccentClair, 0.14f) : Theme.Sur(Theme.Surface, Theme.Fond, 0.35f);
        RemplirArrondi(g, r, fond);
        Dessin.Texte(g, texte, Theme.PoliceTitre(9.75f), r, couleur, centre: true);
    }

    private void RemplirArrondi(Graphics g, Rectangle r, Color couleur)
    {
        using var chemin = Dessin.Arrondi(r, E(10));
        using var pinceau = new SolidBrush(couleur);
        g.FillPath(pinceau, chemin);
    }
}
