using System.ComponentModel;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>Allure des puces : valeur du panneau « Filtres », raccourci, ou filtre en cours (avec sa croix).</summary>
public enum StylePuces
{
    Option,
    Raccourci,
    Etiquette,
}

/// <summary>Une puce : clé rendue au clic, texte, nombre (facultatif), point de couleur (facultatif), cochée ou non.</summary>
public sealed record PuceFiltre(string Cle, string Texte, int? Nombre = null, Color? Point = null, bool Choisie = false, bool Active = true);

/// <summary>
/// Puces à cocher (maquette « Inventaire » v2) qui passent à la ligne, dessinées dans un seul contrôle : plusieurs peuvent
/// être cochées (c'est l'écran qui décide, d'après <see cref="PuceCliquee"/>). Intitulé facultatif en tête (« RACCOURCIS »).
/// Clavier : flèches puis Espace ou Entrée. Hauteur d'après le contenu (AutoSize, mesurée à la largeur du parent).
/// </summary>
public sealed class PucesFiltre : ControleDessine
{
    private const int HauteurZone = 44;
    private const int Ecart = 6;

    private readonly StylePuces _style;
    private string? _intitule;
    private string? _lien;
    private RectangleF _zoneLien;
    private bool _survolLien;
    private readonly ToolTip _bulle = new();
    private readonly List<(RectangleF Zone, PuceFiltre Puce)> _zones = [];
    private IReadOnlyList<PuceFiltre> _puces = [];
    private RectangleF _zoneIntitule;
    private int _survol = -1;
    private int _focus;
    private int _largeurPlacee = -1;
    private int _hauteur;

    public PucesFiltre(StylePuces style, string? intitule = null)
    {
        _style = style;
        _intitule = intitule;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Height = HauteurZone;
        AutoSize = true;
    }

    /// <summary>Clic (ou Espace) sur une puce active : sa clé.</summary>
    public event EventHandler<string>? PuceCliquee;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<PuceFiltre> Puces => _puces;

    /// <summary>Texte en tête de la première ligne (« RACCOURCIS », « 37 équipements »).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Intitule
    {
        get => _intitule;
        set
        {
            _intitule = value;
            _largeurPlacee = -1;
            Invalidate();
        }
    }

    /// <summary>Lien souligné après les puces (« Tout effacer ») ; null = aucun.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Lien
    {
        get => _lien;
        set
        {
            _lien = value;
            _largeurPlacee = -1;
            Invalidate();
        }
    }

    /// <summary>Clic sur <see cref="Lien"/>.</summary>
    public event EventHandler? LienClique;

    /// <summary>Remplace les puces (le focus clavier reste sur la même position).</summary>
    public void Afficher(IEnumerable<PuceFiltre> puces)
    {
        _puces = puces.ToList();
        _focus = Math.Min(_focus, Math.Max(0, _puces.Count - 1));
        _largeurPlacee = -1;
        AccessibleName = string.Join(", ", _puces.Select(p => p.Nombre is { } n ? $"{p.Texte} {n}" : p.Texte));
        Placer(Width);
        if (Height != _hauteur)
        {
            Parent?.PerformLayout(this, nameof(Height));
        }

        Invalidate();
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
        if (_hauteur > 0 && Height != _hauteur)
        {
            Parent?.PerformLayout(this, nameof(Height));
        }
    }

    private Font PoliceTexte => _style == StylePuces.Etiquette ? Theme.PoliceGrasse(8.25f) : Theme.PoliceGrasse(9f);

    private static Font PoliceNombre => Theme.PoliceGrasse(7.5f);

    private Font PoliceIntitule => _style == StylePuces.Etiquette ? Theme.Police(10.5f) : Theme.PoliceGrasse(8.25f);

    private static Font PoliceLien => Theme.PoliceGrasse(9f);

    private float LargeurPuce(PuceFiltre p)
    {
        var l = E(12) + TextRenderer.MeasureText(p.Texte, PoliceTexte, Size.Empty, TextFormatFlags.NoPadding).Width;
        if (p.Point is not null)
        {
            l += E(8 + 8);
        }

        if (p.Nombre is { } n)
        {
            l += E(6) + LargeurNombre(n);
        }

        l += _style switch
        {
            StylePuces.Etiquette => E(6 + 14 + 8), // Croix.
            StylePuces.Raccourci when p.Nombre is not null => E(6),
            _ => E(12),
        };
        return l;
    }

