using System.ComponentModel;
using System.Drawing.Drawing2D;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>
/// Avant une action protégée : si le mode gestion n'est pas ouvert, demande le code (pavé numérique).
/// La règle elle-même est dans Core (<see cref="ServiceGestion"/>) ; les services refusent aussi l'action.
/// </summary>
public sealed class GardeGestion(ServiceGestion gestion)
{
    public ServiceGestion Gestion => gestion;

    /// <summary>Vrai si l'action peut se faire (déjà permise, ou code accepté).</summary>
    public bool Exiger(IWin32Window? proprietaire, ActionProtegee action, string libelleAction)
    {
        if (gestion.EstAutorise(action))
        {
            return true;
        }

        using var dialogue = new DialogueDeverrouillage(gestion, libelleAction);
        return dialogue.ShowDialog(proprietaire) == DialogResult.OK && gestion.EstAutorise(action);
    }

    /// <summary>
    /// Affiche le cadenas sur un bouton (ou le badge sur une carte) tant que l'action est protégée, et le met à jour seul
    /// à chaque changement de mode (déverrouillage, verrouillage, expiration, code défini ou retiré).
    /// <paramref name="condition"/> : condition supplémentaire (ex. du matériel encore dehors pour la clôture).
    /// Retourne l'action de mise à jour, à rappeler si la condition dépend de données rechargées.
    /// </summary>
    public Action Lier<T>(T controle, ActionProtegee action, Func<bool>? condition = null)
        where T : Control, IVerrouillable
    {
        void Actualiser()
        {
            if (!controle.IsDisposed)
            {
                controle.Verrouille = !gestion.EstAutorise(action) && (condition?.Invoke() ?? true);
            }
        }

        void SurChangement(object? sender, EventArgs e)
        {
            if (controle.IsHandleCreated && controle.InvokeRequired)
            {
                controle.BeginInvoke(Actualiser);
            }
            else
            {
                Actualiser();
            }
        }

        gestion.EtatChange += SurChangement;
        controle.Disposed += (_, _) => gestion.EtatChange -= SurChangement;
        Actualiser();
        return Actualiser;
    }

    /// <summary>Ouvre le mode gestion sans action en attente (carte du menu).</summary>
    public bool Deverrouiller(IWin32Window? proprietaire)
    {
        using var dialogue = new DialogueDeverrouillage(gestion, null);
        return dialogue.ShowDialog(proprietaire) == DialogResult.OK;
    }
}

/// <summary>Contrôle qui montre qu'il demande le code gestionnaire (cadenas, badge).</summary>
public interface IVerrouillable
{
    bool Verrouille { get; set; }
}

/// <summary>
/// Pastille « verrouillé » de la maquette : rond jaune de 24 px avec un cadenas foncé, accroché au coin haut droit
/// d'un contrôle et débordant de 9 px, détaché du fond par un liseré de 3 px ; info-bulle « Code gestionnaire requis ».
/// Un contrôle ne peint pas hors de ses bords : la partie qui déborde est peinte par chacun de ses parents opaques
/// (événement Paint ; chacun recouvre ce que peint le suivant, les parents transparents la recopient avec leur fond),
/// la partie intérieure par le contrôle lui-même (<see cref="PeindreInterieur"/>, à la fin de son OnPaint), avec la
/// même géométrie. Rien ne change de taille : la mise en page ne bouge pas au verrouillage.
/// </summary>
public sealed class PastilleVerrou : IDisposable
{
    public const string TexteBulle = "Code gestionnaire requis";
    private const float Diametre = 24, Debord = 9, Lisere = 3, Halo = 8;
    private readonly Control _controle;
    private readonly ToolTip _bulle = new() { InitialDelay = 300 };
    private readonly List<Control> _hotes = [];
    private readonly Dictionary<Control, Rectangle> _zones = [];
    private bool _visible;

