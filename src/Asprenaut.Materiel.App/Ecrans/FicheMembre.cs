using System.ComponentModel;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.App.Ecrans;

// Maquette « Fiche d'un membre » (Membres du club › Adhérents) : en-tête fixe (pastille, nom, rôles, licence, menu « ⋮ »,
// bandeau d'état, trois chiffres), corps défilant (dehors en ce moment, non rendu, sorties), pied fixe (Modifier, Créer une sortie).

/// <summary>Pastille de l'en-tête : « Enseignant E2 », « Inspecteur TIV »…</summary>
internal sealed record PastilleMembre(string Texte, Color Couleur);

/// <summary>Ligne de texte sous le nom : <paramref name="Libelle"/> puis, en police à chasse fixe, <paramref name="Code"/>.</summary>
internal sealed record LigneInfoMembre(string Libelle, Color Couleur, string? Code = null, bool Gras = false);

/// <summary>Un des trois chiffres de l'en-tête (« 3 dehors en ce moment »).</summary>
internal sealed record ChiffreMembre(int Valeur, string Legende, Color Couleur);

/// <summary>
/// En-tête dessiné de la fiche : pastille carrée arrondie (initiales), nom, pastilles de rôle (à la ligne si besoin), lignes
/// d'information, bouton « Plus d'actions » en haut à droite, bandeau « Tout est rentré » / « 3 équipements dehors », trois
/// chiffres ; filet en bas sur toute la largeur. Sa hauteur suit sa largeur (pastilles à la ligne).
/// </summary>
internal sealed class EnTeteMembre : ControleDessine
{
    private const int Marge = 22, Avatar = 64, Bouton = 40;

    private readonly string _initiales;
    private readonly (Color Clair, Color Fonce) _degrade;
    private readonly bool _actif;
    private readonly string _nom;
    private readonly IReadOnlyList<PastilleMembre> _pastilles;
    private readonly IReadOnlyList<LigneInfoMembre> _lignes;
    private readonly BilanMembre _bilan;
    private readonly IReadOnlyList<ChiffreMembre> _chiffres;

    public EnTeteMembre(Adherent a, (Color Clair, Color Fonce) degrade, IReadOnlyList<PastilleMembre> pastilles,
        IReadOnlyList<LigneInfoMembre> lignes, BilanMembre bilan, IReadOnlyList<ChiffreMembre> chiffres)
    {
        Actions = new BoutonArrondi("", StyleBouton.Secondaire, Icones.PlusActions)
        {
            AutoSize = false,
            Size = new Size(Bouton, Bouton),
            Margin = Padding.Empty,
            AccessibleName = "Plus d'actions",
            AfficherPastille = false,
        };
        Controls.Add(Actions);
        _initiales = a.Initiales;
        _degrade = degrade;
        _actif = a.Actif;
        _nom = a.NomComplet;
        _pastilles = pastilles;
        _lignes = lignes;
        _bilan = bilan;
        _chiffres = chiffres;
        Height = 260;
        AccessibleRole = AccessibleRole.Grouping;
        AccessibleName = $"{a.NomComplet}{string.Concat(pastilles.Select(p => $", {p.Texte}"))}. {bilan.Message}. "
            + string.Join(", ", chiffres.Select(c => $"{c.Valeur} {c.Legende}"));
    }

    /// <summary>Bouton « ⋮ » : l'écran y accroche le menu (désactiver, réactiver).</summary>
    public BoutonArrondi Actions { get; }

    private static Font PoliceNom => Theme.PoliceGrasse(16.5f);

    private static Font PolicePastille => Theme.PoliceTitre(9f);

    private static Font PoliceLigne(bool gras) => gras ? Theme.PoliceTitre(9f) : Theme.Police(9f);

    /// <summary>Pastilles placées à la ligne dans la largeur donnée (coordonnées relatives au début du bloc).</summary>
    private List<RectangleF> PlacerPastilles(float largeur)
    {
        var zones = new List<RectangleF>();
        float x = 0, y = 0, h = E(24), ecart = E(6);
        foreach (var p in _pastilles)
        {
            var l = Math.Min(largeur, TextRenderer.MeasureText(p.Texte, PolicePastille, Size.Empty, TextFormatFlags.NoPadding).Width + E(20));
            if (x > 0 && x + l > largeur)
            {
                x = 0;
                y += h + ecart;
            }

            zones.Add(new RectangleF(x, y, l, h));
            x += l + ecart;
        }

        return zones;
    }

