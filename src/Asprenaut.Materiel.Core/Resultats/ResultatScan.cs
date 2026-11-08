using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.Core.Resultats;

public enum NiveauResultat
{
    Succes,
    Avertissement,
    Erreur,
}

/// <summary>Suite possible proposée à l'utilisateur après un scan.</summary>
public enum ActionProposee
{
    Aucune,

    /// <summary>Code inconnu : proposer de créer l'équipement avec ce code.</summary>
    CreerEquipement,

    /// <summary>Équipement dehors dans une autre sortie : proposer de le rentrer dans cette sortie-là.</summary>
    RentrerDansAutreSortie,
}

/// <summary>
/// Résultat d'un scan. L'écran se contente de l'afficher (couleur, son, message)
/// et, si besoin, de proposer l'action indiquée.
/// </summary>
public sealed record ResultatScan(
    NiveauResultat Niveau,
    string Message,
    Equipement? Equipement = null,
    ActionProposee Action = ActionProposee.Aucune,
    string? Code = null,
    long? AutreSortieId = null)
{
    public bool EstSucces => Niveau == NiveauResultat.Succes;

    public static ResultatScan Succes(string message, Equipement? equipement = null) =>
        new(NiveauResultat.Succes, message, equipement, Code: equipement?.Code);

    public static ResultatScan Avertissement(string message, Equipement? equipement = null) =>
        new(NiveauResultat.Avertissement, message, equipement, Code: equipement?.Code);

    public static ResultatScan Erreur(string message, Equipement? equipement = null) =>
        new(NiveauResultat.Erreur, message, equipement, Code: equipement?.Code);
}

/// <summary>Résultat d'une opération simple (créer, valider, clôturer...).</summary>
public sealed record ResultatOperation(bool Reussi, string Message, long? Id = null)
{
    public static ResultatOperation Ok(string message, long? id = null) => new(true, message, id);

    public static ResultatOperation Echec(string message) => new(false, message);
}

/// <summary>Ce que la clôture va provoquer, pour la confirmation.</summary>
/// <param name="ConfirmationNecessaire">Demander confirmation avant de clôturer (réglage activé et matériel encore dehors).</param>
public sealed record ApercuCloture(int NbManquants, string MessageConfirmation, bool ConfirmationNecessaire);