    public PastilleVerrou(Control controle)
    {
        _controle = controle;
        controle.ParentChanged += (_, _) => Rafraichir();
        controle.LocationChanged += (_, _) => Rafraichir();
        controle.SizeChanged += (_, _) => Rafraichir();
        controle.VisibleChanged += (_, _) => Rafraichir();
        controle.Disposed += (_, _) => Dispose();
    }

    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value)
            {
                return;
            }

            _visible = value;
            Bulle(_controle, value ? TexteBulle : null);
            Rafraichir();
            _controle.Invalidate(true);
        }
    }

    /// <summary>Partie de la pastille posée sur le contrôle ; à appeler à la fin de son OnPaint.</summary>
    public void PeindreInterieur(Graphics g)
    {
        if (!_visible)
        {
            return;
        }

        // Les parents peuvent avoir changé depuis (rangée ajoutée à la page après le bouton).
        if (!Hotes().SequenceEqual(_hotes))
        {
            Rafraichir();
        }

        Dessiner(g, _controle, new RectangleF(0, 0, _controle.Width, _controle.Height));
    }

    public void Dispose()
    {
        Accrocher([]);
        _bulle.Dispose();
    }

    /// <summary>Info-bulle sur le contrôle et ses enfants (ils reçoivent la souris à sa place).</summary>
    private void Bulle(Control c, string? texte)
    {
        _bulle.SetToolTip(c, texte);
        foreach (Control enfant in c.Controls)
        {
            Bulle(enfant, texte);
        }
    }

    /// <summary>Parents qui peignent leur fond (même règle que Dessin.EffacerAvecParent), du plus proche à la fenêtre.</summary>
    private List<Control> Hotes()
    {
        var hotes = new List<Control>();
        for (var p = _controle.Parent; p is not null; p = p.Parent)
        {
            if (p is IFondDessine || p.BackColor.A == 255)
            {
                hotes.Add(p);
            }
        }

        return hotes;
    }

    private void Accrocher(List<Control> hotes)
    {
        if (hotes.SequenceEqual(_hotes))
        {
            return;
        }

        foreach (var ancien in _hotes)
        {
            ancien.Paint -= PeindreSurHote;
            if (ancien.IsDisposed)
            {
                _zones.Remove(ancien);
                continue;
            }

            // Zone de la dernière peinture, et place actuelle : un parent intermédiaire a pu bouger depuis
            // sans prévenir (le contrôle garde la même Location), et la pastille resterait à moitié affichée.
            if (_zones.Remove(ancien, out var zone))
            {
                ancien.Invalidate(zone, true);
            }

            ancien.Invalidate(Zone(ancien), true);
        }

        _hotes.Clear();
        _hotes.AddRange(hotes);
        foreach (var hote in hotes)
        {
            hote.Paint += PeindreSurHote;
        }
    }

    /// <summary>Repeint l'ancienne et la nouvelle place de la pastille chez chaque parent opaque.</summary>
    private void Rafraichir()
    {
        if (_controle.IsDisposed)
        {
            return;
        }

        Accrocher(_visible ? Hotes() : []);
        foreach (var hote in _hotes)
        {
            if (_zones.TryGetValue(hote, out var ancienne))
            {
                hote.Invalidate(ancienne, true);
            }

            var zone = Zone(hote);
            _zones[hote] = zone;
            hote.Invalidate(zone, true);
        }
    }

    /// <summary>Bornes du contrôle dans le repère d'un parent (null s'il n'en descend plus).</summary>
    private Rectangle? BornesSur(Control hote)
    {
        var position = Point.Empty;
        for (var c = _controle; c != hote; c = c.Parent)
        {
            position.Offset(c.Location);
            if (c.Parent is null)
            {
                return null;
            }
        }

        return new Rectangle(position, _controle.Size);
    }

    /// <summary>
    /// Partie visible (repère du parent qui peint) des conteneurs défilants situés entre le contrôle et ce parent ;
    /// null s'il n'y en a pas, vide si le contrôle est hors de leur vue.
    /// </summary>
    private Rectangle? VueDefilante(Control hote)
    {
        Rectangle? vue = null;
        for (var p = _controle.Parent; p is not null && p != hote; p = p.Parent)
        {
            if (p is not ScrollableControl { AutoScroll: true })
            {
                continue;
            }

            var position = Point.Empty;
            for (var c = p; c != hote; c = c.Parent)
            {
                position.Offset(c.Location);
                if (c.Parent is null)
                {
                    return null;
                }
            }

            var visible = new Rectangle(position, p.ClientSize);
            vue = vue is { } v ? Rectangle.Intersect(v, visible) : visible;
        }

        return vue;
    }

    private Rectangle Zone(Control hote)
    {
        if (BornesSur(hote) is not { } b)
        {
            return Rectangle.Empty;
        }

        var e = Dessin.Echelle(_controle, 1);
        var (cx, cy) = Centre(b, e);
        var rayon = (int)Math.Ceiling(((Diametre / 2) + Lisere + Halo) * e) + 2;
        return Rectangle.FromLTRB((int)cx - rayon, (int)cy - rayon, (int)cx + rayon, (int)cy + rayon + (int)(4 * e));
    }

    private static (float X, float Y) Centre(RectangleF bornes, float e) =>
        (bornes.Right + ((Debord - (Diametre / 2)) * e), bornes.Top - ((Debord - (Diametre / 2)) * e));

    private void PeindreSurHote(object? sender, PaintEventArgs e)
    {
        if (_visible && sender is Control hote && _controle.Visible && !_controle.IsDisposed && BornesSur(hote) is { } b)
        {
            // Un conteneur défilant entre le contrôle et ce parent cache ce qui sort de sa vue : la pastille aussi
            // (sinon le cadenas d'un bouton descendu sous la fiche défilante apparaissait sur la carte autour).
            var etat = e.Graphics.Save();
            if (VueDefilante(hote) is { } vue)
            {
                if (vue.IsEmpty)
                {
                    e.Graphics.Restore(etat);
                    return;
                }

                e.Graphics.IntersectClip(vue);
            }

            Dessiner(e.Graphics, _controle, b);
            e.Graphics.Restore(etat);

            // Retenir où elle est réellement peinte : c'est cette zone qu'il faudra effacer.
            var zone = Zone(hote);
            if (_zones.TryGetValue(hote, out var ancienne) && ancienne != zone)
            {
                hote.Invalidate(ancienne, true); // Morceau resté à l'ancienne place (parent déplacé).
            }

            _zones[hote] = zone;
        }
    }

    /// <summary>Halo doré, liseré de la couleur du fond, rond jaune et cadenas foncé, centrés sur le coin haut droit.</summary>
    private static void Dessiner(Graphics g, Control controle, RectangleF bornes)
    {
        var e = Dessin.Echelle(controle, 1);
        var (cx, cy) = Centre(bornes, e);
        var r = Diametre / 2 * e;
        RectangleF Rond(float rayon, float decalageY = 0) => new(cx - rayon, cy - rayon + decalageY, 2 * rayon, 2 * rayon);
        var qualite = g.SmoothingMode;
        Dessin.Qualite(g);

        for (var i = 3; i >= 1; i--)
        {
            using var halo = new SolidBrush(Theme.Alpha(Theme.Avertissement, 0.10f));
            g.FillEllipse(halo, Rond(r + (Lisere * e) + (i * Halo / 3 * e), 4 * e));
        }

        using (var lisere = new SolidBrush(Dessin.CouleurParent(controle)))
        {
            g.FillEllipse(lisere, Rond(r + (Lisere * e)));
        }

        using (var fond = new SolidBrush(Theme.Avertissement))
        {
            g.FillEllipse(fond, Rond(r));
        }

        var t = 13 * e;
        Icones.Dessiner(g, Icones.Cadenas, new RectangleF(cx - (t / 2), cy - (t / 2), t, t), Theme.TexteSurAvertissement, 2.6f);
        g.SmoothingMode = qualite;
    }
}

