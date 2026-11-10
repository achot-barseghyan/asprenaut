using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.Core.Tests.Memoire;

/// <summary>Contrôles, passages en atelier et séances TIV en mémoire.</summary>
public sealed class RevisionRepositoryMemoire(BaseMemoire db) : IRevisionRepository
{
    /// <summary>Dans la base partagée : le compteur d'utilisation (dépôt des équipements) les lit aussi.</summary>
    public List<Controle> Controles => db.Controles;
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

    public Task<IReadOnlyList<Controle>> ListerControlesPeriodeAsync(DateOnly du, DateOnly au, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Controle>>(Controles.Where(c => c.DateControle >= du && c.DateControle <= au).OrderBy(c => c.DateControle).ToList());

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

    public Task<IReadOnlyList<PassageAtelier>> ListerPassagesAtelierAsync(long equipementId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PassageAtelier>>(Passages.Where(p => p.EquipementId == equipementId).OrderByDescending(p => p.EnvoyeLe).ToList());

    public Task<long> AjouterSeanceAsync(SeanceTiv seance, CancellationToken ct = default)
    {
        seance.Id = db.ProchainId();
        Seances.Add(seance);
        return Task.FromResult(seance.Id);
    }

    public HashSet<(long Seance, long Equipement)> Planifies { get; } = [];

    public Task<IReadOnlyList<long>> ListerPlanifiesAsync(long seanceId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<long>>(Planifies.Where(p => p.Seance == seanceId).Select(p => p.Equipement).Order().ToList());

    public Task AjouterASeanceAsync(long seanceId, long equipementId, CancellationToken ct = default)
    {
        Planifies.Add((seanceId, equipementId));
        return Task.CompletedTask;
    }

    public Task RetirerDesSeancesAsync(long equipementId, CancellationToken ct = default)
    {
        Planifies.RemoveWhere(p => p.Equipement == equipementId);
        return Task.CompletedTask;
    }

    public Task<SeanceTiv?> ProchaineSeanceAsync(DateOnly aPartirDu, CancellationToken ct = default) =>
        Task.FromResult(Seances.Where(s => s.DateSeance >= aPartirDu).OrderBy(s => s.DateSeance).FirstOrDefault());
}
