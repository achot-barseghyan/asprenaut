using System.ComponentModel;
using System.Drawing.Drawing2D;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.App.Controles;

// Briques de la fenêtre « Modifier la sortie » (maquette) : carte d'en-tête, tuiles des types, cartes des responsables.

/// <summary>
/// En-tête de « Modifier la sortie » : calendrier teinté de la couleur du type (jour, mois), « MODIFIER LA SORTIE »,
/// intitulé, pastille du type et responsable · lieu. Tout est dessiné ; suit la saisie.
/// </summary>
public sealed class EnTeteSortie : ControleDessine
{
    private DateOnly? _date;
    private string _intitule = "";
    private string _type = "";
    private Color _couleur = Theme.Accent;
    private string _detail = "";

    public EnTeteSortie()
    {
        TabStop = false;
        Height = 70;
        AccessibleRole = AccessibleRole.StaticText;
    }

    /// <summary>Met à jour l'en-tête (valeurs en cours de saisie).</summary>
    public void Afficher(DateOnly? date, string intitule, string type, Color couleur, string detail)
    {
        _date = date;
        _intitule = intitule.Trim().Length > 0 ? intitule.Trim() : "Sans intitulé";
        _type = type;
        _couleur = couleur;
        _detail = detail;
        AccessibleName = $"Modifier la sortie : {_intitule}";
        Invalidate();
    }

    protected override void Peindre(Graphics g)
    {
        // Calendrier : 58 × 64, teinte du type.
        var cal = new RectangleF(0.5f, (Height - E(64)) / 2, E(58), E(64));
        using (var chemin = Dessin.Arrondi(cal, E(15)))
        {
            using var fond = new SolidBrush(Theme.Teinte(_couleur, 0.12f));
            g.FillPath(fond, chemin);
            using var bord = new Pen(Theme.Teinte(_couleur, 0.35f), E(1));
            g.DrawPath(bord, chemin);
        }

        var jour = _date is { } d ? d.Day.ToString("00", Libelles.Culture) : "--";
        var mois = _date is { } m ? m.ToString("MMM", Libelles.Culture).TrimEnd('.').ToUpper(Libelles.Culture) : "";
        Dessin.Texte(g, jour, Theme.PoliceGrasse(17f), Rectangle.Round(new RectangleF(cal.X, cal.Y + E(8), cal.Width, E(28))), Theme.Texte, centre: true);
        Dessin.Texte(g, mois, Theme.PoliceGrasse(8.25f), Rectangle.Round(new RectangleF(cal.X, cal.Y + E(37), cal.Width, E(16))), _couleur, centre: true);

        var x = cal.Right + E(16);
        var largeur = Width - x;
        Dessin.Texte(g, "MODIFIER LA SORTIE", Theme.PoliceGrasse(8.25f), Rectangle.Round(new RectangleF(x, 0, largeur, E(16))), Theme.TexteSecondaire);
        Dessin.Texte(g, _intitule, Theme.PoliceGrasse(16f), Rectangle.Round(new RectangleF(x, E(15), largeur, E(30))), Theme.Texte);

        // Pastille du type puis « Moniteur : … · lieu ».
        var police = Theme.PoliceGrasse(8.25f);
        var texteType = _type.ToUpper(Libelles.Culture);
        var lPastille = TextRenderer.MeasureText(texteType, police, Size.Empty, TextFormatFlags.NoPadding).Width + E(18);
        var pastille = new RectangleF(x, E(46), lPastille, E(22));
        using (var chemin = Dessin.Arrondi(pastille, pastille.Height / 2))
        using (var fond = new SolidBrush(Theme.Teinte(_couleur, 0.16f)))
        {
            g.FillPath(fond, chemin);
        }

        Dessin.Texte(g, texteType, police, Rectangle.Round(pastille), _couleur, centre: true);
        var xDetail = pastille.Right + E(8);
        Dessin.Texte(g, _detail, Theme.Police(9f), Rectangle.Round(new RectangleF(xDetail, pastille.Y, Width - xDetail, pastille.Height)), Theme.TexteSecondaireClair);
    }

    public override Size GetPreferredSize(Size proposedSize) => new(Math.Max(Width, proposedSize.Width), (int)E(70));
}

