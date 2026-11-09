using Asprenaut.Materiel.App.Apparence;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>Un événement affiché dans le journal.</summary>
public sealed record LigneJournalAffichee(string Heure, string Titre, string Detail, string? Code, string Icone, Color Couleur);

/// <summary>Un jour du journal : « Hier · dimanche 8 novembre » et ses événements.</summary>
public sealed record JourJournal(string Titre, string Precision, IReadOnlyList<LigneJournalAffichee> Lignes);

/// <summary>
/// Journal de l'écran Historique : un en-tête et une carte par jour, une ligne par événement, avec le code
/// cliquable de l'équipement. Tout est dessiné dans un seul contrôle défilant (seule la partie visible est peinte) :
/// plusieurs milliers d'événements restent fluides, sans créer un contrôle par ligne.
/// </summary>
public sealed class JournalHistorique : ScrollableControl
{
    // Pixels logiques (96 dpi).
    private const int HauteurEnteteJour = 30;
    private const int EcartEntete = 8;
    private const int MargeCarte = 6;
    private const int HauteurLigne = 64;
    private const int EcartJours = 20;
    private const int MargeInterieure = 18;
    private const int HauteurVide = 96;

    private IReadOnlyList<JourJournal> _jours = [];
    private string _messageVide = "";
    private string? _survol;

