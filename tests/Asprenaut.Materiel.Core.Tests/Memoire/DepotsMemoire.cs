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
    public List<Controle> Controles { get; } = [];
    public List<DetailComposant> Composants { get; } = [];
    public List<TypeDeSortie> TypesSortie { get; } = [];

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

    public Task<long> AjouterAsync(Categorie categorie, CancellationToken ct = default)
    {
        categorie.Id = db.ProchainId();
        db.Categories.Add(categorie);
        return Task.FromResult(categorie.Id);
    }

    public Task<int> CompterUtilisationsAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(db.Equipements.Count(e => e.CategorieId == id));

    public Task<bool> SupprimerAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(!db.Equipements.Any(e => e.CategorieId == id) && db.Categories.RemoveAll(c => c.Id == id) == 1);
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
                || e.Libelle.Contains(recherche, StringComparison.OrdinalIgnoreCase)
                || new[] { e.Taille ?? "", e.Marque, e.Modele, e.NumeroSerie, e.NumeroDeuxiemeEtage, e.NumeroOctopus }
                    .Any(v => v.Contains(recherche, StringComparison.OrdinalIgnoreCase)))
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
                    Marque = e.Marque,
                    Modele = e.Modele,
                    NumeroSerie = e.NumeroSerie,
                    Montage = e.Montage,
                    MarqueDeuxiemeEtage = e.MarqueDeuxiemeEtage,
                    ModeleDeuxiemeEtage = e.ModeleDeuxiemeEtage,
                    NumeroDeuxiemeEtage = e.NumeroDeuxiemeEtage,
                    DirectSystem = e.DirectSystem,
                    Manometre = e.Manometre,
                    Octopus = e.Octopus,
                    NumeroOctopus = e.NumeroOctopus,
                    DerniereRevision = db.Controles.Where(c => c.EquipementId == e.Id && c.Type == TypeControle.Revision && c.Composant.Length == 0).Select(c => (DateOnly?)c.DateControle).Max(),
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

    public Task<IReadOnlyList<CompteurEquipement>> ListerCompteursAsync(long? equipementId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CompteurEquipement>>(db.Equipements
            .Where(e => equipementId is null || e.Id == equipementId)
            .Select(e =>
            {
                var dernier = db.Controles.Where(c => c.EquipementId == e.Id && c.Composant.Length == 0).Select(c => (DateOnly?)c.DateControle).Max();
                var apres = dernier is { } d ? d.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) : DateTime.MinValue;
                return new CompteurEquipement
                {
                    EquipementId = e.Id,
                    Utilisations = db.Affectations.Where(a => a.EquipementId == e.Id && a.ScanneRetourLe is { } r && r >= apres)
                        .Sum(a => db.Sorties.First(s => s.Id == a.SortieId).Utilisations),
                };
            })
            .ToList());

    public Task<IReadOnlyList<DetailComposant>> ListerComposantsAsync(long equipementId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DetailComposant>>(db.Composants.Where(c => c.EquipementId == equipementId).OrderBy(c => c.Role).ToList());

    public Task EnregistrerComposantAsync(DetailComposant detail, CancellationToken ct = default)
    {
        db.Composants.RemoveAll(c => c.EquipementId == detail.EquipementId && c.Role == detail.Role);
        db.Composants.Add(detail);
        return Task.CompletedTask;
    }
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

    public Task<bool> SupprimerAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(!db.Affectations.Any(a => a.SortieId == id) && db.Sorties.RemoveAll(s => s.Id == id) == 1);

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

    public Task<IReadOnlyList<PassageSortie>> ListerPassagesAsync(long equipementId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PassageSortie>>(db.Affectations
            .Where(a => a.EquipementId == equipementId)
            .OrderByDescending(a => a.ScanneDepartLe)
            .Select(a =>
            {
                var s = db.Sorties.First(x => x.Id == a.SortieId);
                return new PassageSortie
                {
                    SortieId = s.Id,
                    Intitule = s.Intitule,
                    DateSortie = s.DateSortie,
                    MoniteurNom = db.Moniteurs.FirstOrDefault(m => m.Id == s.MoniteurId)?.NomComplet ?? "",
                    ClotureeLe = s.ClotureeLe,
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

    public Task<IReadOnlyList<PassageBilan>> ListerPassagesPeriodeAsync(DateOnly du, DateOnly au, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PassageBilan>>(db.Affectations
            .Select(a => (a, s: db.Sorties.First(x => x.Id == a.SortieId), e: db.Equipements.First(x => x.Id == a.EquipementId)))
            .Where(t => t.s.DateSortie >= du && t.s.DateSortie <= au)
            .Select(t => new PassageBilan
            {
                SortieId = t.s.Id,
                DateSortie = t.s.DateSortie,
                TypeSortie = t.s.Type,
                TypeId = t.s.TypeId,
                TypeNom = db.TypesSortie.FirstOrDefault(x => x.Id == t.s.TypeId)?.Nom,
                StatutSortie = t.s.Statut,
                Intitule = t.s.Intitule,
                MoniteurNom = db.Moniteurs.FirstOrDefault(m => m.Id == t.s.MoniteurId)?.NomComplet ?? "",
                ClotureeLe = t.s.ClotureeLe,
                EquipementId = t.e.Id,
                Code = t.e.Code,
                Libelle = t.e.Libelle,
                Categorie = db.Categories.FirstOrDefault(c => c.Id == t.e.CategorieId)?.Nom ?? "",
                Taille = t.e.Taille,
                Statut = t.a.Statut,
                ScanneRetourLe = t.a.ScanneRetourLe,
            })
            .ToList());

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
            TypeId = s.TypeId,
            Utilisations = s.Utilisations,
            TypeNom = db.TypesSortie.FirstOrDefault(x => x.Id == s.TypeId)?.Nom,
            Intitule = s.Intitule,
            Lieu = s.Lieu,
            Statut = s.Statut,
            DepartValideLe = s.DepartValideLe,
            ClotureeLe = s.ClotureeLe,
            MoniteurId = s.MoniteurId,
            MoniteurNom = db.Moniteurs.FirstOrDefault(m => m.Id == s.MoniteurId)?.NomComplet ?? "",
            MoniteurGenre = db.Moniteurs.FirstOrDefault(m => m.Id == s.MoniteurId)?.Genre ?? GenreResponsable.Moniteur,
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

    public Task<IReadOnlyList<LigneJournal>> ListerAsync(DateTime? depuisUtc, int nombreMax, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LigneJournal>>(db.Journal
            .Where(j => depuisUtc is null || j.Horodatage >= depuisUtc)
            .OrderByDescending(j => j.Horodatage)
            .ThenByDescending(j => j.Id)
            .Take(nombreMax)
            .Select(j =>
            {
                var e = db.Equipements.FirstOrDefault(x => x.Id == j.EquipementId);
                var s = db.Sorties.FirstOrDefault(x => x.Id == j.SortieId);
                var lignes = db.Affectations.Where(a => s is not null && a.SortieId == s.Id).ToList();
                return new LigneJournal
                {
                    Id = j.Id,
                    Horodatage = j.Horodatage,
                    Action = j.Action,
                    Details = j.Details,
                    EquipementId = e?.Id,
                    Code = e?.Code,
                    Libelle = e?.Libelle,
                    Categorie = db.Categories.FirstOrDefault(c => c.Id == e?.CategorieId)?.Nom,
                    SortieId = s?.Id,
                    SortieIntitule = s?.Intitule,
                    SortieDate = s?.DateSortie,
                    SortieType = s?.Type,
                    SortieTypeNom = db.TypesSortie.FirstOrDefault(x => s is not null && x.Id == s.TypeId)?.Nom,
                    SortieLieu = s?.Lieu,
                    MoniteurNom = db.Moniteurs.FirstOrDefault(m => m.Id == s?.MoniteurId)?.NomComplet,
                    SortieNbEquipements = lignes.Count,
                    SortieNbManquants = lignes.Count(a => a.Statut == StatutSortieEquipement.Manquant),
                };
            })
            .ToList());
}

public sealed class TypeSortieRepositoryMemoire(BaseMemoire db) : ITypeSortieRepository
{
    private TypeDeSortie AvecNombre(TypeDeSortie t)
    {
        t.NbSorties = db.Sorties.Count(s => s.TypeId == t.Id);
        return t;
    }

    public Task<IReadOnlyList<TypeDeSortie>> ListerAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TypeDeSortie>>(db.TypesSortie.OrderBy(t => t.Ordre).ThenBy(t => t.Nom).Select(AvecNombre).ToList());

    public Task<TypeDeSortie?> ObtenirAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(db.TypesSortie.Where(t => t.Id == id).Select(AvecNombre).FirstOrDefault());

    public Task<long> AjouterAsync(TypeDeSortie type, CancellationToken ct = default)
    {
        type.Id = db.ProchainId();
        db.TypesSortie.Add(type);
        return Task.FromResult(type.Id);
    }

    public Task MettreAJourAsync(TypeDeSortie type, CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> SupprimerAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(!db.Sorties.Any(s => s.TypeId == id) && db.TypesSortie.RemoveAll(t => t.Id == id) == 1);
}
