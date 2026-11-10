namespace Asprenaut.Materiel.Core.Modeles;

// Écran « Bilan de saison » : utilisation, pertes et révisions d'une saison (1er septembre → 31 août), pour l'AG.

/// <summary>Passage d'un équipement dans une sortie, avec la sortie et l'équipement (lecture pour le bilan).</summary>
public sealed class PassageBilan
{
    public long SortieId { get; set; }
    public DateOnly DateSortie { get; set; }
    public TypeSortie TypeSortie { get; set; }
    public long? TypeId { get; set; }
    public string? TypeNom { get; set; }
    public StatutSortie StatutSortie { get; set; }
    public string Intitule { get; set; } = "";
    public string MoniteurNom { get; set; } = "";
    public DateTime? ClotureeLe { get; set; }
    public long EquipementId { get; set; }
    public string Code { get; set; } = "";
    public string Libelle { get; set; } = "";
    public string Categorie { get; set; } = "";
    public string? Taille { get; set; }
    public StatutSortieEquipement Statut { get; set; }
    public DateTime? ScanneRetourLe { get; set; }
}

/// <summary>Une saison proposée au choix : 2025-2026, 2026-2027 (en cours).</summary>
public sealed record SaisonBilan(int AnneeDebut, bool EnCours)
{
    public DateOnly Debut => new(AnneeDebut, 9, 1);

    public DateOnly Fin => new(AnneeDebut + 1, 8, 31);

    public string Libelle => $"{AnneeDebut}-{AnneeDebut + 1}";
}

/// <summary>Un grand chiffre du bilan : « Sorties 75 », avec une précision dessous.</summary>
public sealed record ChiffreBilan(string Libelle, string Valeur, string Precision);

/// <summary>Nombre de sorties d'un mois de la saison (« Sep », 6).</summary>
public sealed record MoisBilan(string Libelle, string LibelleLong, int Sorties);

/// <summary>Nombre de sorties d'un type (« Piscine », 42), avec la famille du type (pictogramme).</summary>
public sealed record TypeBilan(TypeSortie Type, string Libelle, int Sorties);

/// <summary>
/// Tension sur le stock d'un groupe de matériel (catégorie et taille) : part du stock sortie lors de la sortie
/// la plus chargée, et nombre de sorties où il n'en restait plus au local.
/// </summary>
public sealed record TensionBilan(string Libelle, int Stock, int Pic, int Ruptures)
{
    /// <summary>Pic d'occupation en pourcentage (0 à 100).</summary>
    public int Pourcentage => Stock == 0 ? 0 : Math.Min(100, (int)Math.Round(100.0 * Pic / Stock));
}

/// <summary>Un équipement et son nombre de sorties dans la saison.</summary>
public sealed record UtilisationBilan(string Code, string Libelle, int Sorties);

/// <summary>Un équipement noté manquant à la clôture d'une sortie de la saison : perdu (toujours manquant) ou retrouvé.</summary>
public sealed record PerteBilan(string Code, string Libelle, string Sortie, DateOnly DateSortie, string Moniteur, bool Retrouve, DateOnly? RetrouveLe);

/// <summary>Nombre de contrôles faits d'une famille (TIV, Requalification, Détendeurs...).</summary>
public sealed record RevisionsBilan(string Libelle, int Nombre);

/// <summary>Bilan complet d'une saison.</summary>
public sealed record Bilan(
    SaisonBilan Saison,
    int Sorties,
    int EquipementsSortis,
    IReadOnlyList<ChiffreBilan> Chiffres,
    IReadOnlyList<MoisBilan> Mois,
    string NoteMois,
    IReadOnlyList<TypeBilan> Types,
    IReadOnlyList<TensionBilan> Tension,
    IReadOnlyList<UtilisationBilan> PlusUtilises,
    IReadOnlyList<UtilisationBilan> PeuSortis,
    IReadOnlyList<PerteBilan> Pertes,
    IReadOnlyList<RevisionsBilan> Revisions)
{
    public int NombreRevisions => Revisions.Sum(r => r.Nombre);
}
