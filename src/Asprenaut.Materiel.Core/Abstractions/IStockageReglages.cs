using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.Core.Abstractions;

/// <summary>Enregistrement des réglages (fichier parametres.json dans l'application).</summary>
public interface IStockageReglages
{
    void Enregistrer(Reglages reglages);
}