/// <summary>
/// Type de sortie en tuiles (pictogramme coloré + nom), 5 par ligne au plus ; la tuile choisie est teintée de sa couleur.
/// Clavier : flèches (choisissent directement). Un seul contrôle dessiné.
/// </summary>
public sealed class ChoixTypeSortie : ControleDessine
{
    private const int Ecart = 8;

    /// <summary>Un type ; <paramref name="Detail"/> facultatif sous le nom (« 1 utilisation »).</summary>
    public sealed record Element(long Id, string Nom, string Icone, Color Couleur, string? Detail = null);

    private readonly List<Element> _elements = [];
    private long? _valeur;
    private int _survol = -1;
    private int _largeurPlacee = -1;
    private int _hauteur = 66;
    private readonly List<RectangleF> _zones = [];

    public ChoixTypeSortie()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        AutoSize = true;
        Height = HauteurTuile;
        AccessibleName = "Type de sortie";
        AccessibleRole = AccessibleRole.List;
    }

    public void Remplir(IEnumerable<Element> elements)
    {
        _elements.Clear();
        _elements.AddRange(elements);
        _largeurPlacee = -1;
        PerformLayout();
        Invalidate();
    }

    /// <summary>Type choisi.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public long? Valeur
    {
        get => _valeur;
        set
        {
            if (_valeur == value)
            {
                return;
            }

            _valeur = value;
            AccessibleDescription = Choisi?.Nom;
            Invalidate();
            ValeurChangee?.Invoke(this, EventArgs.Empty);
        }
    }

    public Element? Choisi => _elements.FirstOrDefault(e => e.Id == _valeur);

    public event EventHandler? ValeurChangee;

    /// <summary>Tuiles par ligne au plus (« Modifier la sortie » : 5 ; « Nouvelle sortie » : 6).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ColonnesMax
    {
        get => _colonnesMax;
        set
        {
            if (value == _colonnesMax)
            {
                return;
            }

            _colonnesMax = value;
            _largeurPlacee = -1;
            Parent?.PerformLayout(this, nameof(Height));
            Invalidate();
        }
    }

    private int _colonnesMax = 5;

    /// <summary>Hauteur d'une tuile, en pixels logiques.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int HauteurTuile { get; set; } = 66;

    private int Colonnes
    {
        get
        {
            var max = Math.Max(1, ColonnesMax);
            return _elements.Count <= max ? Math.Max(1, _elements.Count) : (int)Math.Ceiling(_elements.Count / Math.Ceiling(_elements.Count / (double)max));
        }
    }

    private void Placer(int largeur)
    {
        if (largeur == _largeurPlacee || largeur <= 0)
        {
            return;
        }

        _largeurPlacee = largeur;
        _zones.Clear();
        var colonnes = Colonnes;
        var ecart = E(Ecart);
        var l = (largeur - 1 - ((colonnes - 1) * ecart)) / colonnes;
        var h = E(HauteurTuile);
        for (var i = 0; i < _elements.Count; i++)
        {
            _zones.Add(new RectangleF((i % colonnes) * (l + ecart), (i / colonnes) * (h + ecart), l, h));
        }

        var lignes = Math.Max(1, (_elements.Count + colonnes - 1) / colonnes);
        _hauteur = (int)Math.Ceiling((lignes * h) + ((lignes - 1) * ecart)) + 1;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var largeur = MesureFormulaire.Largeur(this, proposedSize);
        Placer(largeur);
        return new Size(largeur, _hauteur);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Placer(Width);
        if (Height != _hauteur)
        {
            Parent?.PerformLayout(this, nameof(Height));
        }
    }

    protected override void Peindre(Graphics g)
    {
        Placer(Width);
        for (var i = 0; i < _zones.Count; i++)
        {
            var el = _elements[i];
            var zone = _zones[i];
            var choisi = el.Id == _valeur;
            var r = new RectangleF(zone.X + 0.5f, zone.Y + 0.5f, zone.Width - 1, zone.Height - 1);
            using var chemin = Dessin.Arrondi(r, E(14));
            using (var fond = new SolidBrush(choisi ? Theme.Sur(ChampSaisie.FondChamp, el.Couleur, 0.16f) : ChampSaisie.FondChamp))
            {
                g.FillPath(fond, chemin);
            }

            var focus = Focused && choisi;
            var bordure = choisi ? el.Couleur
                : i == _survol ? Theme.Sur(ChampSaisie.FondChamp, el.Couleur, 0.6f)
                : Theme.BordureForte;
            using (var stylo = new Pen(bordure, E(choisi ? (focus ? 2.5f : 1.5f) : 1)))
            {
                g.DrawPath(stylo, chemin);
            }

            var icone = E(22);
            var detail = el.Detail is { Length: > 0 } ? E(15) : 0;
            var haut = zone.Y + ((zone.Height - icone - E(5) - E(18) - detail) / 2);
            Icones.Dessiner(g, el.Icone, new RectangleF(zone.X + ((zone.Width - icone) / 2), haut, icone, icone), el.Couleur);
            Dessin.Texte(g, el.Nom, Theme.PoliceTitre(9.75f), Rectangle.Round(new RectangleF(zone.X + E(4), haut + icone + E(5), zone.Width - E(8), E(18))),
                choisi ? Theme.Texte : Theme.TexteSecondaireClair, centre: true);
            if (detail > 0)
            {
                Dessin.Texte(g, el.Detail!, Theme.Police(8.25f), Rectangle.Round(new RectangleF(zone.X + E(4), haut + icone + E(5) + E(17), zone.Width - E(8), E(15))),
                    Theme.TexteSecondaire, centre: true);
            }
        }
    }

    private int Sous(Point p) => _zones.FindIndex(z => z.Contains(p));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var i = Sous(e.Location);
        Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        if (i != _survol)
        {
            _survol = i;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _survol = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (Sous(e.Location) is var i and >= 0)
        {
            Valeur = _elements[i].Id;
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_elements.Count == 0)
        {
            return;
        }

        var i = Math.Max(0, _elements.FindIndex(x => x.Id == _valeur));
        var n = e.KeyCode switch
        {
            Keys.Left => i - 1,
            Keys.Right => i + 1,
            Keys.Up => i - Colonnes,
            Keys.Down => i + Colonnes,
            _ => int.MinValue,
        };
        if (n == int.MinValue)
        {
            return;
        }

        Valeur = _elements[Math.Clamp(n, 0, _elements.Count - 1)].Id;
        e.Handled = true;
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
}

