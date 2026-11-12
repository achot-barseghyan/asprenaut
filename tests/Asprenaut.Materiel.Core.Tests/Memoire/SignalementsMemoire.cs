using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.Core.Tests.Memoire;

/// <summary>Problèmes signalés en mémoire (même règle qu'en SQL : un seul ouvert par équipement).</summary>
public sealed class SignalementRepositoryMemoire(BaseMemoire db) : ISignalementRepository
{
    public List<Signalement> Signalements { get; } = [];

    public Task<long> AjouterAsync(Signalement signalement, CancellationToken ct = default)
    {
        if (signalement.Ouvert && Signalements.Any(s => s.Ouvert && s.EquipementId == signalement.EquipementId))
        {
            throw new InvalidOperationException("ux_signalement_ouvert : déjà un signalement ouvert.");
        }

        signalement.Id = db.ProchainId();
        Signalements.Add(signalement);
        return Task.FromResult(signalement.Id);
    }

    public Task MettreAJourAsync(Signalement signalement, CancellationToken ct = default) => Task.CompletedTask; // Même instance.

    public Task<Signalement?> ObtenirOuvertAsync(long equipementId, CancellationToken ct = default) =>
        Task.FromResult(Signalements.FirstOrDefault(s => s.Ouvert && s.EquipementId == equipementId));

    public Task<IReadOnlyList<Signalement>> ListerOuvertsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Signalement>>(Signalements.Where(s => s.Ouvert).OrderByDescending(s => s.SignaleLe).ThenByDescending(s => s.Id).ToList());

    public Task<IReadOnlyList<Signalement>> ListerAsync(long equipementId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Signalement>>(Signalements.Where(s => s.EquipementId == equipementId).OrderByDescending(s => s.SignaleLe).ThenByDescending(s => s.Id).ToList());

    public Task<int> TraiterAsync(long equipementId, DateTime traiteLe, CancellationToken ct = default)
    {
        var ouverts = Signalements.Where(s => s.Ouvert && s.EquipementId == equipementId).ToList();
        ouverts.ForEach(s => s.TraiteLe = traiteLe);
        return Task.FromResult(ouverts.Count);
    }
}
