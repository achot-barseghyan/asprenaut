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
            .Where(c => c.Composant.Length == 0)
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

    // --- Étapes (table revision) ---

    public List<SuiviRevision> Suivis { get; } = [];

    /// <summary>Prestataires connus, pour le nom lu avec la révision (comme la jointure SQL).</summary>
    public List<Prestataire> Prestataires { get; } = [];

    private SuiviRevision Lu(SuiviRevision s)
    {
        s.Prestataire = Prestataires.FirstOrDefault(p => p.Id == s.PrestataireId)?.Nom
            ?? Passages.FirstOrDefault(p => p.Id == s.AtelierId)?.Prestataire ?? "";
        return s;
    }

    public Task<IReadOnlyList<SuiviRevision>> ListerSuivisAsync(DateOnly faitDepuis, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SuiviRevision>>(Suivis
            .Where(s => s.Etape != EtapeRevision.Fait || (!s.Archive && s.DateFait >= faitDepuis))
            .Select(Lu).ToList());

    public Task<SuiviRevision?> ObtenirSuiviEnCoursAsync(long equipementId, CancellationToken ct = default) =>
        Task.FromResult(Suivis.Where(s => s.EquipementId == equipementId && s.Etape != EtapeRevision.Fait).Select(Lu).SingleOrDefault());

    public Task<SuiviRevision?> ObtenirSuiviAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(Suivis.Where(s => s.Id == id).Select(Lu).SingleOrDefault());

    public Task<long> AjouterSuiviAsync(SuiviRevision suivi, CancellationToken ct = default)
    {
        if (suivi.Etape != EtapeRevision.Fait && Suivis.Any(s => s.EquipementId == suivi.EquipementId && s.Etape != EtapeRevision.Fait))
        {
            throw new InvalidOperationException("Une seule révision en cours par équipement (ux_revision_en_cours).");
        }

        suivi.Id = db.ProchainId();
        Suivis.Add(suivi);
        return Task.FromResult(suivi.Id);
    }

    public Task MettreAJourSuiviAsync(SuiviRevision suivi, CancellationToken ct = default) => Task.CompletedTask; // Même objet.

    public Task SupprimerSuiviAsync(long id, CancellationToken ct = default)
    {
        Suivis.RemoveAll(s => s.Id == id && s.Etape != EtapeRevision.Fait);
        return Task.CompletedTask;
    }

    public Task<int> ArchiverSuivisAsync(IReadOnlyList<long> ids, bool archive, DateOnly? le, CancellationToken ct = default)
    {
        var n = 0;
        foreach (var s in Suivis.Where(s => ids.Contains(s.Id) && s.Etape == EtapeRevision.Fait))
        {
            s.Archive = archive;
            s.DateArchive = archive ? le : null;
            n++;
        }

        return Task.FromResult(n);
    }
}

/// <summary>Prestataires en mémoire (partagés avec le dépôt des révisions pour le nom lu).</summary>
public sealed class PrestataireRepositoryMemoire(BaseMemoire db, List<Prestataire> prestataires) : IPrestataireRepository
{
    public Task<IReadOnlyList<Prestataire>> ListerAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Prestataire>>(prestataires.OrderByDescending(p => p.Actif).ThenBy(p => p.Nom).ToList());

    public Task<Prestataire?> ObtenirAsync(long id, CancellationToken ct = default) => Task.FromResult(prestataires.FirstOrDefault(p => p.Id == id));

    public Task<long> AjouterAsync(Prestataire prestataire, CancellationToken ct = default)
    {
        prestataire.Id = db.ProchainId();
        prestataires.Add(prestataire);
        return Task.FromResult(prestataire.Id);
    }

    public Task MettreAJourAsync(Prestataire prestataire, CancellationToken ct = default) => Task.CompletedTask;
}