/// <summary>
/// Responsables en cartes sur deux colonnes : pastille (initiales d'un moniteur, abréviation d'une classe), nom, détail,
/// rond coché pour le choisi. Au-delà de <see cref="LignesVisibles"/> lignes, la liste défile à la molette (et au clavier).
/// Clavier : flèches (choisissent directement). Un seul contrôle dessiné.
/// </summary>
public sealed class ChoixPersonnes : ControleDessine
{
    private const int HauteurCarte = 56;
    private const int Ecart = 8;
    private const int LargeurBarre = 10;

    public sealed record Element(long Id, string Initiales, string Nom, string Detail, (Color Clair, Color Fonce) Degrade, bool Classe, bool Actif);

    private readonly List<Element> _elements = [];
    private long? _valeur;
    private int _survol = -1;
    private float _decalage;

    public ChoixPersonnes()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        AutoSize = true;
        Height = HauteurCarte;
        AccessibleRole = AccessibleRole.List;
    }

    /// <summary>Lignes affichées avant de défiler.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int LignesVisibles { get; set; } = 4;

    private int _colonnes = 2;

    /// <summary>Cartes par ligne (« Modifier la sortie » : 2 ; « Nouvelle sortie » : 4, moins si la place manque).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Colonnes
    {
        get => _colonnes;
        set
        {
            value = Math.Max(1, value);
            if (value == _colonnes)
            {
                return;
            }

            _colonnes = value;
            _decalage = 0;
            RendreVisible();
            Parent?.PerformLayout(this, nameof(Height));
            Invalidate();
        }
    }

    /// <summary>Texte affiché quand la liste est vide.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string TexteVide { get; set; } = "";

    public void Remplir(IEnumerable<Element> elements, long? choisi)
    {
        _elements.Clear();
        _elements.AddRange(elements);
        _valeur = _elements.Any(e => e.Id == choisi) ? choisi : null;
        _decalage = 0;
        _survol = -1;
        RendreVisible();
        Parent?.PerformLayout(this, nameof(Height));
        Invalidate();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public long? Valeur
    {
        get => _valeur;
        set
        {
            if (_valeur == value)
            {
                return;
            }

            _valeur = value;
            AccessibleDescription = Choisi?.Nom;
            RendreVisible();
            Invalidate();
            ValeurChangee?.Invoke(this, EventArgs.Empty);
        }
    }

    public Element? Choisi => _elements.FirstOrDefault(e => e.Id == _valeur);

    public event EventHandler? ValeurChangee;

    private int Lignes => Math.Max(1, (_elements.Count + Colonnes - 1) / Colonnes);

    private int LignesAffichees => Math.Min(Lignes, LignesVisibles);

    private bool Defile => Lignes > LignesVisibles;

    private float HauteurContenu => (Lignes * E(HauteurCarte)) + ((Lignes - 1) * E(Ecart));

    private int HauteurVoulue => (int)Math.Ceiling((LignesAffichees * E(HauteurCarte)) + ((LignesAffichees - 1) * E(Ecart))) + 1;

    public override Size GetPreferredSize(Size proposedSize) => new(MesureFormulaire.Largeur(this, proposedSize), HauteurVoulue);

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Height != HauteurVoulue)
        {
            Parent?.PerformLayout(this, nameof(Height));
        }
    }

    private RectangleF Zone(int i)
    {
        var largeur = Width - 1 - (Defile ? E(LargeurBarre) : 0);
        var l = (largeur - ((Colonnes - 1) * E(Ecart))) / Colonnes;
        return new RectangleF((i % Colonnes) * (l + E(Ecart)), ((i / Colonnes) * (E(HauteurCarte) + E(Ecart))) - _decalage, l, E(HauteurCarte));
    }

    protected override void Peindre(Graphics g)
    {
        if (_elements.Count == 0)
        {
            Dessin.Texte(g, TexteVide, Theme.Police(9.75f), new Rectangle(0, 0, Width, Height), Theme.TexteSecondaire);
            return;
        }

        for (var i = 0; i < _elements.Count; i++)
        {
            var zone = Zone(i);
            if (zone.Bottom < 0 || zone.Top > Height)
            {
                continue;
            }

            PeindreCarte(g, _elements[i], zone, i == _survol);
        }

        if (Defile)
        {
            // Barre de défilement discrète à droite.
            var piste = new RectangleF(Width - E(4) - 1, 0, E(4), Height - 1);
            using (var fond = new SolidBrush(Theme.Teinte(Theme.Texte, 0.06f)))
            using (var chemin = Dessin.Arrondi(piste, piste.Width / 2))
            {
                g.FillPath(fond, chemin);
            }

            var part = Height / HauteurContenu;
            var curseur = new RectangleF(piste.X, piste.Height * (_decalage / HauteurContenu), piste.Width, Math.Max(E(20), piste.Height * part));
            using var pinceau = new SolidBrush(Theme.BordureForte);
            using var forme = Dessin.Arrondi(curseur, curseur.Width / 2);
            g.FillPath(pinceau, forme);
        }
    }

    private void PeindreCarte(Graphics g, Element el, RectangleF zone, bool survol)
    {
        var choisi = el.Id == _valeur;
        var r = new RectangleF(zone.X + 0.5f, zone.Y + 0.5f, zone.Width - 1, zone.Height - 1);
        using var chemin = Dessin.Arrondi(r, E(14));
        using (var fond = new SolidBrush(choisi ? Theme.Sur(ChampSaisie.FondChamp, Theme.Accent, 0.08f) : ChampSaisie.FondChamp))
        {
            g.FillPath(fond, chemin);
        }

        var focus = Focused && choisi;
        var bordure = choisi ? Theme.Accent : survol ? Theme.Sur(ChampSaisie.FondChamp, Theme.Accent, 0.45f) : Theme.BordureForte;
        using (var stylo = new Pen(bordure, E(choisi ? (focus ? 2.5f : 1.5f) : 1)))
        {
            g.DrawPath(stylo, chemin);
        }

        // Pastille : ronde pour un moniteur, carrée arrondie pour une classe (comme dans Membres).
        var av = new RectangleF(zone.X + E(8), zone.Y + ((zone.Height - E(36)) / 2), E(36), E(36));
        var (clair, fonce) = el.Actif ? el.Degrade : (Theme.TexteSecondaire, Theme.SurfaceActive);
        using (var degrade = new LinearGradientBrush(av, clair, fonce, 40f))
        {
            if (el.Classe)
            {
                using var forme = Dessin.Arrondi(av, av.Height * 0.3f);
                g.FillPath(degrade, forme);
            }
            else
            {
                g.FillEllipse(degrade, av);
            }
        }

        var policeAvatar = Theme.PoliceGrasse(el.Initiales.Length > 2 ? 7.5f : 9.75f);
        Dessin.Texte(g, el.Initiales, policeAvatar, Rectangle.Round(av), Theme.TexteSurAvatar, centre: true);

        // Rond coché à droite.
        var ck = new RectangleF(zone.Right - E(10) - E(20), zone.Y + ((zone.Height - E(20)) / 2), E(20), E(20));
        if (choisi)
        {
            using var plein = new SolidBrush(Theme.Accent);
            g.FillEllipse(plein, ck);
            Icones.Dessiner(g, Icones.Coche, RectangleF.Inflate(ck, -E(4.5f), -E(4.5f)), Theme.TexteSurAccent, 3f);
        }
        else
        {
            using var cercle = new Pen(Theme.BordureForte, E(1.5f));
            g.DrawEllipse(cercle, ck);
        }

        var x = av.Right + E(10);
        var l = ck.Left - E(6) - x;
        Dessin.Texte(g, el.Nom, Theme.PoliceTitre(10.5f), Rectangle.Round(new RectangleF(x, zone.Y + E(9), l, E(20))), el.Actif ? Theme.Texte : Theme.TexteSecondaireClair);
        Dessin.Texte(g, el.Detail, Theme.Police(9f), Rectangle.Round(new RectangleF(x, zone.Y + E(28), l, E(18))), Theme.TexteSecondaire);
    }

    private int Sous(Point p)
    {
        for (var i = 0; i < _elements.Count; i++)
        {
            if (Zone(i).Contains(p))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Fait défiler pour que le choisi soit entièrement visible.</summary>
    private void RendreVisible()
    {
        var i = _elements.FindIndex(e => e.Id == _valeur);
        if (i < 0 || !Defile)
        {
            _decalage = Defile ? _decalage : 0;
            return;
        }

        var haut = (i / Colonnes) * (E(HauteurCarte) + E(Ecart));
        var hauteurVue = HauteurVoulue;
        if (haut < _decalage)
        {
            _decalage = haut;
        }
        else if (haut + E(HauteurCarte) > _decalage + hauteurVue)
        {
            _decalage = haut + E(HauteurCarte) - hauteurVue + 1;
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!Defile)
        {
            return;
        }

        var max = Math.Max(0, HauteurContenu - Height + 1);
        _decalage = Math.Clamp(_decalage - (e.Delta / 120f * (E(HauteurCarte) + E(Ecart))), 0, max);
        if (e is HandledMouseEventArgs h)
        {
            h.Handled = true; // La fenêtre ne défile pas en même temps.
        }

        _survol = Sous(e.Location);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var i = Sous(e.Location);
        Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        if (i != _survol)
        {
            _survol = i;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _survol = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (Sous(e.Location) is var i and >= 0)
        {
            Valeur = _elements[i].Id;
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_elements.Count == 0)
        {
            return;
        }

        var i = _elements.FindIndex(x => x.Id == _valeur);
        var n = e.KeyCode switch
        {
            Keys.Left => i - 1,
            Keys.Right => i + 1,
            Keys.Up => i - Colonnes,
            Keys.Down => i < 0 ? 0 : i + Colonnes,
            _ => int.MinValue,
        };
        if (n == int.MinValue)
        {
            return;
        }

        Valeur = _elements[Math.Clamp(n, 0, _elements.Count - 1)].Id;
        e.Handled = true;
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
}
