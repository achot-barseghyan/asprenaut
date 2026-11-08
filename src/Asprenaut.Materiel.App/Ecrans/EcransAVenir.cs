using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Navigation;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>Écran vide avec titre, en attendant son contenu.</summary>
public abstract class EcranAVenir : EcranBase
{
    protected EcranAVenir(string sousTitre)
    {
        Controls.Add(Ui.Colonne(
            (Ui.Entete(Ui.Pile(Ui.Titre(Titre), Ui.SousTitre(sousTitre))), SizeType.AutoSize, 0),
            (Ui.Libelle("Cet écran sera disponible dans une prochaine version.", Theme.Police(11f), Theme.TexteSecondaire), SizeType.Percent, 100)));
    }
}

public sealed class EcranHistorique() : EcranAVenir("Journal de tous les scans et changements.")
{
    public override string Titre => "Historique";

    public override EntreeMenu Menu => EntreeMenu.Historique;
}