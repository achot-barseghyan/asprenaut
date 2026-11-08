using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.Core.Tests.Memoire;

/// <summary>Base en mémoire partagée par les dépôts de test.</summary>
public sealed class BaseMemoire
{
    public List<Categorie> Categories { get; } = [];
    public List<Equipement> Equipements { get; } = [];
    public List<Moniteur> Moniteurs { get; } = [];
    public List<Sortie> Sorties { get; } = [];
    public List<SortieEquipement> Affectations { get; } = [];
    public List<EntreeJournal> Journal { get; } = [];

    private long _sequence;

    public long ProchainId() => ++_sequence;
}

public sealed class HorlogeFixe(DateTime maintenantUtc) : IHorloge
{
    public DateTime MaintenantUtc { get; set; } = maintenantUtc;

    public DateOnly AujourdHui => DateOnly.FromDateTime(MaintenantUtc);
}

public sealed class CategorieRepositoryMemoire(BaseMemoire db) : ICategorieRepository
{
    public Task<IReadOnlyList<Categorie>> ListerAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Categorie>>(db.Categories.OrderBy(c => c.Nom).ToList());
}

public sealed class EquipementRepositoryMemoire(BaseMemoire db) : IEquipementRepository
{
    public Task<Equipement?> ObtenirAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(db.Equipements.FirstOrDefault(e => e.Id == id));

