using System.ComponentModel;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Sortie nominative : « Un équipement par catégorie et par adhérent » (Nouvelle sortie, Modifier la sortie).
/// Activé par défaut ; le couper demande le code gestionnaire (cadenas sur l'interrupteur en consultation), le remettre est libre.
/// Le service revérifie (<see cref="ActionProtegee.PlusieursParCategorie"/>).
/// </summary>
public sealed class OptionUnParCategorie : Carte
{
    public const int Hauteur = 84;
    private readonly Interrupteur _interrupteur = new("Un équipement par catégorie et par adhérent") { Checked = true, AutoCheck = false };
    private readonly Action _majVerrou;
    private readonly Label _aide = Ui.Libelle("", Theme.Police(9f), Theme.TexteSecondaire);

    public OptionUnParCategorie(GardeGestion garde)
    {
        var titre = Ui.Libelle("Un équipement par catégorie et par adhérent", Theme.PoliceTitre(10.5f));
        titre.Margin = new Padding(0, 0, 0, 2);
        _aide.Margin = Padding.Empty;
        _aide.MaximumSize = new Size(440, 0);
        var textes = Ui.Pile(titre, _aide);
        textes.Anchor = AnchorStyles.Left;
        _interrupteur.Anchor = AnchorStyles.Right;
        _interrupteur.Margin = new Padding(12, 0, 0, 0);

        // Cadenas seulement tant que l'option est active : c'est la couper qui demande le code.
        _majVerrou = garde.Lier(_interrupteur, ActionProtegee.PlusieursParCategorie, () => _interrupteur.Checked);
        _interrupteur.Click += (_, _) =>
        {
            if (_interrupteur.Checked && !garde.Exiger(FindForm(), ActionProtegee.PlusieursParCategorie, "Autoriser plusieurs équipements d'une catégorie"))
            {
                return;
            }

            _interrupteur.Checked = !_interrupteur.Checked;
            _majVerrou();
            AfficherAide();
        };

        var rangee = Ui.Colonnes((textes, SizeType.Percent, 100), (_interrupteur, SizeType.AutoSize, 0));
        rangee.AutoSize = true;
        rangee.Dock = DockStyle.Fill;
        Controls.Add(rangee);
        Padding = new Padding(16, 12, 16, 12);
        Rayon = 16;
        BackColor = Theme.Teinte(Theme.Accent, 0.06f);
        CouleurBordure = Theme.Teinte(Theme.Accent, 0.24f);
        Height = Hauteur;
        AfficherAide();
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
        }
    }

    private void AfficherAide() => _aide.Text = _interrupteur.Checked
        ? "Pas deux détendeurs, deux blocs ou deux gilets pour le même adhérent. Se coupe en mode gestion."
        : "Désactivé : un adhérent peut prendre plusieurs équipements d'une même catégorie.";
}
