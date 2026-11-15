using System.ComponentModel;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Sortie nominative : « Un équipement par catégorie et par adhérent » (Nouvelle sortie, Modifier la sortie).
/// Activé par défaut ; le couper demande le code gestionnaire (cadenas sur l'interrupteur en consultation), le remettre est libre.
/// Le service revérifie (<see cref="ActionProtegee.PlusieursParCategorie"/>).
/// Maquette « Modifier la sortie » : bouclier vert à gauche, carte teintée de vert quand la règle est active ;
/// la hauteur suit la description, mise à la ligne selon la largeur de la carte.
/// </summary>
public sealed class OptionUnParCategorie : Carte
{
    public const int Hauteur = 84;
    private readonly Interrupteur _interrupteur = new("Un équipement par catégorie et par adhérent") { Checked = true, AutoCheck = false };
    private readonly Action _majVerrou;
    private readonly Label _titre = Ui.Libelle("Un équipement par catégorie et par adhérent", Theme.PoliceTitre(10.5f));
    private readonly Label _aide = Ui.Libelle("", Theme.Police(9f), Theme.TexteSecondaire);
    private readonly TuileIcone _tuile = new(Icones.Bouclier, null, Theme.Retour) { Anchor = AnchorStyles.Left };
    private readonly FlowLayoutPanel _textes;
    private readonly TableLayoutPanel _rangee;

    public OptionUnParCategorie(GardeGestion garde)
    {
        var titre = _titre;
        titre.Margin = new Padding(0, 0, 0, 2);
        _aide.Margin = Padding.Empty;
        var textes = _textes = Ui.Pile(titre, _aide);
        textes.Anchor = AnchorStyles.Left;
        textes.Margin = new Padding(14, 0, 0, 0);
        var tuile = _tuile;
        _interrupteur.Anchor = AnchorStyles.Right;
        _interrupteur.Margin = new Padding(12, 0, 0, 0);
        _interrupteur.CouleurActive = Theme.Retour;

        // Cadenas seulement tant que l'option est active : c'est la couper qui demande le code.
        _majVerrou = garde.Lier(_interrupteur, ActionProtegee.PlusieursParCategorie, () => _interrupteur.Checked);
        _interrupteur.Click += (_, _) => Basculer(garde);
        foreach (var c in new Control[] { this, textes, titre, _aide, tuile })
        {
            c.Click += (_, _) => Basculer(garde); // Un clic sur la carte bascule l'interrupteur (maquette).
            c.Cursor = Cursors.Hand;
        }

        _rangee = Ui.Colonnes((tuile, SizeType.AutoSize, 0), (textes, SizeType.Percent, 100), (_interrupteur, SizeType.AutoSize, 0));
        tuile.Dock = textes.Dock = _interrupteur.Dock = DockStyle.None; // Colonnes met Dock Fill : centrés verticalement.
        tuile.Anchor = textes.Anchor = AnchorStyles.Left;
        _interrupteur.Anchor = AnchorStyles.Right;
        _rangee.AutoSize = true;
        _rangee.Dock = DockStyle.Fill;
        _rangee.Click += (_, _) => Basculer(garde);
        Controls.Add(_rangee);
        Padding = new Padding(16, 12, 16, 12);
        Rayon = 16;
        Height = Hauteur;
        AfficherAide();
        // Hauteur d'après le texte mis à la ligne (GetPreferredSize), comme les sections du formulaire.
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    /// <summary>Largeur laissée aux textes : la carte moins la tuile, l'interrupteur et les marges.</summary>
    private int LargeurTextes(int largeur) =>
        Math.Max(80, largeur - Padding.Horizontal - _tuile.Width - _textes.Margin.Horizontal - _interrupteur.Width - _interrupteur.Margin.Horizontal - 2);

    public override Size GetPreferredSize(Size proposedSize)
    {
        var largeur = MesureFormulaire.Largeur(this, proposedSize);
        var texte = LargeurTextes(largeur);
        if (_titre.MaximumSize.Width != texte)
        {
            _titre.MaximumSize = _aide.MaximumSize = new Size(texte, 0);
        }

        var hauteur = _rangee.GetPreferredSize(new Size(Math.Max(1, largeur - Padding.Horizontal), 0)).Height + Padding.Vertical;
        return new Size(largeur, Math.Max((int)Dessin.Echelle(this, Hauteur), hauteur));
    }

    /// <summary>Un adhérent ne prend qu'un détendeur, un bloc, un gilet… (sinon : plusieurs).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Actif
    {
        get => _interrupteur.Checked;
        set
        {
            _interrupteur.Checked = value;
            _majVerrou();
            AfficherAide();
            ActifChange?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>L'option a changé (clic ou code).</summary>
    public event EventHandler? ActifChange;

    private void Basculer(GardeGestion garde)
    {
        if (_interrupteur.Checked && !garde.Exiger(FindForm(), ActionProtegee.PlusieursParCategorie, "Autoriser plusieurs équipements d'une catégorie"))
        {
            return;
        }

        Actif = !_interrupteur.Checked;
    }

    private void AfficherAide()
    {
        _aide.Text = _interrupteur.Checked
            ? "Pas deux détendeurs, deux blocs ou deux gilets pour le même adhérent. Se coupe en mode gestion."
            : "Désactivé : un adhérent peut prendre plusieurs équipements d'une même catégorie.";
        BackColor = _interrupteur.Checked ? Theme.Sur(ChampSaisie.FondChamp, Theme.Retour, 0.06f) : ChampSaisie.FondChamp;
        CouleurBordure = _interrupteur.Checked ? Theme.Sur(ChampSaisie.FondChamp, Theme.Retour, 0.5f) : Theme.BordureForte;
        Invalidate(true);
    }
}
