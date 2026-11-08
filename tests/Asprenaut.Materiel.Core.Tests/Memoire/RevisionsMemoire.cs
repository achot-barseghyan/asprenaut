using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.Core.Tests.Memoire;

/// <summary>Contrôles, passages en atelier et séances TIV en mémoire.</summary>
public sealed class RevisionRepositoryMemoire(BaseMemoire db) : IRevisionRepository
{
    public List<Controle> Controles { get; } = [];
    public List<PassageAtelier> Passages { get; } = [];
    public List<SeanceTiv> Seances { get; } = [];

    public Task<long> AjouterControleAsync(Controle controle, CancellationToken ct = default)
    {
        controle.Id = db.ProchainId();
        Controles.Add(controle);
        return Task.FromResult(controle.Id);
    }

    public Task<IReadOnlyList<DernierControle>> ListerDerniersControlesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DernierControle>>(Controles
            .GroupBy(c => (c.EquipementId, c.Type))
            .Select(g => new DernierControle { EquipementId = g.Key.EquipementId, Type = g.Key.Type, DateControle = g.Max(c => c.DateControle) })
            .ToList());

    public Task<IReadOnlyList<Controle>> ListerControlesAsync(long equipementId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Controle>>(Controles.Where(c => c.EquipementId == equipementId).OrderByDescending(c => c.DateControle).ToList());

    public Task<long> AjouterPassageAtelierAsync(PassageAtelier passage, CancellationToken ct = default)
    {
        passage.Id = db.ProchainId();
        Passages.Add(passage);
        return Task.FromResult(passage.Id);
    }

    public Task<PassageAtelier?> ObtenirPassageEnCoursAsync(long equipementId, CancellationToken ct = default) =>
        Task.FromResult(Passages.FirstOrDefault(p => p.EquipementId == equipementId && p.RevenuLe is null));

    public Task<PassageAtelier?> ObtenirPassageAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(Passages.FirstOrDefault(p => p.Id == id));

    public Task MettreAJourPassageAsync(PassageAtelier passage, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<LigneAtelier>> ListerAtelierEnCoursAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LigneAtelier>>(Passages
            .Where(p => p.RevenuLe is null)
            .Select(p =>
            {
                var e = db.Equipements.First(x => x.Id == p.EquipementId);
                return new LigneAtelier(p.Id, e.Id, e.Code, e.Libelle, p.Prestataire, p.Motif, p.EnvoyeLe, p.RetourPrevu);
            })
            .ToList());

    public Task<long> AjouterSeanceAsync(SeanceTiv seance, CancellationToken ct = default)
    {
        seance.Id = db.ProchainId();
        Seances.Add(seance);
        return Task.FromResult(seance.Id);
    }

    public Task<SeanceTiv?> ProchaineSeanceAsync(DateOnly aPartirDu, CancellationToken ct = default) =>
        Task.FromResult(Seances.Where(s => s.DateSeance >= aPartirDu).OrderBy(s => s.DateSeance).FirstOrDefault());
}
