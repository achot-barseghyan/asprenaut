using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Media;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Saisie = Asprenaut.Materiel.Core.Saisie;

namespace Asprenaut.Materiel.App.Controles;

public enum ModeScan
{
    Depart,
    Retour,
}

public sealed class CodeScanneEventArgs(string code, string? type = null) : EventArgs
{
    /// <summary>Code déjà nettoyé (espaces, préfixe "ASP:", majuscules).</summary>
    public string Code { get; } = code;

    /// <summary>Type écrit sur la médaille (« (Detendeur) A24 … » → Detendeur), ou null.</summary>
    public string? Type { get; } = type;
}

/// <summary>
/// Grande zone de scan. Capte la saisie du pistolet (mode clavier HID : le code puis Entrée)
/// ou une saisie manuelle, et déclenche <see cref="CodeScanne"/>.
/// - Nettoie le code (voir Core.Saisie.CodeScanne).
/// - Sans Entrée : une frappe de pistolet (rafale, voir Core.Saisie.DetecteurRafale) est validée après 150 ms de pause.
/// - Anti-rebond : le même code reçu deux fois en moins de 800 ms est ignoré.
/// - Garde le focus : s'il part ailleurs dans l'écran, il revient au champ après 1 seconde
///   (sauf si l'utilisateur tape dans un autre champ, ou si une autre fenêtre est active).
/// - Retour visuel dans <see cref="Bandeau"/> (vert / jaune / rouge) et sonore (son du club au succès, SystemSounds sinon).
/// Le bandeau n'est pas un enfant de la zone : l'écran le place où il veut.
/// </summary>
public sealed class ScanInput : UserControl, IFondDessine
{
    private const int DelaiRetourFocusMs = 1000;
    private const int CycleSonarMs = 2800;