    private float LargeurNombre(int n) => _style == StylePuces.Raccourci
        ? Math.Max(E(24), TextRenderer.MeasureText(n.ToString(Libelles.Culture), PoliceNombre).Width + E(6))
        : TextRenderer.MeasureText(n.ToString(Libelles.Culture), PoliceNombre, Size.Empty, TextFormatFlags.NoPadding).Width;

    private void Placer(int largeur)
    {
        if (largeur == _largeurPlacee || largeur <= 0)
        {
            return;
        }

        _largeurPlacee = largeur;
        _zones.Clear();
        var hauteur = E(HauteurZone);
        float x = 0, y = 0;
        _zoneIntitule = RectangleF.Empty;
        if (!string.IsNullOrEmpty(_intitule))
        {
            var t = TextRenderer.MeasureText(_intitule, PoliceIntitule, Size.Empty, TextFormatFlags.NoPadding).Width;
            _zoneIntitule = new RectangleF(0, 0, t, hauteur);
            x = t + E(12);
        }

        var debutLigne = x;
        foreach (var puce in _puces)
        {
            var l = Math.Min(LargeurPuce(puce), largeur);
            if (x > debutLigne && x + l > largeur)
            {
                x = _style == StylePuces.Etiquette ? 0 : debutLigne;
                y += hauteur;
            }

            _zones.Add((new RectangleF(x, y, l, hauteur), puce));
            x += l + E(Ecart);
        }

        _zoneLien = RectangleF.Empty;
        if (!string.IsNullOrEmpty(_lien))
        {
            var l = TextRenderer.MeasureText(_lien, PoliceLien, Size.Empty, TextFormatFlags.NoPadding).Width + E(12);
            if (x > 0 && x + l > largeur)
            {
                x = 0;
                y += hauteur;
            }

            _zoneLien = new RectangleF(x, y, l, hauteur);
        }

        _hauteur = _puces.Count == 0 && _zoneIntitule.IsEmpty && _zoneLien.IsEmpty ? 0 : (int)Math.Ceiling(y + hauteur);
    }

    protected override void Peindre(Graphics g)
    {
        Placer(Width);
        if (!_zoneIntitule.IsEmpty && _intitule is not null)
        {
            Dessin.Texte(g, _intitule, PoliceIntitule, Rectangle.Round(_zoneIntitule), _style == StylePuces.Etiquette ? Theme.TexteDoux : Theme.TexteSecondaire);
        }

        if (!_zoneLien.IsEmpty && _lien is not null)
        {
            var focusLien = Focused && ShowFocusCues && _focus == _zones.Count;
            using var souligne = new Font(PoliceLien, FontStyle.Bold | FontStyle.Underline);
            var zone = Rectangle.Round(new RectangleF(_zoneLien.X + E(6), _zoneLien.Y, _zoneLien.Width - E(6), _zoneLien.Height));
            Dessin.Texte(g, _lien, souligne, zone, _survolLien || focusLien ? Theme.Texte : Theme.TexteSecondaire);
        }

        var fondParent = Dessin.CouleurParent(this);
        for (var i = 0; i < _zones.Count; i++)
        {
            PeindrePuce(g, _zones[i].Zone, _zones[i].Puce, i == _survol, Focused && ShowFocusCues && i == _focus, fondParent);
        }
    }

