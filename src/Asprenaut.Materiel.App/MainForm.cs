using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Ecrans;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App;

/// <summary>Fenêtre unique : menu latéral fixe à gauche, écran courant à droite.</summary>
public sealed class MainForm : Form
{
    private readonly ServiceNavigation _navigation;
    private readonly Dictionary<EntreeMenu, BoutonArrondi> _menu = [];
    private Panel _menuPanneau;
    private readonly Panel _contenu;
    private readonly ServiceGestion _gestion;
    private readonly GardeGestion _garde;
    private readonly FiltreActivite _activite;
    private readonly System.Windows.Forms.Timer _minuterie = new() { Interval = 5000 };
    private CarteModeGestion _carteGestion = null!;

    public MainForm(ServiceNavigation navigation, ServiceGestion gestion, GardeGestion garde)
    {
        _navigation = navigation;
        _gestion = gestion;
        _garde = garde;
        _activite = new FiltreActivite(gestion);

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Asprenaut · Matériel";
        Icon = Icones.IconeFenetre;
        BackColor = Theme.Fond;
        ForeColor = Theme.Texte;
        Font = Theme.Police();
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1360, 860);
        WindowState = FormWindowState.Maximized;
        KeyPreview = false;

        var contenu = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Fond, Padding = new Padding(40, 28, 40, 28) };
        var menu = CreerMenu();
        _menuPanneau = menu;
        _contenu = contenu;

        Controls.Add(contenu);
        Controls.Add(menu);

        _navigation.Attacher(contenu);
        _navigation.EcranChange += (_, ecran) => SelectionnerMenu(ecran.Menu);
        Theme.Changement += ChangerTheme;

        // Mode gestion : carte du menu à jour, retour automatique en consultation vérifié toutes les 5 s.
        _gestion.EtatChange += ActualiserGestion;
        _minuterie.Tick += (_, _) =>
        {
            if (!_gestion.Verifier())
            {
                _carteGestion.Actualiser(); // Minutes restantes.
            }
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Theme.Changement -= ChangerTheme;
            _gestion.EtatChange -= ActualiserGestion;
            _minuterie.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ActualiserGestion(object? sender, EventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ActualiserGestion(sender, e));
            return;
        }

        _carteGestion.Actualiser();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Application.RemoveMessageFilter(_activite);
        _minuterie.Stop();
        _gestion.PreparerFermeture(); // Reverrouille au prochain lancement, sauf si le club l'a désactivé.
        base.OnFormClosed(e);
    }

    private void SelectionnerMenu(EntreeMenu menu)
    {
        foreach (var (entree, bouton) in _menu)
        {
            bouton.Selectionne = entree == menu;
        }
    }

    /// <summary>
    /// Nouveau thème : fond, barre de titre et menu refaits tout de suite. L'écran courant est recréé par
    /// celui qui a changé le thème (Paramètres), qui sait où se replacer.
    /// </summary>
    private void ChangerTheme(object? sender, EventArgs e)
    {
        Natif.Geler(this);
        try
        {
            SuspendLayout();
            BackColor = _contenu.BackColor = Theme.Fond;
            ForeColor = Theme.Texte;
            var ancien = _menuPanneau;
            _menu.Clear();
            _menuPanneau = CreerMenu();
            Dessin.MettreALEchelle(_menuPanneau, this);
            Controls.Remove(ancien);
            Controls.Add(_menuPanneau);
            ancien.Dispose();
            if (_navigation.EcranCourant is { } ecran)
            {
                SelectionnerMenu(ecran.Menu);
            }

            ResumeLayout(true);
            Natif.BarreDeTitre(this);
        }
        finally
        {
            Natif.Degeler(this);
        }
    }

    // Pas de WS_EX_COMPOSITED : avec lui, chaque petite animation (point « Prêt à scanner », sonar) faisait
    // repeindre presque toute la fenêtre ; sur les écrans chargés (Paramètres), la fenêtre clignotait en continu.

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Natif.BarreDeTitre(this);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Taille minimale 1280 x 800 en pixels, bornée à l'écran pour les petits portables.
        var zone = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(1280, zone.Width), Math.Min(800, zone.Height));

        // Menu et marges construits en pixels logiques : mis à l'échelle de l'écran (125 %, 150 %...).
        Dessin.MettreALEchelle(_menuPanneau, this);
        var f = DeviceDpi / 96f;
        _contenu.Padding = new Padding((int)(40 * f), (int)(28 * f), (int)(40 * f), (int)(28 * f));
        _navigation.AllerA<EcranSorties>();
        Application.AddMessageFilter(_activite);
        _minuterie.Start();
    }

    private Panel CreerMenu()
    {
        var panneau = new PanneauMenu { Dock = DockStyle.Left, Width = Theme.LargeurMenu, BackColor = Theme.FondMenu, Padding = new Padding(18, 28, 18, 18) };

        // Marque : pastille ronde du logo, nom du club.
        var nom = Ui.Pile(
            Ui.Libelle("Asprenaut", Theme.PoliceGrasse(13.5f)),
            Ui.Libelle("Matériel du club", Theme.Police(9f), Theme.TexteSecondaire));
        var logo = new Logo { Margin = Padding.Empty };
        var marque = Ui.Rangee(true, logo, nom);
        marque.Margin = new Padding(6, 0, 0, 34);
        nom.Margin = new Padding(12, (46 - nom.PreferredSize.Height) / 2, 0, 0);

        var entrees = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Theme.FondMenu,
        };
        entrees.Controls.Add(marque);

        void Ajouter<TEcran>(EntreeMenu entree, string texte, string icone)
            where TEcran : EcranBase
        {
            var bouton = new BoutonArrondi(texte, StyleBouton.Menu, icone)
            {
                Width = Theme.LargeurMenu - 36,
                Margin = new Padding(0, 0, 0, 4),
            };
            bouton.Click += (_, _) => _navigation.AllerA<TEcran>();
            _menu[entree] = bouton;
            entrees.Controls.Add(bouton);
        }

        Ajouter<EcranSorties>(EntreeMenu.Sorties, "Sorties", Icones.Sorties);
        Ajouter<EcranInventaire>(EntreeMenu.Inventaire, "Inventaire", Icones.Inventaire);
        Ajouter<EcranMoniteurs>(EntreeMenu.Moniteurs, "Moniteurs", Icones.Moniteurs);
        Ajouter<EcranHistorique>(EntreeMenu.Historique, "Historique", Icones.Historique);
        Ajouter<EcranBilan>(EntreeMenu.Bilan, "Bilan de saison", Icones.Bilan);
        Ajouter<EcranRevisions>(EntreeMenu.Revisions, "Révisions", Icones.Revisions);
        Ajouter<EcranParametres>(EntreeMenu.Parametres, "Paramètres", Icones.Parametres);

        var scanner = new CarteScanner { Dock = DockStyle.Bottom };
        var version = Ui.Libelle($"Version {Application.ProductVersion.Split('+')[0]}", Theme.Police(8.25f), Theme.TexteSecondaire);
        version.Dock = DockStyle.Bottom;
        version.Padding = new Padding(6, 10, 0, 0);

        // Mode consultation / mode gestion, juste au-dessus de la carte du pistolet.
        _carteGestion = new CarteModeGestion(_gestion) { Dock = DockStyle.Bottom };
        _carteGestion.Click += (_, _) =>
        {
            if (_gestion.ModeGestion)
            {
                _gestion.Verrouiller();
            }
            else
            {
                _garde.Deverrouiller(this);
            }
        };

        // Ancrées en bas dans l'ordre inverse de leur ajout : version tout en bas, puis pistolet, puis mode gestion.
        panneau.Controls.Add(entrees);
        panneau.Controls.Add(_carteGestion);
        panneau.Controls.Add(scanner);
        panneau.Controls.Add(version);
        return panneau;
    }

    /// <summary>Menu latéral : fond légèrement plus sombre, filet turquoise à droite.</summary>
    private sealed class PanneauMenu : Panel
    {
        public PanneauMenu()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            using var filet = new Pen(Theme.Sur(BackColor, Theme.Accent, 0.10f));
            e.Graphics.DrawLine(filet, Width - 1, 0, Width - 1, Height);
        }
    }
}