    private float GaucheTextes => E(Marge + Avatar + 16);

    private float LargeurTextes => Math.Max(E(60), Width - GaucheTextes - E(Marge + Bouton + 12));

    /// <summary>Hauteur du bloc nom + pastilles + lignes.</summary>
    private float HauteurTextes()
    {
        var h = E(30);
        if (_pastilles.Count > 0)
        {
            h += E(8) + PlacerPastilles(LargeurTextes).Max(z => z.Bottom);
        }

        if (_lignes.Count > 0)
        {
            h += E(8) + (_lignes.Count * E(20));
        }

        return h;
    }

    private float HautBandeau => Math.Max(E(Avatar), HauteurTextes()) + E(16);

    private int HauteurVoulue => (int)(HautBandeau + E(50) + E(12) + E(70) + E(18) + 1);

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        Actions.Location = new Point(Width - (int)E(Marge) - Actions.Width, 0);
        if (Width > 0 && Height != HauteurVoulue)
        {
            Height = HauteurVoulue;
        }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        PerformLayout();
    }

    protected override void Peindre(Graphics g)
    {
        var m = E(Marge);

        // Pastille carrée arrondie avec les initiales.
        EcranMoniteurs.Avatar.Dessiner(g, new RectangleF(m, 0, E(Avatar), E(Avatar)), _initiales, _degrade, _actif, true, this);

        // Nom, pastilles, lignes.
        var x = GaucheTextes;
        var l = LargeurTextes;
        Dessin.Texte(g, _nom, PoliceNom, new Rectangle((int)x, 0, (int)l, (int)E(30)), _actif ? Theme.Texte : Theme.TexteSecondaireClair);
        var y = E(30);
        if (_pastilles.Count > 0)
        {
            y += E(8);
            var zones = PlacerPastilles(l);
            for (var i = 0; i < zones.Count; i++)
            {
                var z = zones[i];
                z.Offset(x, y);
                var p = _pastilles[i];
                using (var chemin = Dessin.Arrondi(z, z.Height / 2))
                using (var pinceau = new SolidBrush(Theme.Teinte(p.Couleur, 0.14f)))
                {
                    g.FillPath(pinceau, chemin);
                }

                Dessin.Texte(g, p.Texte, PolicePastille, Rectangle.Round(z), p.Couleur, centre: true);
            }

            y += zones.Max(z => z.Bottom);
        }

        if (_lignes.Count > 0)
        {
            y += E(8);
            foreach (var ligne in _lignes)
            {
                var police = PoliceLigne(ligne.Gras);
                var zone = new Rectangle((int)x, (int)y, (int)l, (int)E(20));
                if (ligne.Code is { } code)
                {
                    var lib = TextRenderer.MeasureText(g, ligne.Libelle + " ", police, Size.Empty, TextFormatFlags.NoPadding).Width;
                    Dessin.Texte(g, ligne.Libelle, police, zone, ligne.Couleur);
                    Dessin.Texte(g, code, Theme.PoliceCode(9f), Rectangle.FromLTRB(zone.X + lib, zone.Y, zone.Right, zone.Bottom), Theme.TexteDoux);
                }
                else
                {
                    Dessin.Texte(g, ligne.Libelle, police, zone, ligne.Couleur);
                }

                y += E(20);
            }
        }

        // Bandeau d'état.
        var fond = Dessin.CouleurParent(this);
        var ok = _bilan.ToutRentre;
        var couleur = ok ? Theme.Retour : Theme.Depart;
        var texte = ok ? Theme.Retour : Theme.DepartClair;
        var bandeau = new RectangleF(m, HautBandeau, Width - (2 * m), E(50));
        using (var chemin = Dessin.Arrondi(bandeau, E(14)))
        {
            using var pinceau = new SolidBrush(Theme.Sur(fond, couleur, 0.10f));
            g.FillPath(pinceau, chemin);
            using var bord = new Pen(Theme.Sur(fond, couleur, ok ? 0.30f : 0.40f), E(1));
            g.DrawPath(bord, chemin);
        }

        var rond = new RectangleF(bandeau.X + E(14), bandeau.Y + E(10), E(30), E(30));
        using (var pinceau = new SolidBrush(couleur))
        {
            g.FillEllipse(pinceau, rond);
        }

        rond.Inflate(-E(6), -E(6));
        Icones.Dessiner(g, ok ? Icones.Coche : Icones.Exclamation, rond, ok ? Theme.TexteSurRetour : Theme.TexteSurDepart, 2.8f);
        var gaucheMessage = (int)(bandeau.X + E(56));
        Dessin.Texte(g, _bilan.Message, Theme.PoliceTitre(10.5f), Rectangle.FromLTRB(gaucheMessage, (int)bandeau.Y, (int)(bandeau.Right - E(14)), (int)bandeau.Bottom), texte);

        // Trois chiffres.
        var n = Math.Max(1, _chiffres.Count);
        var ecart = E(8);
        var lc = (Width - (2 * m) - ((n - 1) * ecart)) / n;
        var yc = bandeau.Bottom + E(12);
        for (var i = 0; i < _chiffres.Count; i++)
        {
            var c = _chiffres[i];
            var r = new RectangleF(m + (i * (lc + ecart)), yc, lc, E(70));
            using (var chemin = Dessin.Arrondi(r, E(14)))
            using (var pinceau = new SolidBrush(Theme.Sur(fond, Theme.Fond, 0.45f)))
            {
                g.FillPath(pinceau, chemin);
            }

            var xi = (int)(r.X + E(12));
            var li = (int)(r.Width - E(20));
            Dessin.Texte(g, c.Valeur.ToString(Libelles.Culture), Theme.PoliceGrasse(16.5f), new Rectangle(xi, (int)(r.Y + E(8)), li, (int)E(28)), c.Couleur);
            TextRenderer.DrawText(g, c.Legende, Theme.Police(8.25f), new Rectangle(xi, (int)(r.Y + E(37)), li, (int)E(30)), Theme.TexteSecondaireClair,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        // Filet sur toute la largeur.
        using var filet = new Pen(Theme.Sur(fond, Theme.AccentClair, 0.10f));
        g.DrawLine(filet, 0, Height - 1, Width, Height - 1);
    }
}

/// <summary>Une ligne d'une section de la fiche : équipement (nom, où, code) ou sortie (date, titre, type, état).</summary>
internal sealed record LigneMembre(string Titre, string Detail, Action? Ouvrir, string? Code = null, DateOnly? Date = null,
    string? Type = null, Color CouleurType = default, string? Etat = null, Color CouleurEtat = default);

/// <summary>Genre d'une section : matériel dehors (orange), non rendu (rouge), sorties (lignes avec la date en tuile).</summary>
internal enum GenreSectionMembre
{
    Dehors,
    NonRendu,
    Sorties,
}

/// <summary>
/// Section de la fiche, dessinée dans un seul contrôle : titre en capitales puis les lignes. Clic, ou flèches + Entrée, sur
/// une ligne = <see cref="LigneMembre.Ouvrir"/> (la sortie). Hauteur fixe d'après le nombre de lignes (pixels logiques).
/// </summary>
internal sealed class SectionMembre : Control
{
    private const int HauteurTitre = 30, HauteurEquipement = 60, EcartEquipement = 8, HauteurSortie = 62, HauteurVide = 34;

    private readonly string _titre;
    private readonly GenreSectionMembre _genre;
    private readonly IReadOnlyList<LigneMembre> _lignes;
    private readonly string _vide;
    private int _survol = -1;
    private int _choisie = -1;

    public SectionMembre(string titre, GenreSectionMembre genre, IReadOnlyList<LigneMembre> lignes, string vide = "")
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        _titre = titre;
        _genre = genre;
        _lignes = lignes;
        _vide = vide;
        TabStop = lignes.Any(l => l.Ouvrir is not null);
        Margin = new Padding(0, 0, 0, 20);
        Height = HauteurTitre + (lignes.Count == 0 ? HauteurVide : (lignes.Count * PasLogique) - Ecart);
        AccessibleRole = AccessibleRole.List;
        AccessibleName = $"{titre} : {(lignes.Count == 0 ? vide : string.Join(" ; ", lignes.Select(l => $"{l.Titre}, {l.Detail}{(l.Etat is null ? "" : $", {l.Etat}")}")))}";
    }

    /// <summary>Texte mesuré et dessiné sans les marges de TextRenderer : mesuré avec, dessiné sans, il était coupé (« M… »).</summary>
    private const TextFormatFlags TexteSerre = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.Left;

    private int Ecart => _genre == GenreSectionMembre.Sorties ? 0 : EcartEquipement;

    private int PasLogique => (_genre == GenreSectionMembre.Sorties ? HauteurSortie : HauteurEquipement) + Ecart;

    private float E(float v) => Dessin.Echelle(this, v);

    private RectangleF Zone(int i)
    {
        var h = _genre == GenreSectionMembre.Sorties ? HauteurSortie : HauteurEquipement;
        return new RectangleF(0, E(HauteurTitre) + (i * E(PasLogique)), Width - 1, E(h));
    }

    private int LigneSous(Point p)
    {
        for (var i = 0; i < _lignes.Count; i++)
        {
            if (Zone(i).Contains(p))
            {
                return i;
            }
        }

        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var i = LigneSous(e.Location);
        if (i != _survol)
        {
            _survol = i;
            Cursor = i >= 0 && _lignes[i].Ouvrir is not null ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _survol = -1;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && LigneSous(e.Location) is var i and >= 0)
        {
            _lignes[i].Ouvrir?.Invoke();
        }
    }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        if (_choisie < 0 && _lignes.Count > 0)
        {
            _choisie = 0;
        }

        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_lignes.Count == 0)
        {
            return;
        }

        if (e.KeyCode is Keys.Up or Keys.Down)
        {
            _choisie = Math.Clamp(_choisie + (e.KeyCode == Keys.Down ? 1 : -1), 0, _lignes.Count - 1);
            e.Handled = true;
            Invalidate();
        }
        else if (e.KeyCode is Keys.Enter or Keys.Space && _choisie >= 0)
        {
            e.Handled = true;
            _lignes[_choisie].Ouvrir?.Invoke();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Dessin.EffacerAvecParent(this, g);
        Dessin.Qualite(g);
        var fond = Dessin.CouleurParent(this);

        var couleurTitre = _genre switch
        {
            GenreSectionMembre.Dehors => Theme.DepartClair,
            GenreSectionMembre.NonRendu => Theme.Erreur,
            _ => Theme.TexteSecondaireClair,
        };
        Dessin.Texte(g, _titre.ToUpper(Libelles.Culture), Theme.PoliceTitre(9f), new Rectangle(0, 0, Width, (int)E(HauteurTitre - 8)), couleurTitre);

        if (_lignes.Count == 0)
        {
            Dessin.Texte(g, _vide, Theme.Police(9.75f), new Rectangle(0, (int)E(HauteurTitre), Width, (int)E(HauteurVide)), Theme.TexteSecondaireClair);
            return;
        }

        for (var i = 0; i < _lignes.Count; i++)
        {
            var r = Zone(i);
            if (r.Bottom < e.ClipRectangle.Top || r.Top > e.ClipRectangle.Bottom)
            {
                continue;
            }

            if (_genre == GenreSectionMembre.Sorties)
            {
                PeindreSortie(g, _lignes[i], r, fond, i == _survol);
            }
            else
            {
                PeindreEquipement(g, _lignes[i], r, fond, i == _survol);
            }

            if (Focused && ShowFocusCues && i == _choisie)
            {
                using var chemin = Dessin.Arrondi(RectangleF.Inflate(r, -1, -1), E(13));
                using var focus = new Pen(Theme.Accent, E(1.5f));
                g.DrawPath(focus, chemin);
            }
        }
    }

    private void PeindreEquipement(Graphics g, LigneMembre l, RectangleF r, Color fond, bool survol)
    {
        var couleur = _genre == GenreSectionMembre.NonRendu ? Theme.Erreur : Theme.Depart;
        using (var chemin = Dessin.Arrondi(r, E(14)))
        {
            using var pinceau = new SolidBrush(Theme.Sur(fond, couleur, survol ? 0.14f : 0.08f));
            g.FillPath(pinceau, chemin);
            using var bord = new Pen(Theme.Sur(fond, couleur, _genre == GenreSectionMembre.NonRendu ? 0.30f : 0.25f), E(1));
            g.DrawPath(bord, chemin);
        }

        var droite = (int)(r.Right - E(12));
        if (l.Code is { } code)
        {
            var police = Theme.PoliceCode(9.75f);
            var lc = TextRenderer.MeasureText(g, code, police, Size.Empty, TexteSerre).Width;
            TextRenderer.DrawText(g, code, police, new Rectangle(droite - lc, (int)r.Y, lc + 2, (int)r.Height), Theme.Accent, TexteSerre | TextFormatFlags.VerticalCenter);
            droite -= lc + (int)E(12);
        }

        var x = (int)(r.X + E(12));
        Dessin.Texte(g, l.Titre, Theme.PoliceTitre(10.5f), Rectangle.FromLTRB(x, (int)(r.Y + E(9)), droite, (int)(r.Y + E(31))), Theme.Texte);
        Dessin.Texte(g, l.Detail, Theme.Police(9f), Rectangle.FromLTRB(x, (int)(r.Y + E(31)), droite, (int)(r.Y + E(51))), Theme.TexteSecondaireClair);
    }

    private void PeindreSortie(Graphics g, LigneMembre l, RectangleF r, Color fond, bool survol)
    {
        if (survol && l.Ouvrir is not null)
        {
            using var chemin = Dessin.Arrondi(r, E(12));
            using var pinceau = new SolidBrush(Theme.Sur(fond, Theme.AccentClair, 0.06f));
            g.FillPath(pinceau, chemin);
        }

        // Date en tuile : jour, mois abrégé.
        var tuile = new RectangleF(r.X + E(8), r.Y + E(8), E(44), E(46));
        using (var chemin = Dessin.Arrondi(tuile, E(12)))
        using (var pinceau = new SolidBrush(Theme.Sur(fond, Theme.Accent, 0.08f)))
        {
            g.FillPath(pinceau, chemin);
        }

        if (l.Date is { } date)
        {
            Dessin.Texte(g, date.Day.ToString("00", Libelles.Culture), Theme.PoliceGrasse(12f),
                new Rectangle((int)tuile.X, (int)(tuile.Y + E(5)), (int)tuile.Width, (int)E(22)), Theme.Texte, centre: true);
            Dessin.Texte(g, date.ToString("MMM", Libelles.Culture).ToUpper(Libelles.Culture), Theme.PoliceTitre(7.5f),
                new Rectangle((int)tuile.X, (int)(tuile.Y + E(26)), (int)tuile.Width, (int)E(14)), Theme.AccentClair, centre: true);
        }

        // État à droite : point + texte dans une pastille.
        var droite = (int)(r.Right - E(8));
        if (l.Etat is { } etat)
        {
            var police = Theme.PoliceTitre(9f);
            var lt = TextRenderer.MeasureText(g, etat, police, Size.Empty, TextFormatFlags.NoPadding).Width;
            var pastille = new RectangleF(droite - lt - E(33), r.Y + ((r.Height - E(24)) / 2), lt + E(33), E(24));
            using (var chemin = Dessin.Arrondi(pastille, pastille.Height / 2))
            using (var pinceau = new SolidBrush(Theme.Sur(fond, l.CouleurEtat, 0.12f)))
            {
                g.FillPath(pinceau, chemin);
            }

            var d = E(7);
            using (var point = new SolidBrush(l.CouleurEtat))
            {
                g.FillEllipse(point, pastille.X + E(10), pastille.Y + ((pastille.Height - d) / 2), d, d);
            }

            Dessin.Texte(g, etat, police, Rectangle.FromLTRB((int)(pastille.X + E(23)), (int)pastille.Y, (int)pastille.Right, (int)pastille.Bottom), l.CouleurEtat);
            droite = (int)(pastille.X - E(10));
        }

        var x = (int)(tuile.Right + E(12));
        Dessin.Texte(g, l.Titre, Theme.PoliceTitre(10.5f), Rectangle.FromLTRB(x, (int)(r.Y + E(10)), droite, (int)(r.Y + E(32))), Theme.Texte);
        var y = (int)(r.Y + E(33));
        var bas = (int)(r.Y + E(53));
        var gauche = x;
        if (l.Type is { } type)
        {
            var police = Theme.PoliceTitre(9f);
            var lt = Math.Min(droite - x, TextRenderer.MeasureText(g, type, police, Size.Empty, TexteSerre).Width + 2);
            TextRenderer.DrawText(g, type, police, new Rectangle(x, y, lt, bas - y), l.CouleurType, TexteSerre | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            gauche = x + lt;
        }

        Dessin.Texte(g, l.Type is null ? l.Detail : $" · {l.Detail}", Theme.Police(9f), Rectangle.FromLTRB(gauche, y, droite, bas), Theme.TexteSecondaireClair);
    }
}