    private void PeindrePuce(Graphics g, RectangleF zone, PuceFiltre p, bool survol, bool focus, Color fondParent)
    {
        var h = E(_style switch { StylePuces.Option => 36, StylePuces.Raccourci => 38, _ => 32 });
        var r = new RectangleF(zone.X + 0.5f, zone.Y + ((zone.Height - h) / 2), zone.Width - 1, h);
        var teinte = p.Point ?? Theme.Accent;
        Color fond, bord, texte, nombre;
        switch (_style)
        {
            case StylePuces.Option:
                fond = p.Choisie ? Theme.Sur(fondParent, Theme.Accent, 0.16f) : survol ? Theme.Sur(fondParent, Theme.AccentClair, 0.08f) : Theme.Sur(fondParent, Theme.Fond, 0.45f);
                bord = p.Choisie ? Theme.Accent : Theme.Bordure;
                texte = p.Choisie ? Theme.Texte : Theme.TexteDoux;
                nombre = p.Choisie ? Theme.AccentClair : Theme.TexteSecondaire;
                break;
            case StylePuces.Raccourci:
                fond = p.Choisie ? Theme.Sur(fondParent, teinte, 0.16f) : survol && p.Active ? Theme.Sur(fondParent, Theme.AccentClair, 0.06f) : fondParent;
                bord = p.Choisie ? teinte : Theme.Bordure;
                texte = p.Active ? Theme.Texte : Theme.TexteSecondaire;
                nombre = texte;
                break;
            default:
                fond = Theme.Sur(fondParent, Theme.Accent, survol ? 0.24f : 0.14f);
                bord = fond;
                texte = Theme.AccentClair;
                nombre = texte;
                break;
        }

        using (var chemin = Dessin.Arrondi(r, _style == StylePuces.Option ? E(10) : h / 2))
        {
            using (var pinceau = new SolidBrush(fond))
            {
                g.FillPath(pinceau, chemin);
            }

            using var stylo = new Pen(focus ? Theme.AccentClair : bord, E(focus ? 2 : 1));
            g.DrawPath(stylo, chemin);
        }

        var x = r.X + E(12);
        if (p.Point is { } point)
        {
            var d = E(8);
            using var pinceau = new SolidBrush(p.Active ? point : Theme.Sur(fondParent, point, 0.45f));
            g.FillEllipse(pinceau, x, r.Y + ((r.Height - d) / 2), d, d);
            x += E(8 + 8);
        }

        const TextFormatFlags drapeaux = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
        var taille = TextRenderer.MeasureText(g, p.Texte, PoliceTexte, Size.Empty, drapeaux);
        var reste = p.Nombre is { } n0 ? E(6) + LargeurNombre(n0) : 0;
        var fin = r.Right - (_style == StylePuces.Etiquette ? E(6 + 14 + 8) : _style == StylePuces.Raccourci && p.Nombre is not null ? E(6) : E(12)) - reste;
        var largeurTexte = Math.Min(taille.Width, (int)(fin - x));
        TextRenderer.DrawText(g, p.Texte, PoliceTexte, new Rectangle((int)x, (int)(r.Y + ((r.Height - taille.Height) / 2)), Math.Max(0, largeurTexte), taille.Height), texte, drapeaux);
        x += largeurTexte;

        if (p.Nombre is { } n)
        {
            x += E(6);
            var ln = LargeurNombre(n);
            var texteNombre = n.ToString(Libelles.Culture);
            if (_style == StylePuces.Raccourci)
            {
                var hb = E(24);
                var badge = new RectangleF(x, r.Y + ((r.Height - hb) / 2), ln, hb);
                using var chemin = Dessin.Arrondi(badge, hb / 2);
                using var pinceau = new SolidBrush(Theme.Sur(fond, Theme.AccentClair, 0.12f));
                g.FillPath(pinceau, chemin);
                Dessin.Texte(g, texteNombre, PoliceNombre, Rectangle.Round(badge), nombre, centre: true);
            }
            else
            {
                Dessin.Texte(g, texteNombre, PoliceNombre, Rectangle.Round(new RectangleF(x, r.Y, ln + 2, r.Height)), nombre);
            }
        }

        if (_style == StylePuces.Etiquette)
        {
            var c = E(14);
            Icones.Dessiner(g, Icones.Croix, new RectangleF(r.Right - E(8) - c, r.Y + ((r.Height - c) / 2), c, c), texte);
        }
    }

    /// <summary>Indice de la puce sous la souris ; <c>_zones.Count</c> pour le lien ; -1 sinon.</summary>
    private int Sous(Point p) => _zones.FindIndex(z => z.Zone.Contains(p)) is var i and >= 0 ? i : _zoneLien.Contains(p) ? _zones.Count : -1;

