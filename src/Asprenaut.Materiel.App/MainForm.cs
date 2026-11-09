using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Ecrans;
using Asprenaut.Materiel.App.Navigation;

namespace Asprenaut.Materiel.App;

/// <summary>Fenêtre unique : menu latéral fixe à gauche, écran courant à droite.</summary>
public sealed class MainForm : Form
{
    private readonly ServiceNavigation _navigation;
    private readonly Dictionary<EntreeMenu, BoutonArrondi> _menu = [];
    private Panel _menuPanneau;
    private readonly Panel _contenu;

    public MainForm(ServiceNavigation navigation)
    {
        _navigation = navigation;

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
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Theme.Changement -= ChangerTheme;
        }

        base.Dispose(disposing);
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
        Ajouter<EcranRevisions>(EntreeMenu.Revisions, "Révisions", Icones.Revisions);
        Ajouter<EcranParametres>(EntreeMenu.Parametres, "Paramètres", Icones.Parametres);

        var scanner = new CarteScanner { Dock = DockStyle.Bottom };
        var version = Ui.Libelle($"Version {Application.ProductVersion.Split('+')[0]}", Theme.Police(8.25f), Theme.TexteSecondaire);
        version.Dock = DockStyle.Bottom;
        version.Padding = new Padding(6, 10, 0, 0);

        panneau.Controls.Add(entrees);
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
