using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.Core.Abstractions;

// Contrats d'accès aux données. Implémentés en SQLite dans Asprenaut.Materiel.Data,
// et en mémoire dans les tests de Core. Toutes les méthodes sont asynchrones.

public interface ICategorieRepository
{
    Task<IReadOnlyList<Categorie>> ListerAsync(CancellationToken ct = default);
}

public interface IEquipementRepository
{
    Task<Equipement?> ObtenirAsync(long id, CancellationToken ct = default);

    /// <summary>Recherche par code exact (déjà normalisé).</summary>
    Task<Equipement?> ObtenirParCodeAsync(string code, CancellationToken ct = default);

    Task<IReadOnlyList<LigneInventaire>> ListerInventaireAsync(string? recherche, long? categorieId, CancellationToken ct = default);

    Task<long> AjouterAsync(Equipement equipement, CancellationToken ct = default);

    Task MettreAJourAsync(Equipement equipement, CancellationToken ct = default);
}

public interface IMoniteurRepository
{
    Task<Moniteur?> ObtenirAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<Moniteur>> ListerAsync(bool actifsSeulement, CancellationToken ct = default);

    Task<long> AjouterAsync(Moniteur moniteur, CancellationToken ct = default);

    Task MettreAJourAsync(Moniteur moniteur, CancellationToken ct = default);
}

public interface ISortieRepository
{
    Task<Sortie?> ObtenirAsync(long id, CancellationToken ct = default);

    Task<long> AjouterAsync(Sortie sortie, CancellationToken ct = default);

    Task MettreAJourAsync(Sortie sortie, CancellationToken ct = default);

    Task<SortieResume?> ObtenirResumeAsync(long id, CancellationToken ct = default);

    /// <summary>Sorties clôturées si <paramref name="cloturees"/>, sinon en préparation et en cours.</summary>
    Task<IReadOnlyList<SortieResume>> ListerResumesAsync(bool cloturees, CancellationToken ct = default);

    // --- Matériel d'une sortie (table sortie_equipement) ---

    Task<IReadOnlyList<LigneSortie>> ListerLignesAsync(long sortieId, CancellationToken ct = default);

    Task<IReadOnlyList<SortieEquipement>> ListerAffectationsAsync(long sortieId, CancellationToken ct = default);

    Task<SortieEquipement?> ObtenirAffectationAsync(long sortieId, long equipementId, CancellationToken ct = default);

    /// <summary>Affectation "Dehors" de cet équipement, toutes sorties confondues (au plus une).</summary>
    Task<SortieEquipement?> ObtenirAffectationDehorsAsync(long equipementId, CancellationToken ct = default);

    /// <summary>Affectation "Manquant" la plus récente de cet équipement, toutes sorties confondues.</summary>
    Task<SortieEquipement?> ObtenirDerniereAffectationManquanteAsync(long equipementId, CancellationToken ct = default);

    Task<long> AjouterAffectationAsync(SortieEquipement affectation, CancellationToken ct = default);

    Task MettreAJourAffectationAsync(SortieEquipement affectation, CancellationToken ct = default);

    Task SupprimerAffectationAsync(long affectationId, CancellationToken ct = default);

    /// <summary>Enregistre la clôture (sortie + affectations modifiées) en une seule transaction.</summary>
    Task EnregistrerClotureAsync(Sortie sortie, IReadOnlyList<SortieEquipement> affectationsModifiees, CancellationToken ct = default);
}

/// <summary>Contrôles, passages en atelier et séances TIV.</summary>
public interface IRevisionRepository
{
    Task<long> AjouterControleAsync(Controle controle, CancellationToken ct = default);

    /// <summary>Date du dernier contrôle de chaque type, pour chaque équipement.</summary>
    Task<IReadOnlyList<DernierControle>> ListerDerniersControlesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Controle>> ListerControlesAsync(long equipementId, CancellationToken ct = default);

    Task<long> AjouterPassageAtelierAsync(PassageAtelier passage, CancellationToken ct = default);

    /// <summary>Passage en cours (non revenu) de cet équipement, ou null.</summary>
    Task<PassageAtelier?> ObtenirPassageEnCoursAsync(long equipementId, CancellationToken ct = default);

    Task<PassageAtelier?> ObtenirPassageAsync(long id, CancellationToken ct = default);

    Task MettreAJourPassageAsync(PassageAtelier passage, CancellationToken ct = default);

    Task<IReadOnlyList<LigneAtelier>> ListerAtelierEnCoursAsync(CancellationToken ct = default);

    Task<long> AjouterSeanceAsync(SeanceTiv seance, CancellationToken ct = default);

    /// <summary>Première séance à partir de cette date, ou null.</summary>
    Task<SeanceTiv?> ProchaineSeanceAsync(DateOnly aPartirDu, CancellationToken ct = default);
}

public interface IJournalRepository
{
    Task AjouterAsync(EntreeJournal entree, CancellationToken ct = default);

    Task<IReadOnlyList<EntreeJournal>> ListerRecentsAsync(int nombre, CancellationToken ct = default);
}
