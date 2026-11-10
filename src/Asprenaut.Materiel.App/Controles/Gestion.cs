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

    /// <summary>Visibilité, textes et nom accessible selon l'état du mode gestion.</summary>
    public void Actualiser()
    {
        Visible = _gestion.ProtectionActive;
        var (titre, sous) = Textes();
        AccessibleName = $"{titre}. {sous}";
        Invalidate();
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
        var boite = new RectangleF(E(12), r.Y + ((r.Height - c) / 2), c, c);
        using (var forme = Dessin.Arrondi(boite, E(10)))
        using (var pinceau = new SolidBrush(Theme.Sur(fond, couleur, 0.16f)))
        {
            g.FillPath(pinceau, forme);
        }

        var t = E(18);
        Icones.Dessiner(g, ouvert ? Icones.CadenasOuvert : Icones.Cadenas, new RectangleF(boite.X + ((c - t) / 2), boite.Y + ((c - t) / 2), t, t), couleur, 2f);

        var (titre, sous) = Textes();
        var gauche = (int)(boite.Right + E(12));
        var largeur = Width - gauche - (int)E(8);
        var milieu = r.Y + (r.Height / 2);
        Dessin.Texte(g, titre, Theme.PoliceTitre(9.75f), new Rectangle(gauche, (int)(milieu - E(20)), largeur, (int)E(20)), Theme.Texte);
        Dessin.Texte(g, sous, Theme.PoliceTitre(9f), new Rectangle(gauche, (int)(milieu + E(1)), largeur, (int)E(19)), ouvert ? Theme.RetourClair : Theme.Accent);
    }
}
