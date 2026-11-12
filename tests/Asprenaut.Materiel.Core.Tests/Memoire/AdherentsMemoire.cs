using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.Core.Tests.Memoire;

/// <summary>Adhérents en mémoire ; comme l'index ux_adherent_licence, refuse deux fois la même licence.</summary>
public sealed class AdherentRepositoryMemoire(BaseMemoire db) : IAdherentRepository
{
    public Task<IReadOnlyList<Adherent>> ListerAsync(bool actifsSeulement, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Adherent>>(db.Adherents.Where(a => !actifsSeulement || a.Actif).OrderBy(a => a.Nom).ThenBy(a => a.Prenom).ToList());

    public Task<Adherent?> ObtenirAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(db.Adherents.FirstOrDefault(a => a.Id == id));

    public Task<long> AjouterAsync(Adherent adherent, CancellationToken ct = default)
    {
        VerifierLicence(adherent);
        adherent.Id = db.ProchainId();
        db.Adherents.Add(adherent);
        return Task.FromResult(adherent.Id);
    }

    public Task MettreAJourAsync(Adherent adherent, CancellationToken ct = default)
    {
        VerifierLicence(adherent);
        var i = db.Adherents.FindIndex(a => a.Id == adherent.Id);
        db.Adherents[i] = adherent;
        return Task.CompletedTask;
    }

    private void VerifierLicence(Adherent adherent)
    {
        if (adherent.Licence is { Length: > 0 } l && db.Adherents.Any(a => a.Id != adherent.Id && a.Licence == l))
        {
            throw new InvalidOperationException($"Licence {l} déjà prise (index unique).");
        }
    }

    public Task<IReadOnlyList<SituationAdherent>> ListerSituationsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SituationAdherent>>(db.Affectations
            .Where(a => a.AdherentId is not null)
            .GroupBy(a => a.AdherentId!.Value)
            .Select(g => new SituationAdherent
            {
                AdherentId = g.Key,
                NbDehors = g.Count(a => a.Statut == StatutSortieEquipement.Dehors),
                NbManquants = g.Count(a => a.Statut == StatutSortieEquipement.Manquant),
            })
            .Where(s => s.NbDehors + s.NbManquants > 0)
            .ToList());

    public Task<IReadOnlyList<PassageAdherent>> ListerPassagesAsync(long adherentId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PassageAdherent>>(db.Affectations
            .Where(a => a.AdherentId == adherentId)
            .Select(a =>
            {
                var s = db.Sorties.First(x => x.Id == a.SortieId);
                var e = db.Equipements.First(x => x.Id == a.EquipementId);
                return new PassageAdherent
                {
                    SortieId = s.Id,
                    Intitule = s.Intitule,
                    DateSortie = s.DateSortie,
                    TypeNom = db.TypesSortie.FirstOrDefault(t => t.Id == s.TypeId)?.Nom,
                    TypeSortie = s.Type,
                    StatutSortie = s.Statut,
                    EquipementId = e.Id,
                    Code = e.Code,
                    Libelle = e.Libelle,
                    Categorie = db.Categories.FirstOrDefault(c => c.Id == e.CategorieId)?.Nom ?? "",
                    Statut = a.Statut,
                    ScanneDepartLe = a.ScanneDepartLe,
                    ScanneRetourLe = a.ScanneRetourLe,
                };
            })
            .OrderByDescending(p => p.DateSortie).ThenByDescending(p => p.SortieId)
            .ToList());
}
