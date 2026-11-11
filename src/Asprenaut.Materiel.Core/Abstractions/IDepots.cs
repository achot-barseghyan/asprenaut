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

    /// <summary>
    /// Compteur d'utilisation : retours scannés de chaque équipement (ou d'un seul) après le jour de son dernier contrôle
    /// (tous types), ou depuis toujours s'il n'en a pas.
    /// </summary>
    Task<IReadOnlyList<CompteurEquipement>> ListerCompteursAsync(long? equipementId, CancellationToken ct = default);
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

    /// <summary>Supprime la sortie si aucun matériel n'y est rattaché ; faux sinon (rien n'est supprimé).</summary>
    Task<bool> SupprimerAsync(long id, CancellationToken ct = default);

    Task<SortieResume?> ObtenirResumeAsync(long id, CancellationToken ct = default);

    /// <summary>Sorties clôturées si <paramref name="cloturees"/>, sinon en préparation et en cours.</summary>
    Task<IReadOnlyList<SortieResume>> ListerResumesAsync(bool cloturees, CancellationToken ct = default);

    // --- Matériel d'une sortie (table sortie_equipement) ---

    Task<IReadOnlyList<LigneSortie>> ListerLignesAsync(long sortieId, CancellationToken ct = default);

    /// <summary>Toutes les sorties où cet équipement est passé, la plus récente d'abord.</summary>
    Task<IReadOnlyList<PassageSortie>> ListerPassagesAsync(long equipementId, CancellationToken ct = default);

    /// <summary>Matériel de toutes les sorties datées du <paramref name="du"/> au <paramref name="au"/> inclus (bilan de saison).</summary>
    Task<IReadOnlyList<PassageBilan>> ListerPassagesPeriodeAsync(DateOnly du, DateOnly au, CancellationToken ct = default);

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

    /// <summary>Contrôles faits du <paramref name="du"/> au <paramref name="au"/> inclus (bilan de saison).</summary>
    Task<IReadOnlyList<Controle>> ListerControlesPeriodeAsync(DateOnly du, DateOnly au, CancellationToken ct = default);

    Task<long> AjouterPassageAtelierAsync(PassageAtelier passage, CancellationToken ct = default);

    /// <summary>Passage en cours (non revenu) de cet équipement, ou null.</summary>
    Task<PassageAtelier?> ObtenirPassageEnCoursAsync(long equipementId, CancellationToken ct = default);

    Task<PassageAtelier?> ObtenirPassageAsync(long id, CancellationToken ct = default);

    Task MettreAJourPassageAsync(PassageAtelier passage, CancellationToken ct = default);

    Task<IReadOnlyList<LigneAtelier>> ListerAtelierEnCoursAsync(CancellationToken ct = default);

    /// <summary>Tous les passages en atelier de cet équipement, le plus récent d'abord.</summary>
    Task<IReadOnlyList<PassageAtelier>> ListerPassagesAtelierAsync(long equipementId, CancellationToken ct = default);

    Task<long> AjouterSeanceAsync(SeanceTiv seance, CancellationToken ct = default);

    /// <summary>Équipements ajoutés à une séance TIV (colonne « Planifié »).</summary>
    Task<IReadOnlyList<long>> ListerPlanifiesAsync(long seanceId, CancellationToken ct = default);

    /// <summary>Ajoute un équipement à une séance (sans doublon).</summary>
    Task AjouterASeanceAsync(long seanceId, long equipementId, CancellationToken ct = default);

    /// <summary>Retire l'équipement de toutes les séances (intervention faite, ou retiré à la main).</summary>
    Task RetirerDesSeancesAsync(long equipementId, CancellationToken ct = default);

    /// <summary>Première séance à partir de cette date, ou null.</summary>
    Task<SeanceTiv?> ProchaineSeanceAsync(DateOnly aPartirDu, CancellationToken ct = default);
}

/// <summary>Types de sortie (table type_sortie), avec le nombre de sorties de chacun.</summary>
public interface ITypeSortieRepository
{
    /// <summary>Tous les types, masqués compris, dans l'ordre d'affichage.</summary>
    Task<IReadOnlyList<TypeDeSortie>> ListerAsync(CancellationToken ct = default);

    Task<TypeDeSortie?> ObtenirAsync(long id, CancellationToken ct = default);

    Task<long> AjouterAsync(TypeDeSortie type, CancellationToken ct = default);

    Task MettreAJourAsync(TypeDeSortie type, CancellationToken ct = default);

    /// <summary>Supprime le type s'il n'est utilisé par aucune sortie ; faux sinon (rien n'est supprimé).</summary>
    Task<bool> SupprimerAsync(long id, CancellationToken ct = default);
}

/// <summary>Manuels des fabricants (table manuel, migration 009).</summary>
public interface IManuelRepository
{
    /// <summary>Tous les manuels, avec le nom de leur catégorie et leur nombre d'équipements liés, triés par marque puis titre.</summary>
    Task<IReadOnlyList<Manuel>> ListerAsync(CancellationToken ct = default);

    Task<Manuel?> ObtenirAsync(long id, CancellationToken ct = default);

    Task<long> AjouterAsync(Manuel manuel, CancellationToken ct = default);

    Task MettreAJourAsync(Manuel manuel, CancellationToken ct = default);

    Task SupprimerAsync(long id, CancellationToken ct = default);

    /// <summary>Codes des équipements liés : même catégorie, même marque (sans tenir compte des majuscules), hors réformés.</summary>
    Task<IReadOnlyList<string>> ListerCodesEquipementsAsync(long manuelId, CancellationToken ct = default);

    /// <summary>Manuel de chaque équipement qui en a un (même catégorie, même marque) : id de l'équipement → id du manuel (le plus ancien).</summary>
    Task<IReadOnlyDictionary<long, long>> ListerManuelsDesEquipementsAsync(CancellationToken ct = default);
}

/// <summary>
/// Fichiers PDF des manuels, copiés dans un dossier à côté de la base : lisibles sans internet,
/// même si la clé USB d'origine n'est plus là.
/// </summary>
public interface IStockageManuels
{
    /// <summary>Copie le fichier et retourne le nom sous lequel il est rangé (un nom déjà pris reçoit un suffixe).</summary>
    string Importer(string cheminSource);

    /// <summary>Chemin complet d'un fichier rangé.</summary>
    string Chemin(string fichier);

    bool Existe(string fichier);

    /// <summary>Supprime un fichier rangé (sans erreur s'il n'existe plus).</summary>
    void Supprimer(string fichier);
}

/// <summary>Lecture de toutes les tables pour l'export CSV complet.</summary>
public interface IExportRepository
{
    Task<IReadOnlyList<TableExport>> ListerTablesAsync(CancellationToken ct = default);
}

public interface IJournalRepository
{
    Task AjouterAsync(EntreeJournal entree, CancellationToken ct = default);

    Task<IReadOnlyList<EntreeJournal>> ListerRecentsAsync(int nombre, CancellationToken ct = default);

    /// <summary>
    /// Entrées depuis <paramref name="depuisUtc"/> (tout si null), les plus récentes d'abord, au plus <paramref name="nombreMax"/>,
    /// avec l'équipement, la sortie et son moniteur.
    /// </summary>
    Task<IReadOnlyList<LigneJournal>> ListerAsync(DateTime? depuisUtc, int nombreMax, CancellationToken ct = default);
}
