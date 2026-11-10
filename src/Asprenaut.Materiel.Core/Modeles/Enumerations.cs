namespace Asprenaut.Materiel.Core.Modeles;

// Les valeurs sont stockées en texte dans la base (nom exact du membre).
// Ne jamais renommer un membre sans migration SQL.

/// <summary>État physique d'un équipement.</summary>
public enum EtatEquipement
{
    EnService,
    HorsService,
    Reforme,
}

/// <summary>
/// Famille (pictogramme) d'un type de sortie. Les types eux-mêmes sont des données (<see cref="TypeDeSortie"/>, Paramètres) ;
/// la colonne sortie.type garde la famille du type choisi.
/// </summary>
public enum TypeSortie
{
    Piscine,
    Fosse,
    Mer,
    Lac,
    Bapteme,
    Autre,
}

/// <summary>Cycle de vie d'une sortie : préparation (scan départ) → en cours (scan retour) → clôturée.</summary>
public enum StatutSortie
{
    EnPreparation,
    EnCours,
    Cloturee,
}

/// <summary>Statut d'un équipement dans une sortie.</summary>
public enum StatutSortieEquipement
{
    Dehors,
    Rendu,
    Manquant,
}

/// <summary>État de la prochaine révision d'un équipement (calculé, non stocké).</summary>
public enum EtatRevision
{
    AJour,
    Proche,
    Depassee,
}

/// <summary>Statut calculé d'un équipement pour l'inventaire.</summary>
public enum StatutInventaire
{
    Disponible,
    Dehors,
    Manquant,
    HorsService,
    Reforme,
}

/// <summary>Thème de l'interface, choisi dans Paramètres (enregistré en texte dans parametres.json).</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ModeAffichage>))]
public enum ModeAffichage
{
    Sombre,
    Clair,
}

/// <summary>Montage du premier étage d'un détendeur sur le robinet du bloc.</summary>
public enum MontageDetendeur
{
    Etrier,
    Din,
}
