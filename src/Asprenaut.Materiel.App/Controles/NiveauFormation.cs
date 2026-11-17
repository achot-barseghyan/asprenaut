using System.ComponentModel;
using System.Drawing.Drawing2D;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>
/// Maquette « Niveau et formation » : échelle des brevets obtenus, Aucun (débutant) puis N1 à N4, reliés par une piste
/// remplie jusqu'au brevet choisi. Brevets passés cochés, brevet choisi en turquoise, brevet préparé (<see cref="EnFormation"/>)
/// en pointillé doré, « en formation ». Choix unique (clic, flèches) ; null = non renseigné. Un seul contrôle dessiné.
/// </summary>
public sealed class EchelleBrevets : ControleDessine
{
    private const int Pastille = 58;
    private const int HauteurTotale = 114; // Marge haute de 10 : halo (5) et anneau du focus (8) des pastilles.
    private const int Halo = 10;

    private static readonly (string Court, string Libelle, string Detail)[] Etapes =
    [
        ("–", "Aucun", "débutant"),
        ("N1", "Niveau 1", "20 m encadré"),
        ("N2", "Niveau 2", "40 m"),
        ("N3", "Niveau 3", "60 m"),
        ("N4", "Niveau 4", "guide"),
    ];

    private int? _valeur;
    private bool _enFormation;
    private int _survol = -1;
    private int _focus;

    public EchelleBrevets()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleName = "Brevet obtenu";
        AccessibleRole = AccessibleRole.RadioButton;
        Size = new Size(560, HauteurTotale);
        AutoSize = true;
    }

    /// <summary>Brevet obtenu : 0 (aucun, débutant) à 4, ou null (non renseigné).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int? Valeur
    {
        get => _valeur;
        set
        {
            if (_valeur == value)
            {
                return;
            }

            _valeur = value;
            AccessibleDescription = value is { } v ? Etapes[v].Libelle : "non renseigné";
            Invalidate();
            ValeurChangee?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Le brevet suivant est préparé : pastille en pointillé doré, « en formation ».</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool EnFormation { get => _enFormation; set => Invalider(ref _enFormation, value); }

    public event EventHandler? ValeurChangee;

    public override Size GetPreferredSize(Size proposedSize) =>
        new(MesureFormulaire.Largeur(this, proposedSize), (int)Math.Ceiling(E(HauteurTotale)));

    private float LargeurColonne => Width / (float)Etapes.Length;

    private RectangleF Rond(int i)
    {
        var d = E(Pastille);
        return new RectangleF((LargeurColonne * (i + 0.5f)) - (d / 2), E(Halo), d, d);
    }

    protected override void Peindre(Graphics g)
    {
        // Piste du centre de la première pastille au centre de la dernière, remplie jusqu'au brevet choisi.
        var yPiste = Rond(0).Top + (E(Pastille) / 2) - E(2);
        var debut = LargeurColonne / 2;
        var fin = Width - (LargeurColonne / 2);
        using (var piste = Dessin.Arrondi(new RectangleF(debut, yPiste, fin - debut, E(4)), E(2)))
        using (var fond = new SolidBrush(Theme.Teinte(Theme.AccentClair, 0.16f)))
        {
            g.FillPath(fond, piste);
        }

        if (_valeur is > 0 and var v)
        {
            using var rempli = Dessin.Arrondi(new RectangleF(debut, yPiste, LargeurColonne * v, E(4)), E(2));
            using var pinceau = new LinearGradientBrush(new RectangleF(debut - 1, yPiste, (LargeurColonne * v) + 2, E(4)), Teinte(Theme.Accent, 0.7f), Theme.Accent, LinearGradientMode.Horizontal);
            g.FillPath(pinceau, rempli);
        }

        for (var i = 0; i < Etapes.Length; i++)
        {
            PeindreEtape(g, i);
        }
    }

    private static Color Teinte(Color c, float part) => Theme.Sur(Theme.Surface, c, part);

    private void PeindreEtape(Graphics g, int i)
    {
        var (court, libelle, detail) = Etapes[i];
        var r = Rond(i);
        var choisie = _valeur == i;
        var passee = _valeur is { } v && i > 0 && i < v;
        var preparee = _enFormation && _valeur is { } w && i == w + 1;

        Color fond, texte;
        Color? bordure = null;
        var pointille = false;
        if (choisie)
        {
            fond = i == 0 ? Theme.Texte : Theme.Accent;
            texte = i == 0 ? Theme.Fond : Theme.TexteSurAccent;
            using var halo = new SolidBrush(Theme.Teinte(i == 0 ? Theme.Texte : Theme.Accent, 0.22f));
            g.FillEllipse(halo, RectangleF.Inflate(r, E(5), E(5)));
        }
        else if (passee)
        {
            fond = Teinte(Theme.Accent, 0.7f);
            texte = Theme.TexteSurAccent;
        }
        else if (preparee)
        {
            fond = ChampSaisie.FondChamp;
            texte = Theme.Avertissement;
            bordure = Theme.Avertissement;
            pointille = true;
        }
        else
        {
            fond = ChampSaisie.FondChamp;
            texte = Theme.TexteSecondaire;
            bordure = i == _survol ? Theme.AccentClair : Theme.BordureForte;
        }

        using (var pinceau = new SolidBrush(fond))
        {
            g.FillEllipse(pinceau, r);
        }

        if (bordure is { } b)
        {
            using var stylo = new Pen(b, E(pointille ? 2 : 1.5f));
            if (pointille)
            {
                stylo.DashStyle = DashStyle.Dash;
            }

            g.DrawEllipse(stylo, RectangleF.Inflate(r, -E(1), -E(1)));
        }

        if (Focused && i == _focus)
        {
            using var focus = new Pen(Theme.AccentClair, E(2));
            g.DrawEllipse(focus, RectangleF.Inflate(r, E(8), E(8)));
        }

        if (passee)
        {
            Icones.Dessiner(g, Icones.Coche, RectangleF.Inflate(r, -E(18), -E(18)), texte, 3f);
        }
        else
        {
            Dessin.Texte(g, court, Theme.PoliceGrasse(12f), Rectangle.Round(r), texte, centre: true);
        }

        var colonne = new RectangleF(LargeurColonne * i, r.Bottom + E(8), LargeurColonne, E(20));
        var couleurLibelle = choisie ? Theme.Texte : preparee ? Theme.Avertissement : Theme.TexteSecondaireClair;
        Dessin.Texte(g, libelle, choisie ? Theme.PoliceGrasse(10f) : Theme.Police(10f), Rectangle.Round(colonne), couleurLibelle, centre: true);
        colonne.Offset(0, E(19));
        Dessin.Texte(g, preparee ? "en formation" : detail, Theme.Police(8.25f), Rectangle.Round(colonne), preparee ? Theme.Avertissement : Theme.TexteSecondaire, centre: true);
    }

    private int Sous(Point p) => p.X < 0 || p.X >= Width ? -1 : Math.Clamp((int)(p.X / LargeurColonne), 0, Etapes.Length - 1);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var i = Sous(e.Location);
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
            _focus = i;
            Valeur = i;
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Up:
                _focus = Math.Max(0, _focus - 1);
                Valeur = _focus;
                break;
            case Keys.Right or Keys.Down:
                _focus = Math.Min(Etapes.Length - 1, _focus + 1);
                Valeur = _focus;
                break;
            case Keys.Space:
                Valeur = _focus;
                break;
            default:
                return;
        }

        e.Handled = true;
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        _focus = _valeur ?? 0;
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }
}

