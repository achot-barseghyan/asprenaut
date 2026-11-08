using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>Moniteurs : lecture pour les listes de choix. La gestion complète viendra plus tard.</summary>
public sealed class ServiceMoniteurs(IMoniteurRepository moniteurs)
{
    public Task<IReadOnlyList<Moniteur>> ListerActifsAsync(CancellationToken ct = default) =>
        moniteurs.ListerAsync(actifsSeulement: true, ct);

    public Task<IReadOnlyList<Moniteur>> ListerTousAsync(CancellationToken ct = default) =>
        moniteurs.ListerAsync(actifsSeulement: false, ct);
}

/// <summary>Historique : lecture du journal.</summary>
public sealed class ServiceHistorique(IJournalRepository journal)
{
    public Task<IReadOnlyList<EntreeJournal>> ListerRecentsAsync(int nombre = 500, CancellationToken ct = default) =>
        journal.ListerRecentsAsync(nombre, ct);
}