/// <summary>
/// Bandeau doré « Mode consultation » sous le titre d'une page où presque tout est protégé (Révisions, Paramètres) :
/// grand cadenas, texte de la page et bouton jaune « Déverrouiller ». Visible tant que l'action est protégée
/// (<see cref="GardeGestion.Lier{T}"/>) ; masqué sans code gestionnaire ou en mode gestion.
/// </summary>
public sealed class BandeauConsultation : TableLayoutPanel, IVerrouillable, IFondDessine
{
    private readonly Label _texte;
    private readonly BoutonArrondi _bouton;
    private readonly PastilleRonde _cadenas = new() { Margin = new Padding(0, 0, 14, 0), Anchor = AnchorStyles.Left };

    public BandeauConsultation(GardeGestion garde, string texte)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        ColumnCount = 3;
        RowCount = 1;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Fill;
        BackColor = Color.Transparent;
        Padding = new Padding(18, 14, 18, 14);
        Margin = new Padding(0, 0, 0, 18);
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        AccessibleRole = AccessibleRole.Alert;
        AccessibleName = $"Mode consultation. {texte}";

        var titre = Ui.Libelle("Mode consultation", Theme.PoliceGrasse(11.25f), Theme.Avertissement);
        titre.Margin = new Padding(0, 0, 0, 2);
        _texte = Ui.Libelle(texte, Theme.Police(9.75f), Theme.Texte);
        _texte.Margin = Padding.Empty;
        var textes = Ui.Pile(titre, _texte);
        textes.Anchor = AnchorStyles.Left;
        textes.Margin = new Padding(0, 0, 14, 0);
        _bouton = new BoutonArrondi("Déverrouiller", StyleBouton.Verrou, Icones.CadenasOuvert) { Margin = Padding.Empty, Anchor = AnchorStyles.Right };
        _bouton.Height = 46;
        _bouton.Click += (_, _) => garde.Deverrouiller(FindForm());
        Controls.Add(_cadenas, 0, 0);
        Controls.Add(textes, 1, 0);
        Controls.Add(_bouton, 2, 0);
        Visible = false;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Verrouille
    {
        get => Visible;
        set => Visible = value;
    }

