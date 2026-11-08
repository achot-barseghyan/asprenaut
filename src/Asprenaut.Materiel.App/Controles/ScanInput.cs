using System.ComponentModel;
using System.Media;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.Core.Resultats;
using Saisie = Asprenaut.Materiel.Core.Saisie;

namespace Asprenaut.Materiel.App.Controles;

public enum ModeScan
{
    Depart,
    Retour,
}

public sealed class CodeScanneEventArgs(string code) : EventArgs
{
    /// <summary>Code déjà nettoyé (espaces, préfixe "ASP:", majuscules).</summary>
    public string Code { get; } = code;
}

/// <summary>
/// Grande zone de scan. Capte la saisie du pistolet (mode clavier HID : le code puis Entrée)
/// ou une saisie manuelle, et déclenche <see cref="CodeScanne"/>.
/// - Nettoie le code (voir Core.Saisie.CodeScanne).
/// - Anti-rebond : le même code reçu deux fois en moins de 800 ms est ignoré.
/// - Garde le focus : s'il part ailleurs dans l'écran, il revient au champ après 1 seconde
///   (sauf si l'utilisateur tape dans un autre champ, ou si une autre fenêtre est active).
/// - Retour visuel (bandeau vert / jaune / rouge) et sonore (SystemSounds).
/// </summary>
public sealed class ScanInput : UserControl
{
    private const int DelaiRetourFocusMs = 1000;

    private readonly ChampScan _champ;
    private readonly Label _consigne;
    private readonly BandeauMessage _bandeau;
    private readonly System.Windows.Forms.Timer _minuterieFocus = new() { Interval = 200 };
    private readonly Saisie.AntiRebond _antiRebond = new(800);
    private long _perteFocusMs;
    private ModeScan _mode;

    public ScanInput()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoScaleMode = AutoScaleMode.Inherit;
        Padding = new Padding(28, 20, 28, 20);
        Margin = new Padding(0, 0, 0, 16);
        Height = 240;

        _consigne = Ui.Libelle("", Theme.PoliceTitre(13f));
        _champ = new ChampScan
        {
            BorderStyle = BorderStyle.None,
            Font = Theme.Police(20f),
            BackColor = Theme.Fond,
            ForeColor = Theme.Texte,
            PlaceholderText = "Scannez ou tapez un code puis Entrée",
            AccessibleName = "Code scanné",
        };
        var cadreChamp = new CadreChamp(_champ);
        _bandeau = new BandeauMessage();

        Controls.Add(Ui.Colonne(
            (_consigne, SizeType.AutoSize, 0),
            (cadreChamp, SizeType.Absolute, 76),
            (_bandeau, SizeType.Absolute, 56)));

        _champ.KeyDown += SurToucheChamp;
        _champ.Enter += (_, _) => _perteFocusMs = -1;
        _champ.Leave += (_, _) => _perteFocusMs = Environment.TickCount64;
        _minuterieFocus.Tick += (_, _) => RendreFocusSiBesoin();
        Mode = ModeScan.Depart;
    }

    /// <summary>Code nettoyé, après anti-rebond.</summary>
    public event EventHandler<CodeScanneEventArgs>? CodeScanne;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ModeScan Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            BackColor = value == ModeScan.Depart ? Theme.FondDepart : Theme.FondRetour;
            _consigne.ForeColor = value == ModeScan.Depart ? Theme.Depart : Theme.Retour;
            _consigne.Text = value == ModeScan.Depart ? "Scannez le matériel qui part" : "Scannez le matériel qui rentre";
            Invalidate(true);
        }
    }

    /// <summary>Désactive le retour automatique du focus (par exemple pendant une saisie ailleurs).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool GarderFocus { get; set; } = true;

    public void PrendreFocus()
    {
        if (_champ.CanFocus)
        {
            _champ.Focus();
        }
    }

    public void AfficherResultat(ResultatScan resultat) => AfficherMessage(resultat.Niveau, resultat.Message);

    public void AfficherMessage(NiveauResultat niveau, string message)
    {
        _bandeau.Afficher(niveau, message);
        switch (niveau)
        {
            case NiveauResultat.Succes:
                SystemSounds.Beep.Play();
                break;
            case NiveauResultat.Avertissement:
                SystemSounds.Exclamation.Play();
                break;
            default:
                SystemSounds.Hand.Play();
                break;
        }
    }

    /// <summary>Simule un scan (tests manuels, saisie au clavier).</summary>
    public void Soumettre(string brut)
    {
        var code = Saisie.CodeScanne.Nettoyer(brut);
        if (code.Length == 0 || !_antiRebond.Accepter(code, Environment.TickCount64))
        {
            return;
        }

        CodeScanne?.Invoke(this, new CodeScanneEventArgs(code));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _perteFocusMs = 0; // Prend le focus dès que l'écran est affiché.
        _minuterieFocus.Start();
    }

    protected override void OnPaintBackground(PaintEventArgs e) =>
        Dessin.PeindreFondArrondi(this, e.Graphics, BackColor, _mode == ModeScan.Depart ? Theme.Depart : Theme.Retour, Theme.RayonZoneScan, 2f);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _minuterieFocus.Dispose();
        }

        base.Dispose(disposing);
    }

    private void SurToucheChamp(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true; // Pas de "ding" Windows.
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

        public CadreChamp(TextBox champ)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _champ = champ;
            BackColor = Theme.Fond;
            Margin = new Padding(0, 8, 0, 8);
            Cursor = Cursors.IBeam;
            Controls.Add(champ);
            Click += (_, _) => champ.Focus();
            champ.Enter += (_, _) => Invalidate();
            champ.Leave += (_, _) => Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            var marge = (int)Dessin.Echelle(this, 18);
            _champ.SetBounds(marge, (Height - _champ.Height) / 2, Width - (2 * marge), _champ.Height);
        }

        protected override void OnPaintBackground(PaintEventArgs e) =>
            Dessin.PeindreFondArrondi(this, e.Graphics, BackColor, _champ.Focused ? Theme.Accent : Theme.BordureForte, Theme.RayonBouton, 1.5f);
    }
}

/// <summary>Bandeau de message coloré sous la zone de scan.</summary>
public sealed class BandeauMessage : Control
{
    private NiveauResultat? _niveau;

    public BandeauMessage()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Font = Theme.PoliceTitre(12f);
        Margin = Padding.Empty;
        Text = "En attente d'un scan…";
    }

    public void Afficher(NiveauResultat niveau, string message)
    {
        _niveau = niveau;
        Text = message;
        AccessibleDescription = message;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (_niveau is not { } niveau)
        {
            g.Clear(Dessin.CouleurParent(this));
            TextRenderer.DrawText(g, Text, Theme.Police(11f), ClientRectangle, Theme.TexteSecondaireClair,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            return;
        }

        var (fond, texte) = niveau switch
        {
            NiveauResultat.Succes => Theme.CouleursSucces,
            NiveauResultat.Avertissement => Theme.CouleursAvertissement,
            _ => Theme.CouleursErreur,
        };
        Dessin.PeindreFondArrondi(this, g, fond, texte, Theme.RayonBouton, 1.5f);
        var icone = niveau switch
        {
            NiveauResultat.Succes => "✓",
            NiveauResultat.Avertissement => "!",
            _ => "✕",
        };
        var marge = (int)Dessin.Echelle(this, 16);
        TextRenderer.DrawText(g, $"{icone}   {Text}", Font, Rectangle.FromLTRB(marge, 0, Width - marge, Height), texte,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
    }
}