    private readonly ChampScan _champ;
    private readonly CadreChamp _cadre;
    private readonly System.Windows.Forms.Timer _minuterieFocus = new() { Interval = 200 };
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 40 };
    private readonly Saisie.AntiRebond _antiRebond = new(800);
    private readonly ValidationSansEntree _sansEntree;
    private long _perteFocusMs;
    private ModeScan _mode;
    private string _consigne = "";
    private string _aide = "";
    private string? _dernierCode;
    private RectangleF _zoneSonar;

    public ScanInput()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoScaleMode = AutoScaleMode.Inherit;
        Margin = Padding.Empty;
        BackColor = Theme.Surface;

        _champ = new ChampScan
        {
            BorderStyle = BorderStyle.None,
            Font = Theme.PoliceCode(13f),
            TextAlign = HorizontalAlignment.Center,
            ForeColor = Theme.Texte,
            PlaceholderText = "En attente d'un scan…",
            AccessibleName = "Code scanné",
        };
        _cadre = new CadreChamp(_champ);
        Controls.Add(_cadre);
        Bandeau = new BandeauMessage();

        _champ.KeyDown += SurToucheChamp;
        _sansEntree = new ValidationSansEntree(_champ, Valider);
        _champ.Enter += (_, _) => _perteFocusMs = -1;
        _champ.Leave += (_, _) => _perteFocusMs = Environment.TickCount64;
        _minuterieFocus.Tick += (_, _) => RendreFocusSiBesoin();
        _animation.Tick += (_, _) =>
        {
            if (Visible && !_zoneSonar.IsEmpty)
            {
                Invalidate(Rectangle.Ceiling(_zoneSonar));
            }
        };
        Mode = ModeScan.Depart;
    }

    /// <summary>Code nettoyé, après anti-rebond.</summary>
    public event EventHandler<CodeScanneEventArgs>? CodeScanne;

    /// <summary>Bandeau de résultat du scan, à placer par l'écran (sous la zone, en général).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public BandeauMessage Bandeau { get; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ModeScan Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            _consigne = value == ModeScan.Depart ? "Scannez le matériel qui part" : "Scannez le matériel qui revient";
            _aide = value == ModeScan.Depart ? "Chaque équipement scanné s'ajoute à cette sortie." : "Il passe de « À rendre » à « Rendu ».";
            AccessibleName = _consigne;
            AccessibleDescription = _aide;
            _champ.BackColor = Theme.Sur(Theme.Surface, Theme.Fond, 0.75f);
            _cadre.CouleurBordure = Theme.Sur(Theme.Surface, Couleur, 0.35f);
            PerformLayout();
            Invalidate(true);
        }
    }

    /// <summary>Désactive le retour automatique du focus (par exemple pendant une saisie ailleurs).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool GarderFocus { get; set; } = true;

    /// <summary>Bip au succès, son grave en cas d'erreur.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Sons { get; set; } = true;

    /// <summary>Ignore le même code lu deux fois en moins de 800 ms.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IgnorerDoublesLectures { get; set; } = true;

    /// <summary>Préfixe des étiquettes, retiré à la lecture.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Prefixe { get; set; } = Saisie.CodeScanne.Prefixe;

    /// <summary>Applique les réglages du pistolet (écran Paramètres).</summary>
    public void Configurer(Reglages reglages)
    {
        Sons = reglages.SonsActifs;
        GarderFocus = reglages.GarderFocusScan;
        IgnorerDoublesLectures = reglages.IgnorerDoublesLectures;
        Prefixe = reglages.PrefixeEtiquette;
    }

    private Color Couleur => _mode == ModeScan.Depart ? Theme.Depart : Theme.Retour;

    public void PrendreFocus()
    {
        if (_champ.CanFocus)
        {
            _champ.Focus();
        }
    }

    public void AfficherResultat(ResultatScan resultat) =>
        AfficherMessage(resultat.Niveau, resultat.Message, resultat.Code ?? resultat.Equipement?.Code ?? _dernierCode);

    /// <summary>Affiche un message ; <paramref name="code"/> s'affiche en dessous avec l'heure.</summary>
    public void AfficherMessage(NiveauResultat niveau, string message, string? code = null)
    {
        var detail = code is null ? null : $"{code} · {DateTime.Now.ToString("HH:mm", Libelles.Culture)}";
        Bandeau.Afficher(niveau, message, detail);
        if (!Sons)
        {
            return;
        }

        switch (niveau)
        {
            case NiveauResultat.Succes:
                JouerScanReussi();
                break;
            case NiveauResultat.Avertissement:
                SystemSounds.Exclamation.Play();
                break;
            default:
                SystemSounds.Hand.Play();
                break;
        }
    }

    // Son du club (ressource « Sons.ScanReussi.wav »), chargé une fois ; bip de Windows s'il est illisible.
    private static readonly Lazy<SoundPlayer?> SonScanReussi = new(() =>
    {
        try
        {
            var flux = typeof(ScanInput).Assembly.GetManifestResourceStream("Sons.ScanReussi.wav");
            if (flux is null)
            {
                return null;
            }

            var lecteur = new SoundPlayer(flux);
            lecteur.Load();
            return lecteur;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
        {
            return null;
        }
    });

    private static void JouerScanReussi()
    {
        if (SonScanReussi.Value is { } lecteur)
        {
            // Play est asynchrone : un nouveau scan coupe le son précédent au lieu de s'empiler.
            lecteur.Play();
        }
        else
        {
            SystemSounds.Beep.Play();
        }
    }

    /// <summary>Simule un scan (tests manuels, saisie au clavier).</summary>
    public void Soumettre(string brut)
    {
        var (code, type) = Saisie.CodeScanne.Lire(brut, Prefixe);
        if (code.Length == 0)
        {
            return;
        }

        // L'anti-rebond voit tous les codes (il retient le dernier), mais ne bloque que si le réglage est actif.
        if (!_antiRebond.Accepter(code, Environment.TickCount64) && IgnorerDoublesLectures)
        {
            return;
        }

        _dernierCode = code;
        CodeScanne?.Invoke(this, new CodeScanneEventArgs(code, type));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _perteFocusMs = 0; // Prend le focus dès que l'écran est affiché.
        _minuterieFocus.Start();
        _animation.Start();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_cadre is null)
        {
            return;
        }

        var (sonar, haut, _, _, champ) = Disposition();
        _zoneSonar = sonar <= 0
            ? RectangleF.Empty
            : RectangleF.Inflate(new RectangleF((Width - sonar) / 2f, haut, sonar, sonar), sonar * 0.32f, sonar * 0.32f);
        var largeur = (int)Math.Min(Width - E(64), E(440));
        _cadre.SetBounds((Width - largeur) / 2, (int)champ, largeur, (int)Math.Min(E(56), Math.Max(E(40), Height - E(8))));
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        Dessin.EffacerAvecParent(this, g);
        Dessin.Qualite(g);
        var couleur = Couleur;
        var r = new RectangleF(0.75f, 0.75f, Width - 2.5f, Height - 2.5f);
        using var chemin = Dessin.Arrondi(r, E(Theme.RayonZoneScan));
        using (var fond = new SolidBrush(Theme.Surface))
        {
            g.FillPath(fond, chemin);
        }

        // Lueur colorée autour du sonar (radial-gradient de la maquette).
        var (sonar, haut, _, _, _) = Disposition();
        var centre = new PointF(Width / 2f, haut + (sonar / 2));
        var rayon = Math.Max(Width, Height) * 0.55f;
        using (var lueur = new GraphicsPath())
        {
            lueur.AddEllipse(centre.X - rayon, centre.Y - (rayon * 0.8f), rayon * 2, rayon * 1.6f);
            using var degrade = new PathGradientBrush(lueur)
            {
                CenterPoint = centre,
                CenterColor = Theme.Teinte(couleur, 0.16f),
                SurroundColors = [Theme.Surface],
            };
            g.SetClip(chemin);
            g.FillPath(degrade, lueur);
            g.ResetClip();
        }

        using (var bord = new Pen(Theme.Sur(Theme.Surface, couleur, 0.55f), E(1.5f)))
        {
            g.DrawPath(bord, chemin);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Dessin.Qualite(g);
        var couleur = Couleur;
        var (sonar, haut, titre, aide, _) = Disposition();
        var centre = new PointF(Width / 2f, haut + (sonar / 2));
        var policeTitre = Theme.PoliceGrasse(_mode == ModeScan.Depart ? 19.5f : 18f);
        if (titre >= 0)
        {
            Dessin.Texte(g, _consigne, policeTitre, new Rectangle(0, (int)titre, Width, TextRenderer.MeasureText(_consigne, policeTitre).Height), Theme.Texte, centre: true);
        }

        if (sonar <= 0)
        {
            return; // Mode compact : consigne et champ seulement.
        }

        // Ondes du sonar : trois cercles qui grandissent et s'effacent, décalés d'un tiers de cycle.
        using (var clip = Dessin.Arrondi(new RectangleF(1, 1, Width - 3, Height - 3), E(Theme.RayonZoneScan)))
        {
            g.SetClip(clip);
            var temps = Environment.TickCount64;
            for (var i = 0; i < 3; i++)
            {
                var phase = ((temps + (i * CycleSonarMs / 3)) % CycleSonarMs) / (float)CycleSonarMs;
                var rayon = sonar / 2 * (0.55f + (1.05f * phase));
                using var onde = new Pen(Theme.Alpha(couleur, 0.75f * (1 - phase)), E(2));
                g.DrawEllipse(onde, centre.X - rayon, centre.Y - rayon, rayon * 2, rayon * 2);
            }

            g.ResetClip();
        }

        // Pastille centrale avec le pictogramme QR.
        var d = sonar * 0.64f;
        var pastille = new RectangleF(centre.X - (d / 2), centre.Y - (d / 2), d, d);
        using (var forme = new GraphicsPath())
        {
            forme.AddEllipse(pastille);
            using var degrade = new PathGradientBrush(forme)
            {
                CenterPoint = new PointF(pastille.X + (d * 0.3f), pastille.Y + (d * 0.25f)),
                CenterColor = Theme.Sur(couleur, Color.White, 0.45f),
                SurroundColors = [Theme.Assombrir(couleur, 0.12f)],
            };
            g.FillPath(degrade, forme);
        }

        var icone = d * 0.46f;
        Icones.Dessiner(g, Icones.Qr, new RectangleF(centre.X - (icone / 2), centre.Y - (icone / 2), icone, icone),
            _mode == ModeScan.Depart ? Theme.TexteSurDepart : Theme.TexteSurRetour);

        // Aide sous la consigne.
        var policeAide = Theme.Police(11f);
        Dessin.Texte(g, _aide, policeAide, new Rectangle(0, (int)aide, Width, TextRenderer.MeasureText(_aide, policeAide).Height), Theme.TexteSecondaireClair, centre: true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _minuterieFocus.Dispose();
            _animation.Dispose();
            _sansEntree.Dispose();
        }

        base.Dispose(disposing);
    }

    private float E(float pixelsLogiques) => Dessin.Echelle(this, pixelsLogiques);

    /// <summary>
    /// Position verticale des éléments : le sonar rétrécit si la zone est basse ; en dessous d'un seuil
    /// (petit écran), il disparaît avec l'aide : il reste la consigne et le champ, toujours utilisables.
    /// </summary>
    private (float Sonar, float Haut, float Titre, float Aide, float Champ) Disposition()
    {
        var hTitre = TextRenderer.MeasureText("Ag", Theme.PoliceGrasse(19.5f)).Height;
        var hAide = TextRenderer.MeasureText("Ag", Theme.Police(11f)).Height;
        var fixe = E(22) + hTitre + E(6) + hAide + E(22) + E(56);
        if (Height < hTitre + E(16) + E(56) + E(16))
        {
            return (0, 0, -1000, -1000, Math.Max(E(4), (Height - E(56)) / 2)); // Champ seul.
        }

        if (Height < fixe + E(56) + E(40))
        {
            var bloc = hTitre + E(16) + E(56);
            var hautCompact = Math.Max(E(8), (Height - bloc) / 2);
            return (0, hautCompact, hautCompact, -1000, hautCompact + hTitre + E(16));
        }

        var marge = Math.Clamp((Height - fixe - E(150)) / 2, E(20), E(44));
        var sonar = Math.Clamp(Height - fixe - (2 * marge), E(56), E(150));
        var total = sonar + fixe;
        var haut = Math.Max(marge, (Height - total) / 2);
        var titre = haut + sonar + E(22);
        var aide = titre + hTitre + E(6);
        var champ = aide + hAide + E(22);
        return (sonar, haut, titre, aide, champ);
    }

    private void SurToucheChamp(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true; // Pas de "ding" Windows.
        Valider();
    }

    private void Valider()
    {
        _sansEntree.Annuler();
        var brut = _champ.Text;
        _champ.Clear();
        Soumettre(brut);
    }

    private void RendreFocusSiBesoin()
    {
        if (!GarderFocus || _perteFocusMs < 0 || !Visible || !_champ.CanFocus)
        {
            return;
        }

        if (Environment.TickCount64 - _perteFocusMs < DelaiRetourFocusMs)
        {
            return;
        }

        var fenetre = FindForm();
        if (fenetre is null || Form.ActiveForm != fenetre)
        {
            return; // Une boîte de dialogue ou une autre application est au premier plan.
        }

        var actif = ControleActif(fenetre);
        if (actif is TextBoxBase && actif != _champ)
        {
            return; // L'utilisateur tape dans un autre champ.
        }

        if (actif is ComboBox { DroppedDown: true })
        {
            return;
        }

        _champ.Focus();
    }

    private static Control? ControleActif(ContainerControl conteneur)
    {
        Control? actif = conteneur.ActiveControl;
        while (actif is ContainerControl c && c.ActiveControl is not null)
        {
            actif = c.ActiveControl;
        }

        return actif;
    }

    /// <summary>TextBox qui reçoit Entrée même si la fenêtre a un bouton par défaut.</summary>
    private sealed class ChampScan : TextBox
    {
        protected override bool IsInputKey(Keys keyData) =>
            (keyData & Keys.KeyCode) == Keys.Enter || base.IsInputKey(keyData);
    }

    /// <summary>Cadre arrondi sombre autour du champ de scan.</summary>
    private sealed class CadreChamp : Panel
    {
        private readonly TextBox _champ;
        private Color _bordure = Theme.BordureForte;

        public CadreChamp(TextBox champ)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _champ = champ;
            BackColor = Theme.Sur(Theme.Surface, Theme.Fond, 0.75f);
            Margin = Padding.Empty;
            Cursor = Cursors.IBeam;
            Controls.Add(champ);
            Click += (_, _) => champ.Focus();
            champ.Enter += (_, _) => Invalidate();
            champ.Leave += (_, _) => Invalidate();
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color CouleurBordure
        {
            get => _bordure;
            set
            {
                _bordure = value;
                Invalidate();
            }
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            var marge = (int)Dessin.Echelle(this, 18);
            _champ.SetBounds(marge, (Height - _champ.Height) / 2, Width - (2 * marge), _champ.Height);
        }

        protected override void OnPaintBackground(PaintEventArgs e) =>
            Dessin.PeindreFondArrondi(this, e.Graphics, BackColor, _champ.Focused ? Theme.Accent : _bordure, 16, 1.5f);
    }
}

