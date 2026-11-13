namespace Asprenaut.Materiel.Core.Modeles;

// Problème signalé sur un équipement (au retour d'une sortie ou depuis l'inventaire), migration 013.
// Les valeurs de l'énumération sont stockées en texte dans la base : ne jamais renommer un membre sans migration.

/// <summary>Peut-il repartir ? À surveiller : oui, contrôle à prévoir. Hors service : bloqué jusqu'à réparation.</summary>
public enum GraviteSignalement
{
    ASurveiller,
    HorsService,
}

/// <summary>
/// Un problème signalé (table signalement). Ouvert tant que <see cref="TraiteLe"/> est vide : il apparaît dans Révisions
/// › À prévoir et se ferme à la prochaine intervention enregistrée sur l'équipement. Un seul ouvert par équipement.
/// </summary>
public sealed class Signalement
{
    public long Id { get; set; }
    public long EquipementId { get; set; }

    /// <summary>Sortie au retour de laquelle il a été signalé ; null depuis l'inventaire.</summary>
    public long? SortieId { get; set; }

    /// <summary>Ce qui ne va pas (« Fuite / bulles »), choisi dans <see cref="Services.ServiceSignalements.CausesPour"/>.</summary>
    public string Cause { get; set; } = "";

    public GraviteSignalement Gravite { get; set; }

    /// <summary>Précision facultative (« fuite au raccord du flexible MP »).</summary>
    public string Precision { get; set; } = "";

    public DateTime SignaleLe { get; set; }

    /// <summary>Vrai si c'est ce signalement qui a mis l'équipement hors service (il le remet en service s'il redescend à « À surveiller »).</summary>
    public bool MisHorsService { get; set; }

    /// <summary>Intervention enregistrée depuis : le signalement est traité.</summary>
    public DateTime? TraiteLe { get; set; }

    public bool Ouvert => TraiteLe is null;
}

/// <summary>Signalement à enregistrer (fenêtre « Problème sur A07 »). Remplace le signalement ouvert de l'équipement s'il y en a un.</summary>
public sealed record NouveauSignalement(long EquipementId, long? SortieId, string Cause, GraviteSignalement Gravite, string? Precision = null);

/// <summary>
/// Une tuile de la fenêtre « Problème sur A07 » : libellé, pictogramme (clé de <c>Icones.PictosCause</c> dans l'App) et
/// précision obligatoire ou non. Réglable par catégorie dans Paramètres › Problèmes signalés (<see cref="Reglages.CausesSignalement"/>).
/// </summary>
public sealed class CauseSignalement
{
    public string Libelle { get; set; } = "";

    public string Pictogramme { get; set; } = "Autre";

    /// <summary>Le champ « Précision » doit être rempli quand cette tuile est choisie.</summary>
    public bool PrecisionObligatoire { get; set; }

    public CauseSignalement Copie() => new() { Libelle = Libelle, Pictogramme = Pictogramme, PrecisionObligatoire = PrecisionObligatoire };
}