namespace Asprenaut.Materiel.Core.Modeles;

// Écran Historique : journal lisible, filtré par type et par période, et « vie » d'un équipement.

/// <summary>Famille d'un événement (filtres et couleurs de l'écran Historique).</summary>
public enum TypeEvenement
{
    Depart,
    Retour,
    Sortie,
    Revision,
    Materiel,
    Manquant,

    /// <summary>Moniteurs, séances, données de démonstration... : visibles seulement dans « Tout ».</summary>
    Autre,
}

/// <summary>Période affichée par l'écran Historique.</summary>
public enum PeriodeHistorique
{
    SeptJours,
    TrenteJours,
    Saison,
    Tout,
}

/// <summary>Ligne du journal avec l'équipement et la sortie concernés (lus par jointure).</summary>
public sealed class LigneJournal
{
    public long Id { get; set; }
    public DateTime Horodatage { get; set; }
    public string Action { get; set; } = "";
    public string Details { get; set; } = "";
    public long? EquipementId { get; set; }
    public string? Code { get; set; }
    public string? Libelle { get; set; }
    public string? Categorie { get; set; }
    public long? SortieId { get; set; }
    public string? SortieIntitule { get; set; }
    public DateOnly? SortieDate { get; set; }
    public TypeSortie? SortieType { get; set; }

    /// <summary>Nom du type de la sortie (table type_sortie), s'il est connu.</summary>
    public string? SortieTypeNom { get; set; }
    public string? SortieLieu { get; set; }
    public string? MoniteurNom { get; set; }

    /// <summary>Nombre d'équipements de la sortie (aujourd'hui).</summary>
    public int SortieNbEquipements { get; set; }

    /// <summary>Nombre d'équipements encore manquants dans la sortie (aujourd'hui).</summary>
    public int SortieNbManquants { get; set; }
}

/// <summary>
/// Un événement affiché : une entrée du journal, ou plusieurs scans regroupés (« Retour : 3 équipements »).
/// <see cref="Moment"/> est en heure locale.
/// </summary>
public sealed record EvenementHistorique(
    long Id,
    DateTime Moment,
    TypeEvenement Type,
    string Titre,
    string Detail,
    string? Code,
    int Nombre = 1);

/// <summary>Passage d'un équipement dans une sortie (table sortie_equipement + sortie + moniteur).</summary>
public sealed class PassageSortie
{
    public long SortieId { get; set; }
    public string Intitule { get; set; } = "";
    public DateOnly DateSortie { get; set; }
    public string MoniteurNom { get; set; } = "";
    public DateTime? ClotureeLe { get; set; }
    public StatutSortieEquipement Statut { get; set; }
    public DateTime ScanneDepartLe { get; set; }
    public DateTime? ScanneRetourLe { get; set; }
}

/// <summary>Une étape de la vie d'un équipement. <see cref="Moment"/> en heure locale ; sans heure si <see cref="DateSeule"/>.</summary>
public sealed record EtapeVie(DateTime Moment, bool DateSeule, TypeEvenement Type, string Titre, string Detail);

/// <summary>Fiche « Vie d'un équipement » : où il est, combien de sorties, prochaine échéance, et tout son parcours.</summary>
public sealed record VieEquipement(
    Equipement Equipement,
    string Categorie,
    int SortiesSaison,
    StatutInventaire Statut,
    string LibelleStatut,
    string PrecisionStatut,
    string Echeance,
    EtatRevision EtatEcheance,
    IReadOnlyList<EtapeVie> Etapes);