    /// <summary>Le texte revient à la ligne dans la place laissée par le cadenas et le bouton.</summary>
    protected override void OnLayout(LayoutEventArgs levent)
    {
        if (_texte is not null && Width > 0)
        {
            var place = Width - Padding.Horizontal - _cadenas.Width - _cadenas.Margin.Horizontal - _bouton.Width - (int)Dessin.Echelle(this, 14);
            var maximum = new Size(Math.Max((int)Dessin.Echelle(this, 160), place), 0);
            if (_texte.MaximumSize != maximum)
            {
                _texte.MaximumSize = maximum;
            }
        }

        base.OnLayout(levent);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        Dessin.EffacerAvecParent(this, g);
        Dessin.Qualite(g);
        var parent = Dessin.CouleurParent(this);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var forme = Dessin.Arrondi(r, Dessin.Echelle(this, 18));
        using var degrade = new LinearGradientBrush(new PointF(0, 0), new PointF(Math.Max(1, Width), 0),
            Theme.Sur(parent, Theme.Avertissement, 0.16f), Theme.Sur(parent, Theme.Avertissement, 0.04f));
        using var bord = new Pen(Theme.Sur(parent, Theme.Avertissement, 0.45f), Dessin.Echelle(this, 1));
        g.FillPath(degrade, forme);
        g.DrawPath(bord, forme);
    }

    /// <summary>Rond jaune de 42 px avec un cadenas foncé.</summary>
    private sealed class PastilleRonde : ControleDessine
    {
        public PastilleRonde()
        {
            Size = new Size(42, 42);
            AccessibleRole = AccessibleRole.Graphic;
            AccessibleName = "Cadenas";
        }

        protected override void Peindre(Graphics g)
        {
            using (var fond = new SolidBrush(Theme.Avertissement))
            {
                g.FillEllipse(fond, new RectangleF(0, 0, Width - 1, Height - 1));
            }

            var t = E(20);
            Icones.Dessiner(g, Icones.Cadenas, new RectangleF((Width - t) / 2, (Height - t) / 2, t, t), Theme.TexteSurAvertissement, 2.4f);
        }
    }
}