    public Task<Equipement?> ObtenirParCodeAsync(string code, CancellationToken ct = default) =>
        Task.FromResult(db.Equipements.FirstOrDefault(e => string.Equals(e.Code, code, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<LigneInventaire>> ListerInventaireAsync(string? recherche, long? categorieId, CancellationToken ct = default)
    {
        var lignes = db.Equipements
            .Where(e => categorieId is null || e.CategorieId == categorieId)
            .Where(e => recherche is null
                || e.Code.Contains(recherche, StringComparison.OrdinalIgnoreCase)
                || e.Libelle.Contains(recherche, StringComparison.OrdinalIgnoreCase))
            .Select(e =>
            {
                var dehors = db.Affectations.FirstOrDefault(a => a.EquipementId == e.Id && a.Statut == StatutSortieEquipement.Dehors);
                return new LigneInventaire
                {
                    EquipementId = e.Id,
                    Code = e.Code,
                    Libelle = e.Libelle,
                    CategorieId = e.CategorieId,
                    Categorie = db.Categories.FirstOrDefault(c => c.Id == e.CategorieId)?.Nom ?? "",
                    Taille = e.Taille,
                    Etat = e.Etat,
                    DateProchaineRevision = e.DateProchaineRevision,
                    DateRequalification = e.DateRequalification,
                    SortieEnCoursId = dehors?.SortieId,
                    SortieEnCoursIntitule = dehors is null ? null : db.Sorties.First(s => s.Id == dehors.SortieId).Intitule,
                    EstManquant = db.Affectations.Any(a => a.EquipementId == e.Id && a.Statut == StatutSortieEquipement.Manquant),
                };
            })
            .OrderBy(l => l.Code)
            .ToList();
        return Task.FromResult<IReadOnlyList<LigneInventaire>>(lignes);
    }

    public Task<long> AjouterAsync(Equipement equipement, CancellationToken ct = default)
    {
        equipement.Id = db.ProchainId();
        db.Equipements.Add(equipement);
        return Task.FromResult(equipement.Id);
    }

    public Task MettreAJourAsync(Equipement equipement, CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class MoniteurRepositoryMemoire(BaseMemoire db) : IMoniteurRepository
{
    public Task<Moniteur?> ObtenirAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(db.Moniteurs.FirstOrDefault(m => m.Id == id));

    public Task<IReadOnlyList<Moniteur>> ListerAsync(bool actifsSeulement, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Moniteur>>(db.Moniteurs.Where(m => !actifsSeulement || m.Actif).ToList());

    public Task<long> AjouterAsync(Moniteur moniteur, CancellationToken ct = default)
    {
        moniteur.Id = db.ProchainId();
        db.Moniteurs.Add(moniteur);
        return Task.FromResult(moniteur.Id);
    }

    public Task MettreAJourAsync(Moniteur moniteur, CancellationToken ct = default)
    {
        var i = db.Moniteurs.FindIndex(m => m.Id == moniteur.Id);
        if (i >= 0)
        {
            db.Moniteurs[i] = moniteur;
        }

        return Task.CompletedTask;
    }
}

public sealed class SortieRepositoryMemoire(BaseMemoire db) : ISortieRepository
{
    public Task<Sortie?> ObtenirAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(db.Sorties.FirstOrDefault(s => s.Id == id));

    public Task<long> AjouterAsync(Sortie sortie, CancellationToken ct = default)
    {
        sortie.Id = db.ProchainId();
        db.Sorties.Add(sortie);
        return Task.FromResult(sortie.Id);
    }

    public Task MettreAJourAsync(Sortie sortie, CancellationToken ct = default) => Task.CompletedTask;

    public Task<SortieResume?> ObtenirResumeAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(db.Sorties.Where(s => s.Id == id).Select(Resumer).FirstOrDefault());

    public Task<IReadOnlyList<SortieResume>> ListerResumesAsync(bool cloturees, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SortieResume>>(db.Sorties
            .Where(s => (s.Statut == StatutSortie.Cloturee) == cloturees)
            .Select(Resumer)
            .ToList());

    public Task<IReadOnlyList<LigneSortie>> ListerLignesAsync(long sortieId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LigneSortie>>(db.Affectations
            .Where(a => a.SortieId == sortieId)
            .Select(a =>
            {
                var e = db.Equipements.First(x => x.Id == a.EquipementId);
                return new LigneSortie
                {
                    LigneId = a.Id,
                    SortieId = a.SortieId,
                    EquipementId = e.Id,
                    Code = e.Code,
                    Libelle = e.Libelle,
                    Taille = e.Taille,
                    DateProchaineRevision = e.DateProchaineRevision,
                    DateRequalification = e.DateRequalification,
                    Statut = a.Statut,
                    ScanneDepartLe = a.ScanneDepartLe,
                    ScanneRetourLe = a.ScanneRetourLe,
                };
            })
            .ToList());

    public Task<IReadOnlyList<SortieEquipement>> ListerAffectationsAsync(long sortieId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SortieEquipement>>(db.Affectations.Where(a => a.SortieId == sortieId).ToList());

    public Task<SortieEquipement?> ObtenirAffectationAsync(long sortieId, long equipementId, CancellationToken ct = default) =>
        Task.FromResult(db.Affectations.FirstOrDefault(a => a.SortieId == sortieId && a.EquipementId == equipementId));

    public Task<SortieEquipement?> ObtenirAffectationDehorsAsync(long equipementId, CancellationToken ct = default) =>
        Task.FromResult(db.Affectations.FirstOrDefault(a => a.EquipementId == equipementId && a.Statut == StatutSortieEquipement.Dehors));

    public Task<SortieEquipement?> ObtenirDerniereAffectationManquanteAsync(long equipementId, CancellationToken ct = default) =>
        Task.FromResult(db.Affectations
            .Where(a => a.EquipementId == equipementId && a.Statut == StatutSortieEquipement.Manquant)
            .OrderByDescending(a => a.ScanneDepartLe)
            .FirstOrDefault());

    public Task<long> AjouterAffectationAsync(SortieEquipement affectation, CancellationToken ct = default)
    {
        if (db.Affectations.Any(a => a.SortieId == affectation.SortieId && a.EquipementId == affectation.EquipementId))
        {
            throw new InvalidOperationException("Doublon (sortie, équipement) : la contrainte unique serait violée.");
        }

        affectation.Id = db.ProchainId();
        db.Affectations.Add(affectation);
        return Task.FromResult(affectation.Id);
    }

    public Task MettreAJourAffectationAsync(SortieEquipement affectation, CancellationToken ct = default) => Task.CompletedTask;

    public Task SupprimerAffectationAsync(long affectationId, CancellationToken ct = default)
    {
        db.Affectations.RemoveAll(a => a.Id == affectationId);
        return Task.CompletedTask;
    }

    public Task EnregistrerClotureAsync(Sortie sortie, IReadOnlyList<SortieEquipement> affectationsModifiees, CancellationToken ct = default) =>
        Task.CompletedTask;

    private SortieResume Resumer(Sortie s)
    {
        var lignes = db.Affectations.Where(a => a.SortieId == s.Id).ToList();
        return new SortieResume
        {
            Id = s.Id,
            DateSortie = s.DateSortie,
            Type = s.Type,
            Intitule = s.Intitule,
            Lieu = s.Lieu,
            Statut = s.Statut,
            DepartValideLe = s.DepartValideLe,
            ClotureeLe = s.ClotureeLe,
            MoniteurId = s.MoniteurId,
            MoniteurNom = db.Moniteurs.FirstOrDefault(m => m.Id == s.MoniteurId)?.NomComplet ?? "",
            NbTotal = lignes.Count,
            NbRendus = lignes.Count(a => a.Statut == StatutSortieEquipement.Rendu),
            NbManquants = lignes.Count(a => a.Statut == StatutSortieEquipement.Manquant),
        };
    }
}

public sealed class JournalRepositoryMemoire(BaseMemoire db) : IJournalRepository
{
    public Task AjouterAsync(EntreeJournal entree, CancellationToken ct = default)
    {
        entree.Id = db.ProchainId();
        db.Journal.Add(entree);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<EntreeJournal>> ListerRecentsAsync(int nombre, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EntreeJournal>>(db.Journal.OrderByDescending(j => j.Id).Take(nombre).ToList());
}