/// <summary>Pied fixe de la fiche : filet en haut sur toute la largeur, puis les boutons.</summary>
internal sealed class PiedFicheMembre : Panel
{
    public PiedFicheMembre(Control boutons)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        Padding = new Padding(22, 15, 22, 0);
        Height = 15 + Theme.HauteurBouton;
        boutons.Dock = DockStyle.Fill;
        Controls.Add(boutons);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var filet = new Pen(Theme.Sur(BackColor, Theme.AccentClair, 0.10f));
        e.Graphics.DrawLine(filet, 0, 0, Width, 0);
    }
}

/// <summary>Entrée du menu « Plus d'actions ».</summary>
internal sealed record ActionMenuMembre(string Icone, string Texte, Color Couleur, Action Executer);

/// <summary>
/// Menu « Plus d'actions » de la fiche, dessiné dans une fenêtre déroulante (<see cref="Ouvrir"/>) : un Button n'y émet pas
/// Click, l'action part au relâchement du clic ou sur Entrée.
/// </summary>
internal sealed class MenuMembre : Control
{
    private const int Largeur = 250, HauteurLigne = 44, Bord = 6;

    private readonly IReadOnlyList<ActionMenuMembre> _actions;
    private ToolStripDropDown? _fenetre;
    private int _choisie = -1;