/// <summary>Badge « Code requis » (cadenas + texte) posé à côté du titre d'une carte de réglages.</summary>
public sealed class PastilleCodeRequis : ControleDessine
{
    private const string Texte = "Code requis";

    public PastilleCodeRequis()
    {
        Size = new Size(118, 26);
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = "Code gestionnaire requis pour modifier";
    }

    public override Size GetPreferredSize(Size proposedSize) =>
        new((int)(TextRenderer.MeasureText(Texte, Theme.PoliceTitre(8.25f), Size.Empty, TextFormatFlags.NoPadding).Width + E(40)), (int)E(26));

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var voulue = GetPreferredSize(Size.Empty);
        if (Size != voulue)
        {
            Size = voulue;
        }
    }

    protected override void Peindre(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var forme = Dessin.Arrondi(r, r.Height / 2))
        using (var pinceau = new SolidBrush(Theme.Teinte(Theme.Avertissement, 0.14f)))
        {
            g.FillPath(pinceau, forme);
        }

        var t = E(14);
        Icones.Dessiner(g, Icones.Cadenas, new RectangleF(E(10), (Height - t) / 2, t, t), Theme.Avertissement, 2.2f);
        Dessin.Texte(g, Texte, Theme.PoliceTitre(8.25f), new Rectangle((int)E(29), 0, Width - (int)E(31), Height), Theme.Avertissement);
    }
}

/// <summary>Clavier ou souris dans l'application : repousse le retour automatique en consultation.</summary>
public sealed class FiltreActivite(ServiceGestion gestion) : IMessageFilter
{
    private const int WmKeyDown = 0x0100, WmLButtonDown = 0x0201, WmRButtonDown = 0x0204, WmMouseWheel = 0x020A;

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg is WmKeyDown or WmLButtonDown or WmRButtonDown or WmMouseWheel)
        {
            gestion.NoterActivite();
        }

        return false;
    }
}

/// <summary>
/// Fenêtre « Action protégée » (maquette Déverrouillage) : cadenas, points du code, pavé 3 × 4 cliquable,
/// et saisie au clavier (chiffres, Retour arrière, Entrée, Échap). 6 chiffres saisis : validation automatique.
/// </summary>
public sealed class DialogueDeverrouillage : Form
{
    private const int Largeur = 400;
    private readonly ServiceGestion _gestion;
    private readonly PointsCode _points = new() { Height = 30 };
    private readonly Label _message = Ui.Libelle("", Theme.PoliceTitre(10f), Theme.Erreur);
    private string _code = "";

