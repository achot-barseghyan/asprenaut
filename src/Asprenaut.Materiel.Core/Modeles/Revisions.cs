namespace Asprenaut.Materiel.Core.Modeles;

// Révisions : contrôles périodiques (TIV, requalification, révision fabricant), atelier, séances TIV.
// Les valeurs des énumérations sont stockées en texte dans la base : ne jamais renommer un membre sans migration.

/// <summary>Type de contrôle périodique.</summary>
public enum TypeControle
{
    /// <summary>Inspection visuelle annuelle d'un bloc.</summary>
    Tiv,

    /// <summary>Requalification d'un bloc (épreuve hydraulique).</summary>
    Requalification,

    /// <summary>Révision ou contrôle des autres équipements (délai du fabricant).</summary>
    Revision,
}

/// <summary>Résultat d'un contrôle.</summary>
public enum ResultatControle
{
    Conforme,
    ASurveiller,
    Refuse,
}

/// <summary>Où en est une échéance de contrôle (calculé, non stocké).</summary>
public enum EtatEcheance
{
    EnRetard,
    CeMois,
    Bientot,
    AJour,
}

/// <summary>Un contrôle enregistré (historique, table controle).</summary>
public sealed class Controle
{
    public long Id { get; set; }
    public long EquipementId { get; set; }
    public TypeControle Type { get; set; }
    public DateOnly DateControle { get; set; }
    public ResultatControle Resultat { get; set; }
    public long? InspecteurId { get; set; }
    public string Notes { get; set; } = "";
    public DateTime SaisiLe { get; set; }
}

/// <summary>Un passage en atelier (envoi chez un prestataire, puis retour).</summary>
public sealed class PassageAtelier
{
    public long Id { get; set; }
    public long EquipementId { get; set; }
    public string Prestataire { get; set; } = "";
    public string Motif { get; set; } = "";
    public DateOnly EnvoyeLe { get; set; }
    public DateOnly? RetourPrevu { get; set; }
    public DateOnly? RevenuLe { get; set; }
}

/// <summary>Une séance de TIV prévue au club.</summary>
public sealed class SeanceTiv
{
    public long Id { get; set; }
    public DateOnly DateSeance { get; set; }
    public string Heure { get; set; } = "";
    public string Lieu { get; set; } = "";
}

/// <summary>Date du dernier contrôle d'un type pour un équipement.</summary>
public sealed class DernierControle
{
    public long EquipementId { get; set; }
    public TypeControle Type { get; set; }
    public DateOnly DateControle { get; set; }
}

/// <summary>Une ligne de l'échéancier : un contrôle à venir pour un équipement.</summary>
public sealed record LigneEcheance(
    long EquipementId,
    string Code,
    string Libelle,
    string Categorie,
    TypeControle Type,
    string Controle,
    DateOnly? Dernier,
    DateOnly Echeance,
    EtatEcheance Etat);

/// <summary>Ce que l'écran affiche avant d'enregistrer un contrôle sur l'équipement scanné.</summary>
public sealed record PreparationControle(
    Equipement Equipement,
    string Categorie,
    TypeControle TypePropose,
    IReadOnlyList<TypeControle> TypesPossibles,
    DateOnly? DerniereTiv,
    DateOnly? DerniereRequalification,
    DateOnly? DerniereRevision);

/// <summary>Équipement actuellement en atelier, pour l'affichage.</summary>
public sealed record LigneAtelier(long PassageId, long EquipementId, string Code, string Libelle, string Prestataire, string Motif, DateOnly EnvoyeLe, DateOnly? RetourPrevu);

/// <summary>Prochaine séance TIV avec les inspecteurs et les blocs à préparer.</summary>
public sealed record ProchaineSeance(SeanceTiv Seance, IReadOnlyList<string> Inspecteurs, IReadOnlyList<LigneEcheance> BlocsAPreparer);
