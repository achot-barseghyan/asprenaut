using System.Text.Json.Serialization;

namespace Asprenaut.Materiel.Core.Modeles;

// Compteur d'utilisation (Paramètres) : chaque retour scanné ajoute les utilisations de sa sortie (réglées par type de sortie,
// modifiables sur la sortie) ; la révision tombe au seuil d'utilisations
// depuis le dernier contrôle, ou à la date prévue, le premier atteint.

/// <summary>À quoi s'applique un seuil spécifique : tous les équipements d'un modèle (même nom), ou un seul équipement.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PorteeSeuil>))]
public enum PorteeSeuil
{
    Modele,
    Equipement,
}

/// <summary>Seuil propre à un modèle (nom de l'équipement, ex. « Détendeur Mares ») ou à un code (ex. DET-007).</summary>
public sealed class SeuilSpecifique
{
    public PorteeSeuil Portee { get; set; }

    /// <summary>Nom du modèle, ou code de l'équipement (en majuscules).</summary>
    public string Cle { get; set; } = "";

    /// <summary>Seuil en utilisations (nom gardé pour parametres.json).</summary>
    public int Sorties { get; set; } = 100;
}

/// <summary>Où en est un équipement par rapport à son seuil.</summary>
public enum EtatCompteur
{
    Normal,

    /// <summary>90 % du seuil atteint (si « Prévenir à 90 % » est activé) : à planifier.</summary>
    Proche,

    /// <summary>Seuil atteint : révision à faire.</summary>
    Atteint,
}

/// <summary>Utilisations d'un équipement depuis son dernier contrôle : somme des utilisations des sorties d'où il est rentré (lu en base).</summary>
public sealed class CompteurEquipement
{
    public long EquipementId { get; set; }
    public int Utilisations { get; set; }
}

/// <summary>Usage d'un équipement soumis à un seuil : « 92/100 utilisations ».</summary>
public sealed record UsageEquipement(int Utilisations, int Seuil, EtatCompteur Etat)
{
    public string Texte => $"{Utilisations}/{Seuil} utilisations";
}

/// <summary>Seuil spécifique avec ce qu'il concerne, pour l'écran Paramètres (« 4 équipements », « Détendeur complet »).</summary>
public sealed record SeuilSpecifiqueDecrit(PorteeSeuil Portee, string Cle, int Sorties, string Precision);