/// <summary>
/// Maquette « Niveau et formation » : autres brevets en puces turquoise retirables (×), brevets proposés non choisis en puces
/// pointillées (+), puis un champ « Autre brevet… » et son bouton + (Entrée ajoute). Un même brevet n'est jamais deux fois
/// (majuscules ignorées). Clavier sur les puces : flèches, Espace ou Entrée (ajouter / retirer), Suppr (retirer).
/// </summary>
public sealed class ChoixBrevets : ControleDessine
{
    private const int HauteurPuce = 38;
    private const int Ecart = 8;
    private const int LargeurChamp = 210;

    private readonly List<string> _brevets = [];
    private readonly IReadOnlyList<string> _proposes;
    private readonly List<(RectangleF Zone, string Brevet, bool Choisi)> _zones = [];
    private readonly BoutonPlus _plus = new();
    private Rectangle _zoneChamp;
    private int _survol = -1;
    private int _focus;
    private int _hauteur = HauteurPuce;
    private bool _placement;

    public ChoixBrevets(IReadOnlyList<string> proposes)
    {
        _proposes = proposes;
        SetStyle(ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent; // Sinon le champ « Autre brevet… » efface ses coins avec le blanc par défaut d'un Control.
        TabStop = true;
        AccessibleName = "Autres brevets";
        Champ = new ChampSaisie { TexteIndicatif = "Autre brevet…", Size = new Size(LargeurChamp, HauteurPuce), Padding = new Padding(14, 0, 6, 0) };
        Champ.Champ.AccessibleName = "Autre brevet";
        Champ.Champ.Font = Theme.PoliceGrasse(9.75f);
        Champ.Champ.MaxLength = Core.Modeles.Adherent.LongueurMaxBrevet;
        Champ.ADroite = _plus;
        Controls.Add(Champ);
        Champ.Champ.TextChanged += (_, _) => _plus.Actif = Champ.Valeur.Trim().Length > 0;
        Champ.Champ.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true; // Entrée ajoute le brevet au lieu de valider la fenêtre.
                AjouterSaisie();
            }
        };
        _plus.Click += (_, _) => AjouterSaisie();
        // Mis à l'échelle de l'écran après le premier placement : replacé à sa nouvelle largeur.
        Champ.SizeChanged += (_, _) =>
        {
            if (!_placement)
            {
                Replacer();
            }
        };
        Height = HauteurPuce;
        AutoSize = true;
    }

    /// <summary>Champ « Autre brevet… ».</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ChampSaisie Champ { get; }

    /// <summary>Brevets choisis, dans l'ordre d'ajout.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<string> Brevets
    {
        get => _brevets;
        set
        {
            _brevets.Clear();
            foreach (var b in value)
            {
                Ajouter(b, prevenir: false);
            }

            Replacer();
        }
    }

    /// <summary>Un brevet a été ajouté ou retiré.</summary>
    public event EventHandler? BrevetsChanges;

    /// <summary>Le brevet est-il choisi (majuscules ignorées) ?</summary>
    public bool Contient(string brevet) => _brevets.Contains(brevet, StringComparer.CurrentCultureIgnoreCase);

    /// <summary>Ajoute ou retire un brevet (« Nitrox » suit l'interrupteur « Certifié nitrox »).</summary>
    public void Definir(string brevet, bool present)
    {
        if (present == Contient(brevet))
        {
            return;
        }

        if (present)
        {
            Ajouter(brevet, prevenir: true);
        }
        else
        {
            Retirer(brevet);
        }
    }

    private void AjouterSaisie()
    {
        var brevet = ServiceAdherents.BrevetReconnu(Champ.Valeur);
        if (brevet.Length == 0)
        {
            return;
        }

        Ajouter(brevet, prevenir: true);
        Champ.Valeur = "";
        Champ.Champ.Focus();
    }

    private void Ajouter(string brevet, bool prevenir)
    {
        brevet = ServiceAdherents.BrevetReconnu(brevet);
        if (brevet.Length == 0 || Contient(brevet))
        {
            return;
        }

        _brevets.Add(brevet);
        if (prevenir)
        {
            Replacer();
            BrevetsChanges?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Retirer(string brevet)
    {
        _brevets.RemoveAll(b => string.Equals(b, brevet, StringComparison.CurrentCultureIgnoreCase));
        Replacer();
        BrevetsChanges?.Invoke(this, EventArgs.Empty);
    }

    private IEnumerable<(string Brevet, bool Choisi)> Puces() =>
        _brevets.Select(b => (b, true)).Concat(_proposes.Where(p => !Contient(p)).Select(p => (p, false)));

    public override Size GetPreferredSize(Size proposedSize)
    {
        var largeur = MesureFormulaire.Largeur(this, proposedSize);
        Placer(largeur);
        return new Size(largeur, _hauteur);
    }

    private void Replacer()
    {
        Placer(Width);
        if (Height != _hauteur)
        {
            Parent?.PerformLayout(this, nameof(Height));
        }

        Invalidate();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Replacer();
    }

    private void Placer(int largeur)
    {
        if (largeur <= 0)
        {
            return;
        }

        _zones.Clear();
        var h = E(HauteurPuce);
        float x = 0, y = 0;
        var police = Theme.PoliceGrasse(9.75f);
        foreach (var (brevet, choisi) in Puces())
        {
            // Choisie : texte puis rond × de 30 ; proposée : + puis texte.
            var l = TextRenderer.MeasureText(brevet, police).Width + E(choisi ? 14 + 4 + 30 + 4 : 14 + 13 + 6 + 14);
            if (x > 0 && x + l > largeur)
            {
                x = 0;
                y += h + E(Ecart);
            }

            _zones.Add((new RectangleF(x, y, l, h), brevet, choisi));
            x += l + E(Ecart);
        }

        var lChamp = Champ.Width;
        if (x > 0 && x + lChamp > largeur)
        {
            x = 0;
            y += h + E(Ecart);
        }

        _zoneChamp = new Rectangle((int)x, (int)y, lChamp, (int)Math.Ceiling(h));
        if (Champ.Bounds != _zoneChamp)
        {
            _placement = true;
            Champ.Bounds = _zoneChamp;
            _placement = false;
        }

        _hauteur = (int)Math.Ceiling(y + h) + 1;
        _focus = Math.Min(_focus, Math.Max(0, _zones.Count - 1));
    }

    protected override void Peindre(Graphics g)
    {
        var police = Theme.PoliceGrasse(9.75f);
        for (var i = 0; i < _zones.Count; i++)
        {
            var (zone, brevet, choisi) = _zones[i];
            var r = new RectangleF(zone.X + 0.5f, zone.Y + 0.5f, zone.Width - 1, zone.Height - 1);
            using var chemin = Dessin.Arrondi(r, r.Height / 2);
            var survol = i == _survol;
            if (choisi)
            {
                using var fond = new SolidBrush(Theme.Teinte(Theme.Accent, survol ? 0.2f : 0.13f));
                g.FillPath(fond, chemin);
                using var stylo = new Pen(Theme.Teinte(Theme.Accent, 0.45f), E(1));
                g.DrawPath(stylo, chemin);
                var texte = new RectangleF(zone.X + E(14), zone.Y, zone.Width - E(14 + 34), zone.Height);
                Dessin.Texte(g, brevet, police, Rectangle.Round(texte), Theme.AccentClair);
                var croix = new RectangleF(zone.Right - E(34), zone.Y + ((zone.Height - E(30)) / 2), E(30), E(30));
                if (survol)
                {
                    using var rond = new SolidBrush(Theme.Teinte(Theme.Accent, 0.3f));
                    g.FillEllipse(rond, croix);
                }

                Icones.Dessiner(g, Icones.Croix, RectangleF.Inflate(croix, -E(8), -E(8)), Theme.AccentClair, 2.4f);
            }
            else
            {
                if (survol)
                {
                    using var fond = new SolidBrush(Theme.Teinte(Theme.Accent, 0.08f));
                    g.FillPath(fond, chemin);
                }

                using var stylo = new Pen(survol ? Theme.AccentClair : Theme.Sur(Theme.Surface, Theme.AccentClair, 0.4f), E(1)) { DashStyle = DashStyle.Dash };
                g.DrawPath(stylo, chemin);
                var plus = new RectangleF(zone.X + E(14), zone.Y + ((zone.Height - E(13)) / 2), E(13), E(13));
                Icones.Dessiner(g, Icones.Plus, plus, Theme.Texte, 2.4f);
                var texte = new RectangleF(plus.Right + E(6), zone.Y, zone.Right - plus.Right - E(6), zone.Height);
                Dessin.Texte(g, brevet, Theme.PoliceGrasse(9.25f), Rectangle.Round(texte), Theme.Texte);
            }

            if (Focused && i == _focus)
            {
                using var focus = new Pen(Theme.AccentClair, E(2));
                using var autour = Dessin.Arrondi(RectangleF.Inflate(r, E(2), E(2)), (r.Height / 2) + E(2));
                g.DrawPath(focus, autour);
            }
        }
    }

    private int Sous(Point p) => _zones.FindIndex(z => z.Zone.Contains(p));

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
        if (Sous(e.Location) is var i and >= 0)
        {
            Focus();
            _focus = i;
            Basculer(i);
        }
    }

    private void Basculer(int i)
    {
        var (_, brevet, choisi) = _zones[i];
        if (choisi)
        {
            Retirer(brevet);
        }
        else
        {
            Ajouter(brevet, prevenir: true);
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_zones.Count == 0)
        {
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Up:
                _focus = Math.Max(0, _focus - 1);
                break;
            case Keys.Right or Keys.Down:
                _focus = Math.Min(_zones.Count - 1, _focus + 1);
                break;
            case Keys.Space or Keys.Enter:
                Basculer(_focus);
                break;
            case Keys.Delete or Keys.Back when _zones[_focus].Choisi:
                Basculer(_focus);
                break;
            default:
                return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;
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

    /// <summary>Rond + au bout du champ « Autre brevet… » : turquoise dès qu'un texte est tapé.</summary>
    private sealed class BoutonPlus : ControleDessine
    {
        private bool _actif;

        public BoutonPlus()
        {
            TabStop = false;
            Cursor = Cursors.Hand;
            AccessibleName = "Ajouter ce brevet";
            AccessibleRole = AccessibleRole.PushButton;
            Size = new Size(30, 30);
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Actif
        {
            get => _actif;
            set => Invalider(ref _actif, value);
        }

        public override Size GetPreferredSize(Size proposedSize) => Size;

        protected override void Peindre(Graphics g)
        {
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var fond = new SolidBrush(_actif ? Theme.Accent : Theme.Teinte(Theme.AccentClair, 0.12f)))
            {
                g.FillEllipse(fond, r);
            }

            Icones.Dessiner(g, Icones.Plus, RectangleF.Inflate(r, -E(8), -E(8)), _actif ? Theme.TexteSurAccent : Theme.TexteSecondaire, 2.4f);
        }
    }
}
