using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.Core.Tests.Memoire;

/// <summary>Modèles de sortie en mémoire.</summary>
public sealed class ModeleSortieRepositoryMemoire(BaseMemoire db) : IModeleSortieRepository
{
    public List<ModeleSortie> Modeles { get; } = [];

    public Task<IReadOnlyList<ModeleSortie>> ListerAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ModeleSortie>>(Modeles.OrderBy(m => m.Nom, StringComparer.OrdinalIgnoreCase).ToList());

    public Task<ModeleSortie?> ObtenirAsync(long id, CancellationToken ct = default) => Task.FromResult(Modeles.FirstOrDefault(m => m.Id == id));

    public Task<long> AjouterAsync(ModeleSortie modele, CancellationToken ct = default)
    {
        modele.Id = db.ProchainId();
        Modeles.Add(modele);
        return Task.FromResult(modele.Id);
    }

    public Task MettreAJourAsync(ModeleSortie modele, CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> SupprimerAsync(long id, CancellationToken ct = default) => Task.FromResult(Modeles.RemoveAll(m => m.Id == id) == 1);
}