    private int NombreCibles => _zones.Count + (_zoneLien.IsEmpty ? 0 : 1);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var i = Sous(e.Location);
        Cursor = (i >= 0 && i < _zones.Count && _zones[i].Puce.Active) || i == _zones.Count ? Cursors.Hand : Cursors.Default;
        if (i != _survol)
        {
            _survol = i;
            _survolLien = i == _zones.Count;
            Invalidate();
            _bulle.SetToolTip(this, i >= 0 && i < _zones.Count && _style == StylePuces.Etiquette ? "Retirer ce filtre" : null);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _survol = -1;
        _survolLien = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && Sous(e.Location) is var i and >= 0)
        {
            _focus = i;
            Activer(i);
        }
    }

    private void Activer(int i)
    {
        if (i >= 0 && i < _zones.Count && _zones[i].Puce.Active)
        {
            PuceCliquee?.Invoke(this, _zones[i].Puce.Cle);
        }
        else if (i == _zones.Count && !_zoneLien.IsEmpty)
        {
            LienClique?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (NombreCibles == 0)
        {
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Up:
                _focus = Math.Max(0, _focus - 1);
                break;
            case Keys.Right or Keys.Down:
                _focus = Math.Min(NombreCibles - 1, _focus + 1);
                break;
            case Keys.Space or Keys.Enter:
                Activer(_focus);
                break;
            default:
                return;
        }

        e.Handled = e.SuppressKeyPress = true;
        Invalidate();
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bulle.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>Bouton « Filtres » : entonnoir, libellé et pastille du nombre de filtres cochés ; bordure turquoise s'il est ouvert.</summary>
public sealed class BoutonFiltres : ControleDessine
{
    private int _nombre;
    private bool _ouvert;
    private bool _survol;

    public BoutonFiltres()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = "Filtres";
        Height = Theme.HauteurChamp;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Nombre
    {
        get => _nombre;
        set
        {
            Invalider(ref _nombre, value);
            AccessibleDescription = value > 0 ? Libelles.Pluriel(value, "filtre choisi", "filtres choisis") : null;
        }
    }

    /// <summary>Panneau des filtres ouvert.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Ouvert { get => _ouvert; set => Invalider(ref _ouvert, value); }

    public override Size GetPreferredSize(Size proposedSize) =>
        new((int)(E(16 + 18 + 8 + 16 + 34) + TextRenderer.MeasureText("Filtres", Theme.PoliceGrasse(9.75f)).Width), (int)E(Theme.HauteurChamp));

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        Invalider(ref _survol, true);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Invalider(ref _survol, false);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            e.Handled = e.SuppressKeyPress = true;
            OnClick(EventArgs.Empty);
        }
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

    protected override void Peindre(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        var fond = _ouvert ? Theme.Sur(Theme.Surface, Theme.Accent, 0.10f) : _survol ? Theme.Sur(Theme.Surface, Theme.AccentClair, 0.06f) : Theme.Surface;
        using (var chemin = Dessin.Arrondi(r, E(16)))
        {
            using (var pinceau = new SolidBrush(fond))
            {
                g.FillPath(pinceau, chemin);
            }

            using var stylo = new Pen(_ouvert || (Focused && ShowFocusCues) ? Theme.Accent : Theme.BordureForte, E(Focused && ShowFocusCues ? 2 : 1));
            g.DrawPath(stylo, chemin);
        }

        var t = E(18);
        var x = E(16);
        Icones.Dessiner(g, Icones.Filtre, new RectangleF(x, (Height - t) / 2, t, t), Theme.AccentClair);
        x += t + E(8);
        var police = Theme.PoliceGrasse(9.75f);
        var taille = TextRenderer.MeasureText(g, "Filtres", police, Size.Empty, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, "Filtres", police, new Point((int)x, (Height - taille.Height) / 2), Theme.Texte, TextFormatFlags.NoPadding);
        if (_nombre > 0)
        {
            x += taille.Width + E(8);
            var hb = E(22);
            var lb = Math.Max(hb, TextRenderer.MeasureText(_nombre.ToString(Libelles.Culture), Theme.PoliceGrasse(7.5f)).Width + E(6));
            var badge = new RectangleF(x, (Height - hb) / 2, lb, hb);
            using var chemin = Dessin.Arrondi(badge, hb / 2);
            using var pinceau = new SolidBrush(Theme.Accent);
            g.FillPath(pinceau, chemin);
            Dessin.Texte(g, _nombre.ToString(Libelles.Culture), Theme.PoliceGrasse(7.5f), Rectangle.Round(badge), Theme.TexteSurAccent, centre: true);
        }
    }
}

/// <summary>
/// Panneau « Filtres » de l'inventaire : un groupe par colonne (titre en capitales + puces à cocher), autant de colonnes
/// que la largeur le permet (5 au plus), les groupes suivants passent dessous. Hauteur d'après le contenu (AutoSize).
/// </summary>
public sealed class PanneauFiltres : Carte
{
    private const int LargeurColonneMin = 240;
    private const int EcartColonnes = 18;
    private const int EcartTitre = 6;
    private const int EcartRangees = 14;

    private readonly List<(Label Titre, PucesFiltre Puces)> _groupes = [];
    private int _hauteur;
    private bool _placement;

    public PanneauFiltres()
    {
        Rayon = 20;
        Padding = new Padding(18);
        AutoSize = true; // La ligne AutoSize du TableLayoutPanel lit alors GetPreferredSize.
    }

    /// <summary>Ajoute un groupe et rend ses puces (l'écran les remplit et écoute leurs clics).</summary>
    public PucesFiltre AjouterGroupe(string titre)
    {
        var libelle = Ui.Libelle(titre.ToUpper(Core.Modeles.Libelles.Culture), Theme.PoliceGrasse(8.25f), Theme.TexteSecondaire);
        libelle.Margin = Padding.Empty;
        var puces = new PucesFiltre(StylePuces.Option) { AutoSize = false, AccessibleDescription = $"Filtre {titre}" };
        Controls.Add(libelle);
        Controls.Add(puces);
        _groupes.Add((libelle, puces));
        return puces;
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        if (e.Control is { } enfant)
        {
            Dessin.MettreALEchelleSiAjoutTardif(this, enfant);
        }

        base.OnControlAdded(e);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var largeur = MesureFormulaire.Largeur(this, proposedSize);
        return new Size(largeur, Placer(largeur, appliquer: false));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        if (_placement)
        {
            return;
        }

        var hauteur = Placer(Width, appliquer: true);
        if (hauteur != _hauteur)
        {
            _hauteur = hauteur;
            Parent?.PerformLayout(this, nameof(Height));
        }
    }

    /// <summary>Place les groupes (si <paramref name="appliquer"/>) et rend la hauteur totale.</summary>
    private int Placer(int largeur, bool appliquer)
    {
        if (largeur <= 0 || _groupes.Count == 0)
        {
            return Padding.Vertical;
        }

        float E(float v) => Dessin.Echelle(this, v);
        var interieur = largeur - Padding.Horizontal;
        var ecart = (int)E(EcartColonnes);
        var colonnes = Math.Clamp((interieur + ecart) / (int)(E(LargeurColonneMin) + ecart), 1, Math.Min(5, _groupes.Count));
        var lc = (interieur - ((colonnes - 1) * ecart)) / colonnes;
        var y = Padding.Top;
        _placement = true;
        try
        {
            for (var debut = 0; debut < _groupes.Count; debut += colonnes)
            {
                var rangee = 0;
                for (var c = 0; c < colonnes && debut + c < _groupes.Count; c++)
                {
                    var (titre, puces) = _groupes[debut + c];
                    var ht = titre.PreferredHeight;
                    var hp = puces.GetPreferredSize(new Size(lc, 0)).Height;
                    if (appliquer)
                    {
                        var x = Padding.Left + (c * (lc + ecart));
                        titre.SetBounds(x, y, lc, ht);
                        puces.SetBounds(x, y + ht + (int)E(EcartTitre), lc, hp);
                    }

                    rangee = Math.Max(rangee, ht + (int)E(EcartTitre) + hp);
                }

                y += rangee + (int)E(EcartRangees);
            }
        }
        finally
        {
            _placement = false;
        }

        return y - (int)E(EcartRangees) + Padding.Bottom;
    }
}
