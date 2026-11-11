using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Fiche de suivi d'un équipement, ouverte depuis l'inventaire : où il est, sa prochaine échéance et tout son parcours
/// (sorties, contrôles, ateliers). Lecture seule : la modification reste sur la ligne de l'inventaire.
/// </summary>
public sealed class FicheSuivi : Form
{
    private const int Largeur = 760;
    private const int Hauteur = 820;

    private FicheSuivi(VieEquipement vie)
    {
        Text = $"Fiche de suivi — {vie.Equipement.Code}";
        BackColor = Theme.Surface;
        ForeColor = Theme.Texte;
        Font = Theme.Police();
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Icon = Icones.IconeFenetre;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.None;
        MinimumSize = new Size(560, 480);
        Padding = new Padding(28, 24, 28, 24);

        // Toute la fiche défile : sur un petit écran, le parcours reste accessible.
        var contenu = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };
        contenu.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var controle in FicheVie.Construire(vie, avecCode: true))
        {
            contenu.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            controle.Dock = DockStyle.Fill;
            contenu.Controls.Add(controle, 0, contenu.RowStyles.Count - 1);
        }

        var defile = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Surface, Margin = Padding.Empty };
        Natif.ScrollbarsDuTheme(defile);
        defile.Controls.Add(contenu);

        var fermer = new BoutonArrondi("Fermer", StyleBouton.Principal) { Margin = Padding.Empty, DialogResult = DialogResult.OK };
        CancelButton = fermer;
        AcceptButton = fermer;
        var pied = Ui.Rangee(false, fermer);
        pied.Margin = new Padding(0, 16, 0, 0);

        Controls.Add(Ui.Colonne(
            (defile, SizeType.Percent, 100),
            (pied, SizeType.AutoSize, 0)));
    }

    /// <summary>Ouvre la fiche (modale) ; prévient si l'équipement a été supprimé entre-temps.</summary>
    public static async Task OuvrirAsync(IWin32Window? proprietaire, ServiceHistorique historique, long equipementId)
    {
        var vie = await historique.ObtenirVieAsync(equipementId);
        if (vie is null)
        {
            Dialogues.Erreur(proprietaire, "Cet équipement n'existe plus.");
            return;
        }

        using var fenetre = new FicheSuivi(vie);
        fenetre.ShowDialog(proprietaire);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Natif.BarreDeTitre(this);
    }

    protected override void OnLoad(EventArgs e)
    {
        // Construite en pixels logiques : mise à l'échelle de l'écran (125 %, 150 %...).
        foreach (Control c in Controls)
        {
            Dessin.MettreALEchelle(c, this);
        }

        Padding = new Padding((int)Dessin.Echelle(this, 28), (int)Dessin.Echelle(this, 24), (int)Dessin.Echelle(this, 28), (int)Dessin.Echelle(this, 24));

        // Taille initiale : la fiche entière, mais jamais plus haute que l'écran.
        var ecran = Screen.FromControl(this).WorkingArea.Height;
        var hauteur = Math.Min((int)Dessin.Echelle(this, Hauteur), ecran - (int)Dessin.Echelle(this, 60));
        ClientSize = new Size((int)Dessin.Echelle(this, Largeur), Math.Max(hauteur, (int)Dessin.Echelle(this, 320)));
        base.OnLoad(e);
    }
}