    public DialogueDeverrouillage(ServiceGestion gestion, string? libelleAction)
    {
        _gestion = gestion;
        Text = libelleAction is null ? "Mode gestion" : "Action protégée";
        BackColor = Theme.Surface;
        ForeColor = Theme.Texte;
        Font = Theme.Police();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Icon = Icones.IconeFenetre;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.None;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(30, 26, 30, 22);
        AccessibleName = Text;
        KeyPreview = true; // Les chiffres tapés arrivent à la fenêtre, quel que soit le bouton qui a le focus.

        var page = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, BackColor = Color.Transparent, Margin = Padding.Empty, Width = Largeur };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Largeur));
        void Ligne(Control c, bool centre = true)
        {
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (centre)
            {
                c.Anchor = AnchorStyles.None;
            }
            else
            {
                c.Dock = DockStyle.Fill;
            }

            page.Controls.Add(c, 0, page.RowStyles.Count - 1);
        }

        Ligne(new PastilleCadenas { Margin = new Padding(0, 0, 0, 14) });
        var titre = Ui.Libelle(libelleAction is null ? "Mode gestion" : "Action protégée", Theme.PoliceGrasse(16f));
        titre.Margin = new Padding(0, 0, 0, 6);
        Ligne(titre);
        var sous = Ui.Libelle(
            libelleAction is null
                ? "Entrez le code gestionnaire pour ajouter, modifier ou supprimer."
                : $"« {libelleAction} » demande le code gestionnaire.",
            Theme.Police(10.5f), Theme.TexteSecondaireClair);
        sous.MaximumSize = new Size(Largeur, 0);
        sous.TextAlign = ContentAlignment.MiddleCenter;
        sous.Margin = new Padding(0, 0, 0, 14);
        Ligne(sous);
        _points.Margin = new Padding(0, 0, 0, 4);
        Ligne(_points, centre: false);
        _message.AutoSize = false;
        _message.Height = 26;
        _message.TextAlign = ContentAlignment.MiddleCenter;
        _message.Margin = new Padding(0, 0, 0, 8);
        _message.AccessibleRole = AccessibleRole.Alert;
        Ligne(_message, centre: false);

        // Pavé : 1 2 3 / 4 5 6 / 7 8 9 / effacer 0 OK
        var pave = new TableLayoutPanel { ColumnCount = 3, RowCount = 4, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 14), Height = 4 * 64 };
        for (var i = 0; i < 3; i++)
        {
            pave.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        }

        for (var i = 0; i < 4; i++)
        {
            pave.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        }

        string[] touches = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "effacer", "0", "ok"];
        for (var i = 0; i < touches.Length; i++)
        {
            var t = touches[i];
            BoutonArrondi bouton = t switch
            {
                "effacer" => new BoutonArrondi("Effacer", StyleBouton.Icone, Icones.Effacer) { AccessibleName = "Effacer" },
                "ok" => new BoutonArrondi("OK", StyleBouton.Principal) { AccessibleName = "Valider" },
                _ => new BoutonArrondi(t, StyleBouton.Secondaire) { Font = Theme.PoliceTitre(15f), AccessibleName = $"Chiffre {t}" },
            };
            bouton.AutoSize = false;
            bouton.Dock = DockStyle.Fill;
            bouton.Margin = new Padding(5);
            bouton.TabStop = false; // Le clavier tape le code, il ne déplace pas le focus.
            bouton.Click += (_, _) =>
            {
                switch (t)
                {
                    case "effacer":
                        Effacer();
                        break;
                    case "ok":
                        Valider();
                        break;
                    default:
                        Ajouter(t[0]);
                        break;
                }
            };
            pave.Controls.Add(bouton, i % 3, i / 3);
        }

        Ligne(pave, centre: false);
        var aide = Ui.Libelle($"Une fois déverrouillé, plus de code pendant {gestion.Minutes} min sans activité.", Theme.Police(9f), Theme.TexteSecondaire);
        aide.MaximumSize = new Size(Largeur, 0);
        aide.TextAlign = ContentAlignment.MiddleCenter;
        aide.Margin = new Padding(0, 0, 0, 6);
        Ligne(aide);
        var annuler = new BoutonArrondi("Annuler", StyleBouton.Lien) { Height = Theme.HauteurCliquable, Margin = Padding.Empty, DialogResult = DialogResult.Cancel, TabStop = false };
        Ligne(annuler);
        CancelButton = annuler;

        Controls.Add(page);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Natif.BarreDeTitre(this);
    }

    protected override void OnLoad(EventArgs e)
    {
        // Construite en pixels logiques : mise à l'échelle de l'écran.
        foreach (Control c in Controls)
        {
            Dessin.MettreALEchelle(c, this);
        }

        Padding = new Padding((int)Dessin.Echelle(this, 30), (int)Dessin.Echelle(this, 26), (int)Dessin.Echelle(this, 30), (int)Dessin.Echelle(this, 22));
        base.OnLoad(e);
    }

    /// <summary>
    /// Chiffres lus d'après le caractère tapé, pas la touche : sur un clavier AZERTY, la rangée du haut donne « à & é »
    /// sans Maj ; le chiffre arrive avec Maj ou par le pavé numérique, quelle que soit la disposition.
    /// </summary>
    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (char.IsAsciiDigit(e.KeyChar))
        {
            Ajouter(e.KeyChar);
            e.Handled = true;
            return;
        }

        base.OnKeyPress(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Back:
                Effacer();
                return true;
            case Keys.Enter:
                Valider();
                return true;
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    private void Ajouter(char chiffre)
    {
        if (_code.Length >= ServiceGestion.LongueurMax)
        {
            return;
        }

        _code += chiffre;
        _message.Text = "";
        _points.Nombre = _code.Length;
        if (_code.Length == ServiceGestion.LongueurMax)
        {
            Valider();
        }
    }

    private void Effacer()
    {
        if (_code.Length > 0)
        {
            _code = _code[..^1];
            _points.Nombre = _code.Length;
        }
    }

    private void Valider()
    {
        if (_code.Length < ServiceGestion.LongueurMin)
        {
            _message.Text = $"Le code fait au moins {ServiceGestion.LongueurMin} chiffres.";
            return;
        }

        var resultat = _gestion.Deverrouiller(_code);
        if (resultat.Reussi)
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        _code = "";
        _points.Nombre = 0;
        _message.Text = resultat.Message;
    }

    /// <summary>Cadenas sur une pastille dorée.</summary>
    private sealed class PastilleCadenas : ControleDessine
    {
        public PastilleCadenas()
        {
            Size = new Size(64, 64);
            AccessibleRole = AccessibleRole.Graphic;
            AccessibleName = "Cadenas";
        }

        protected override void Peindre(Graphics g)
        {
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var forme = new GraphicsPath())
            {
                forme.AddEllipse(r);
                using var degrade = new PathGradientBrush(forme)
                {
                    CenterPoint = new PointF(Width * 0.3f, Height * 0.25f),
                    CenterColor = Theme.Eclaircir(Theme.Avertissement, 0.35f),
                    SurroundColors = [Theme.Assombrir(Theme.Avertissement, 0.1f)],
                };
                g.FillPath(degrade, forme);
            }

            Icones.Dessiner(g, Icones.Cadenas, RectangleF.Inflate(r, -E(17), -E(17)), Theme.EstSombre ? Theme.TexteSurDepart : Color.White, 2f);
        }
    }

    /// <summary>Six ronds : pleins pour les chiffres déjà saisis.</summary>
    private sealed class PointsCode : ControleDessine
    {
        private int _nombre;

        public PointsCode()
        {
            AccessibleRole = AccessibleRole.StaticText;
            AccessibleName = "0 chiffre saisi";
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Nombre
        {
            get => _nombre;
            set
            {
                Invalider(ref _nombre, value);
                AccessibleName = $"{value} chiffre{(value > 1 ? "s" : "")} saisi{(value > 1 ? "s" : "")}";
            }
        }

        protected override void Peindre(Graphics g)
        {
            var d = E(14);
            var ecart = E(12);
            var total = (ServiceGestion.LongueurMax * d) + ((ServiceGestion.LongueurMax - 1) * ecart);
            var x = (Width - total) / 2;
            var y = (Height - d) / 2;
            for (var i = 0; i < ServiceGestion.LongueurMax; i++)
            {
                var r = new RectangleF(x + (i * (d + ecart)), y, d, d);
                if (i < _nombre)
                {
                    using var pinceau = new SolidBrush(Theme.Accent);
                    g.FillEllipse(pinceau, r);
                }
                else
                {
                    using var stylo = new Pen(Theme.BordureForte, E(2));
                    g.DrawEllipse(stylo, RectangleF.Inflate(r, -E(1), -E(1)));
                }
            }
        }
    }
}

