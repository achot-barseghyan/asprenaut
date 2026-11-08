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

    public MainForm(ServiceNavigation navigation)
    {
        _navigation = navigation;

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Asprenaut · Matériel";
        BackColor = Theme.Fond;
        ForeColor = Theme.Texte;
        Font = Theme.Police();
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1360, 860);
        WindowState = FormWindowState.Maximized;
        KeyPreview = false;

        var contenu = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Fond, Padding = new Padding(36, 28, 36, 24) };
        var menu = CreerMenu();

        Controls.Add(contenu);
        Controls.Add(menu);

        _navigation.Attacher(contenu);
        _navigation.EcranChange += (_, ecran) =>
        {
            foreach (var (entree, bouton) in _menu)
            {
                bouton.Selectionne = entree == ecran.Menu;
            }
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Natif.BarreDeTitreSombre(this);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Taille minimale 1280 x 800 en pixels, bornée à l'écran pour les petits portables.
        var zone = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(1280, zone.Width), Math.Min(800, zone.Height));
        _navigation.AllerA<EcranSorties>();
    }

    private Panel CreerMenu()
    {
        var panneau = new Panel { Dock = DockStyle.Left, Width = Theme.LargeurMenu, BackColor = Theme.Surface, Padding = new Padding(16, 24, 16, 20) };

        var marque = Ui.Pile(
            Ui.Libelle("Asprenaut", Theme.PoliceTitre(17f), Theme.Accent),
            Ui.Libelle("Suivi du matériel", Theme.Police(10f), Theme.TexteSecondaire));
        marque.Margin = new Padding(8, 0, 0, 28);

        var entrees = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Theme.Surface,
        };
        entrees.Controls.Add(marque);

        void Ajouter<TEcran>(EntreeMenu entree, string texte)
            where TEcran : EcranBase
        {
            var bouton = new BoutonArrondi(texte, StyleBouton.Menu)
            {
                Width = Theme.LargeurMenu - 32,
                Height = Theme.HauteurBouton,
                Margin = new Padding(0, 0, 0, 6),
            };
            bouton.Click += (_, _) => _navigation.AllerA<TEcran>();
            _menu[entree] = bouton;
            entrees.Controls.Add(bouton);
        }

        Ajouter<EcranSorties>(EntreeMenu.Sorties, "Sorties");
        Ajouter<EcranInventaire>(EntreeMenu.Inventaire, "Inventaire");
        Ajouter<EcranMoniteurs>(EntreeMenu.Moniteurs, "Moniteurs et plongeurs");
        Ajouter<EcranHistorique>(EntreeMenu.Historique, "Historique");
        Ajouter<EcranParametres>(EntreeMenu.Parametres, "Paramètres");

        var version = Ui.Libelle($"Version {Application.ProductVersion.Split('+')[0]}", Theme.Police(9f), Theme.TexteSecondaire);
        version.Dock = DockStyle.Bottom;
        version.Padding = new Padding(8, 0, 0, 0);

        panneau.Controls.Add(entrees);
        panneau.Controls.Add(version);
        return panneau;
    }
}