    private MenuMembre(IReadOnlyList<ActionMenuMembre> actions, Control reference)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        _actions = actions;
        Size = new Size((int)Dessin.Echelle(reference, Largeur), (int)Dessin.Echelle(reference, (2 * Bord) + (actions.Count * HauteurLigne)));
        BackColor = Theme.Sur(Theme.Surface, Theme.AccentClair, 0.05f);
        AccessibleRole = AccessibleRole.MenuPopup;
        AccessibleName = "Plus d'actions";
    }

    /// <summary>Ouvre le menu sous <paramref name="bouton"/>, aligné sur son bord droit.</summary>
    public static void Ouvrir(Control bouton, IReadOnlyList<ActionMenuMembre> actions)
    {
        var menu = new MenuMembre(actions, bouton) { _choisie = 0 };
        var hote = new ToolStripControlHost(menu) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = menu.Size };
        var fenetre = new ToolStripDropDown { Padding = Padding.Empty, AutoClose = true, DropShadowEnabled = true, BackColor = Theme.BordureForte };
        fenetre.Items.Add(hote);
        menu._fenetre = fenetre;
        fenetre.Closed += (_, _) => fenetre.BeginInvoke(fenetre.Dispose);
        fenetre.Show(bouton, new Point(bouton.Width - menu.Width, bouton.Height + (int)Dessin.Echelle(bouton, 6)));
        menu.Focus();
    }

    private float E(float v) => Dessin.Echelle(this, v);

    private RectangleF Zone(int i) => new(E(Bord), E(Bord) + (i * E(HauteurLigne)), Width - (2 * E(Bord)), E(HauteurLigne));

    private void Executer(int i)
    {
        if (i < 0 || i >= _actions.Count)
        {
            return;
        }

        var action = _actions[i].Executer;
        _fenetre?.Close(ToolStripDropDownCloseReason.ItemClicked);
        action();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var i = Enumerable.Range(0, _actions.Count).FirstOrDefault(k => Zone(k).Contains(e.Location), -1);
        if (i >= 0 && i != _choisie)
        {
            _choisie = i;
            Invalidate();
        }

        Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left)
        {
            Executer(Enumerable.Range(0, _actions.Count).FirstOrDefault(k => Zone(k).Contains(e.Location), -1));
        }
    }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Up or Keys.Down)
        {
            _choisie = Math.Clamp(_choisie + (e.KeyCode == Keys.Down ? 1 : -1), 0, _actions.Count - 1);
            Invalidate();
        }
        else if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            Executer(_choisie);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Dessin.Qualite(g);
        using (var bord = new Pen(Theme.BordureForte))
        {
            g.DrawRectangle(bord, 0, 0, Width - 1, Height - 1);
        }

        for (var i = 0; i < _actions.Count; i++)
        {
            var a = _actions[i];
            var r = Zone(i);
            if (i == _choisie)
            {
                using var chemin = Dessin.Arrondi(r, E(10));
                using var pinceau = new SolidBrush(Theme.Sur(BackColor, Theme.AccentClair, 0.08f));
                g.FillPath(pinceau, chemin);
            }

            var t = E(17);
            Icones.Dessiner(g, a.Icone, new RectangleF(r.X + E(12), r.Y + ((r.Height - t) / 2), t, t), a.Couleur, 2f);
            Dessin.Texte(g, a.Texte, Theme.PoliceTitre(10.5f), Rectangle.FromLTRB((int)(r.X + E(39)), (int)r.Y, (int)r.Right, (int)r.Bottom), a.Couleur);
        }
    }
}