/// <summary>
/// Carte du menu : « Mode consultation · Déverrouiller pour modifier » (cadenas jaune) ou
/// « Mode gestion · Se verrouille dans 12 min » (vert). Un clic ouvre le pavé, ou referme le mode gestion.
/// Masquée tant qu'aucun code gestionnaire n'est défini.
/// </summary>
public sealed class CarteModeGestion : Button
{
    private readonly ServiceGestion _gestion;
    private bool _survol;

    public CarteModeGestion(ServiceGestion gestion)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Margin = Padding.Empty;
        Height = 70; // Carte de 58 px + 12 px d'écart avec la carte du pistolet.
        _gestion = gestion;
        Actualiser();
    }

    /// <summary>Menu réduit : seulement le cadenas, centré.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Compact { get; set; }

    /// <summary>Texte de la bulle (menu réduit) : « Mode consultation · Déverrouiller pour modifier ».</summary>
    public string TexteBulle => $"{Textes().Titre} · {Textes().Sous}";

    /// <summary>Changement d'état (le menu réduit met sa bulle à jour).</summary>
    public event EventHandler? Actualise;

    /// <summary>Visibilité, textes et nom accessible selon l'état du mode gestion.</summary>
    public void Actualiser()
    {
        Visible = _gestion.ProtectionActive;
        var (titre, sous) = Textes();
        AccessibleName = $"{titre}. {sous}";
        Invalidate();
        Actualise?.Invoke(this, EventArgs.Empty);
    }

    private (string Titre, string Sous) Textes()
    {
        if (!_gestion.ModeGestion)
        {
            return ("Mode consultation", "Déverrouiller pour modifier");
        }

        var minutes = (int)Math.Ceiling((_gestion.TempsRestant ?? TimeSpan.Zero).TotalMinutes);
        return ("Mode gestion", minutes <= 1 ? "Se verrouille dans 1 min" : $"Se verrouille dans {minutes} min");
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _survol = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _survol = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        float E(float v) => Dessin.Echelle(this, v);
        Dessin.EffacerAvecParent(this, g);
        Dessin.Qualite(g);
        var ouvert = _gestion.ModeGestion;
        var couleur = ouvert ? Theme.Retour : Theme.Avertissement;
        var parent = Dessin.CouleurParent(this);
        var fond = ouvert ? Theme.Sur(parent, Theme.Retour, _survol ? 0.16f : 0.10f) : Theme.Sur(parent, Theme.Fond, _survol ? 0.15f : 0.45f);

        var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - E(12) - 1);
        using (var forme = Dessin.Arrondi(r, E(16)))
        using (var pinceau = new SolidBrush(fond))
        using (var bord = new Pen(Theme.Sur(fond, couleur, 0.35f), E(1)))
        {
            g.FillPath(pinceau, forme);
            g.DrawPath(bord, forme);
            if (Focused && ShowFocusCues)
            {
                using var focus = new Pen(Theme.Accent, E(1.5f));
                using var interieur = Dessin.Arrondi(RectangleF.Inflate(r, -3, -3), E(13));
                g.DrawPath(focus, interieur);
            }
        }

        var c = E(34);
        var boite = new RectangleF(Compact ? r.X + ((r.Width - c) / 2) : E(12), r.Y + ((r.Height - c) / 2), c, c);
        using (var forme = Dessin.Arrondi(boite, E(10)))
        using (var pinceau = new SolidBrush(Theme.Sur(fond, couleur, 0.16f)))
        {
            g.FillPath(pinceau, forme);
        }

        var t = E(18);
        Icones.Dessiner(g, ouvert ? Icones.CadenasOuvert : Icones.Cadenas, new RectangleF(boite.X + ((c - t) / 2), boite.Y + ((c - t) / 2), t, t), couleur, 2f);
        if (Compact)
        {
            return; // Menu réduit : le cadenas seul (fermé en consultation, ouvert en mode gestion) ; le texte est dans la bulle.
        }

        var (titre, sous) = Textes();
        var gauche = (int)(boite.Right + E(12));
        var largeur = Width - gauche - (int)E(8);
        var milieu = r.Y + (r.Height / 2);
        Dessin.Texte(g, titre, Theme.PoliceTitre(9.75f), new Rectangle(gauche, (int)(milieu - E(20)), largeur, (int)E(20)), Theme.Texte);
        Dessin.Texte(g, sous, Theme.PoliceTitre(9f), new Rectangle(gauche, (int)(milieu + E(1)), largeur, (int)E(19)), ouvert ? Theme.RetourClair : Theme.Accent);
    }
}