    public JournalHistorique()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false); // Le champ de scan garde le clavier.
        AutoScroll = true;
        BackColor = Theme.Fond;
        Margin = Padding.Empty;
        AccessibleRole = AccessibleRole.List;
        AccessibleName = "Journal";
        Natif.ScrollbarsDuTheme(this);
    }

    /// <summary>Clic sur le code d'un équipement.</summary>
    public event EventHandler<string>? CodeChoisi;

    public void Afficher(IReadOnlyList<JourJournal> jours, string messageVide)
    {
        _jours = jours;
        _messageVide = messageVide;
        _survol = null;
        AutoScrollPosition = Point.Empty;
        AccessibleDescription = jours.Count == 0 ? messageVide : $"{jours.Sum(j => j.Lignes.Count)} événements";
        MettreAJourTaille();
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        MettreAJourTaille();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        MettreAJourTaille();
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var code = CodeSous(e.Location);
        if (code != _survol)
        {
            _survol = code;
            Cursor = code is null ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_survol is not null)
        {
            _survol = null;
            Invalidate();
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left && CodeSous(e.Location) is { } code)
        {
            CodeChoisi?.Invoke(this, code);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Dessin.Qualite(g);
        float E(float v) => Dessin.Echelle(this, v);
        var largeur = ClientSize.Width;
        var haut = e.ClipRectangle.Top;
        var bas = e.ClipRectangle.Bottom;

        if (_jours.Count == 0)
        {
            var r = new RectangleF(0.5f, 0.5f, largeur - 1.5f, E(HauteurVide));
            Carte(g, r);
            Dessin.Texte(g, _messageVide, Theme.Police(10.5f), Rectangle.Round(r), Theme.TexteSecondaire, centre: true);
            return;
        }

        float y = AutoScrollPosition.Y;
        foreach (var jour in _jours)
        {
            var hauteurJour = E(HauteurEnteteJour + EcartEntete + (2 * MargeCarte)) + (jour.Lignes.Count * E(HauteurLigne));
            if (y + hauteurJour >= haut && y <= bas)
            {
                PeindreJour(g, jour, y, largeur);
            }

            y += hauteurJour + E(EcartJours);
            if (y > bas)
            {
                break;
            }
        }
    }

    private void PeindreJour(Graphics g, JourJournal jour, float y, int largeur)
    {
        float E(float v) => Dessin.Echelle(this, v);

        // En-tête : « Hier » en gras, puis la date en gris.
        var titre = Theme.PoliceTitre(12f);
        var hEntete = (int)E(HauteurEnteteJour);
        // Mesure avec les mêmes options que le dessin (marges comprises), sinon le titre est coupé par « … ».
        var lTitre = TextRenderer.MeasureText(g, jour.Titre, titre).Width + (int)E(4);
        Dessin.Texte(g, jour.Titre, titre, new Rectangle(0, (int)y, lTitre, hEntete), Theme.Texte);
        if (jour.Precision.Length > 0)
        {
            var x = lTitre + (int)E(10);
            Dessin.Texte(g, jour.Precision, Theme.Police(9.75f), new Rectangle(x, (int)(y + E(1)), largeur - x, hEntete), Theme.TexteSecondaire);
        }

        // Carte du jour.
        var haut = y + E(HauteurEnteteJour + EcartEntete);
        var carte = new RectangleF(0.5f, haut, largeur - 1.5f, (2 * E(MargeCarte)) + (jour.Lignes.Count * E(HauteurLigne)));
        Carte(g, carte);

        var ly = haut + E(MargeCarte);
        using var filet = new Pen(Theme.Sur(Theme.Surface, Theme.AccentClair, 0.08f), Math.Max(1, E(1)));
        for (var i = 0; i < jour.Lignes.Count; i++)
        {
            if (i > 0)
            {
                g.DrawLine(filet, E(MargeInterieure), ly, largeur - E(MargeInterieure), ly);
            }

            PeindreLigne(g, jour.Lignes[i], ly, largeur);
            ly += E(HauteurLigne);
        }
    }

    private void PeindreLigne(Graphics g, LigneJournalAffichee l, float y, int largeur)
    {
        float E(float v) => Dessin.Echelle(this, v);
        var h = E(HauteurLigne);
        var x = E(MargeInterieure);

        // Heure.
        Dessin.Texte(g, l.Heure, Theme.Police(9.75f), new Rectangle((int)x, (int)y, (int)E(46), (int)h), Theme.TexteSecondaire);
        x += E(46 + 10);

        // Pictogramme dans un carré teinté de la couleur du type.
        var c = E(36);
        var boite = new RectangleF(x, y + ((h - c) / 2), c, c);
        using (var chemin = Dessin.Arrondi(boite, E(12)))
        using (var pinceau = new SolidBrush(Theme.Teinte(l.Couleur, 0.14f)))
        {
            g.FillPath(pinceau, chemin);
        }

        var t = E(18);
        Icones.Dessiner(g, l.Icone, new RectangleF(boite.X + ((c - t) / 2), boite.Y + ((c - t) / 2), t, t), l.Couleur, 2f);
        x += c + E(14);

        // Code de l'équipement, cliquable, à droite.
        var droite = largeur - E(MargeInterieure);
        if (l.Code is { } code)
        {
            var police = Theme.PoliceCode(9f);
            var puce = Puce(code, y, largeur);
            var survol = _survol == code;
            using (var chemin = Dessin.Arrondi(puce, E(10)))
            {
                using var pinceau = new SolidBrush(Theme.Teinte(Theme.Accent, survol ? 0.22f : 0.10f));
                g.FillPath(pinceau, chemin);
                using var bord = new Pen(Theme.Teinte(Theme.Accent, survol ? 0.55f : 0.28f), E(1));
                g.DrawPath(bord, chemin);
            }

            Dessin.Texte(g, code, police, Rectangle.Round(puce), Theme.Accent, centre: true);
            droite = puce.Left - E(12);
        }

        // Titre et détail.
        var largeurTexte = (int)Math.Max(10, droite - x);
        Dessin.Texte(g, l.Titre, Theme.PoliceTitre(10.5f), new Rectangle((int)x, (int)(y + E(10)), largeurTexte, (int)E(24)), Theme.Texte);
        Dessin.Texte(g, l.Detail, Theme.Police(9.75f), new Rectangle((int)x, (int)(y + E(33)), largeurTexte, (int)E(20)), Theme.TexteSecondaireClair);
    }

    private void Carte(Graphics g, RectangleF r)
    {
        using var chemin = Dessin.Arrondi(r, Dessin.Echelle(this, 22));
        using var pinceau = new SolidBrush(Theme.Surface);
        g.FillPath(pinceau, chemin);
        using var bord = new Pen(Theme.Bordure, Dessin.Echelle(this, 1));
        g.DrawPath(bord, chemin);
    }

    /// <summary>Pastille du code d'une ligne dont le haut est en <paramref name="y"/>.</summary>
    private RectangleF Puce(string code, float y, int largeur)
    {
        float E(float v) => Dessin.Echelle(this, v);
        var lc = TextRenderer.MeasureText(code, Theme.PoliceCode(9f), Size.Empty, TextFormatFlags.NoPadding).Width + E(20);
        var hc = E(32);
        return new RectangleF(largeur - E(MargeInterieure) - lc, y + ((E(HauteurLigne) - hc) / 2), lc, hc);
    }

    /// <summary>Code sous le pointeur : la ligne est retrouvée par le calcul (même géométrie que la peinture).</summary>
    private string? CodeSous(Point p)
    {
        float E(float v) => Dessin.Echelle(this, v);
        float y = AutoScrollPosition.Y;
        foreach (var jour in _jours)
        {
            var debutLignes = y + E(HauteurEnteteJour + EcartEntete + MargeCarte);
            var finLignes = debutLignes + (jour.Lignes.Count * E(HauteurLigne));
            if (p.Y >= debutLignes && p.Y < finLignes)
            {
                var index = (int)((p.Y - debutLignes) / E(HauteurLigne));
                var ligne = jour.Lignes[Math.Clamp(index, 0, jour.Lignes.Count - 1)];
                if (ligne.Code is not { } code)
                {
                    return null;
                }

                var puce = Puce(code, debutLignes + (index * E(HauteurLigne)), ClientSize.Width);
                // Zone de clic d'au moins 44 px de haut.
                var zone = RectangleF.Inflate(puce, E(4), Math.Max(0, (E(Theme.HauteurCliquable) - puce.Height) / 2));
                return zone.Contains(p) ? code : null;
            }

            y = finLignes + E(MargeCarte + EcartJours);
            if (y > p.Y)
            {
                return null;
            }
        }

        return null;
    }

    private void MettreAJourTaille()
    {
        float E(float v) => Dessin.Echelle(this, v);
        var total = _jours.Count == 0
            ? E(HauteurVide)
            : _jours.Sum(j => E(HauteurEnteteJour + EcartEntete + (2 * MargeCarte)) + (j.Lignes.Count * E(HauteurLigne))) + ((_jours.Count - 1) * E(EcartJours));
        AutoScrollMinSize = new Size(0, (int)Math.Ceiling(total) + 2);
    }
}