/// <summary>Bandeau de résultat du scan : pastille ✓ / ! / ✕, message, puis code et heure.</summary>
public sealed class BandeauMessage : ControleDessine
{
    private NiveauResultat? _niveau;
    private string? _detail;
    private bool _detailEnCode = true;

    public BandeauMessage()
    {
        Text = "Aucun scan pour l'instant.";
        AccessibleRole = AccessibleRole.StatusBar;
        Height = 76;
    }

    public void Afficher(NiveauResultat niveau, string message, string? detail = null, bool detailEnCode = true)
    {
        _niveau = niveau;
        _detail = detail;
        _detailEnCode = detailEnCode;
        Text = message;
        AccessibleName = message;
        AccessibleDescription = detail;
        Invalidate();
    }

    protected override void Peindre(Graphics g)
    {
        var (fond, texte, pastille) = _niveau switch
        {
            NiveauResultat.Succes => Theme.CouleursSucces,
            NiveauResultat.Avertissement => Theme.CouleursAvertissement,
            NiveauResultat.Erreur => Theme.CouleursErreur,
            _ => (Theme.Surface, Theme.TexteSecondaireClair, Theme.SurfaceActive),
        };
        var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
        using (var chemin = Dessin.Arrondi(r, E(18)))
        {
            using var pinceau = new SolidBrush(fond);
            g.FillPath(pinceau, chemin);
            using var bord = new Pen(_niveau is null ? Theme.Bordure : Theme.Sur(fond, pastille, 0.35f), E(1));
            g.DrawPath(bord, chemin);
        }

        // Pastille ronde avec le symbole.
        var d = E(36);
        var x = E(18);
        var cercle = new RectangleF(x, (Height - d) / 2, d, d);
        using (var plein = new SolidBrush(pastille))
        {
            g.FillEllipse(plein, cercle);
        }

        var (symbole, couleurSymbole, epaisseur) = _niveau switch
        {
            NiveauResultat.Succes => (Icones.Coche, Theme.TexteSurRetour, 2.8f),
            NiveauResultat.Avertissement => (Icones.Exclamation, Theme.TexteSurDepart, 3f),
            NiveauResultat.Erreur => (Icones.Croix, Theme.TexteSurDepart, 2.8f),
            _ => (Icones.Qr, Theme.TexteSecondaire, 1.8f),
        };
        Icones.Dessiner(g, symbole, RectangleF.Inflate(cercle, -E(8), -E(8)), couleurSymbole, epaisseur);

        // Message (deux lignes au plus) puis détail "CODE · 19:41".
        var gauche = (int)(cercle.Right + E(14));
        var largeur = Width - gauche - (int)E(16);
        var police = _niveau is null ? Theme.Police(11f) : Theme.PoliceTitre(11.25f);
        var policeDetail = _detailEnCode ? Theme.PoliceCode(9.75f) : Theme.Police(9.75f);
        var drapeaux = TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        var hMessage = Math.Min(TextRenderer.MeasureText(g, Text, police, new Size(largeur, 0), drapeaux).Height, (int)(Height - E(16)));
        var detail = _detail;
        var hDetail = detail is null ? 0 : TextRenderer.MeasureText(g, detail, policeDetail).Height;
        if (hMessage + hDetail > Height - E(12))
        {
            detail = null; // Bandeau trop bas : le message passe avant le code et l'heure.
            hDetail = 0;
        }

        var y = (Height - hMessage - hDetail) / 2;
        TextRenderer.DrawText(g, Text, police, new Rectangle(gauche, y, largeur, hMessage), texte, drapeaux);
        if (detail is not null)
        {
            var couleurDetail = _niveau switch
            {
                NiveauResultat.Succes => Theme.RetourClair,
                NiveauResultat.Avertissement => Theme.Avertissement,
                _ => Theme.Erreur,
            };
            Dessin.Texte(g, detail, policeDetail, new Rectangle(gauche, y + hMessage, largeur, hDetail), couleurDetail);
        }
    }
}
