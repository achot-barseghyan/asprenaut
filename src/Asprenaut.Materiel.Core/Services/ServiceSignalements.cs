using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>
/// Problèmes signalés par les moniteurs (au retour d'une sortie ou depuis l'inventaire). Consultation et « À surveiller »
/// libres ; « Hors service » (et le retour de « Hors service » à « À surveiller ») demande le code : action Révisions.
/// Le signalement part dans Révisions › À prévoir et dans la fiche EPI ; la prochaine intervention le ferme.
/// </summary>
public sealed class ServiceSignalements(
    ISignalementRepository signalements,
    IEquipementRepository equipements,
    ICategorieRepository categories,
    ISortieRepository sorties,
    IRevisionRepository revisions,
    IJournalRepository journal,
    IHorloge horloge,
    ServiceGestion? gestion = null)
{
    public const int LongueurMaxCause = 60;
    public const int LongueurMaxPrecision = 200;
    public const string CauseAutre = "Autre";

    /// <summary>
    /// Choix proposés selon la catégorie (maquette « Retour du matériel »), « Autre » en dernier.
    /// Catégorie inconnue : choix génériques.
    /// </summary>
    public static IReadOnlyList<string> CausesPour(string? categorie)
    {
        var c = (categorie ?? "").Trim();
        bool Est(params string[] debuts) => debuts.Any(d => c.StartsWith(d, StringComparison.OrdinalIgnoreCase));
        if (Est("Détendeur", "Detendeur"))
        {
            return ["Fuite / bulles", "Débit continu", "Respiration dure", "Embout abîmé", "Flexible abîmé", CauseAutre];
        }

        if (Est("Gilet", "Stab"))
        {
            return ["Ne tient pas l'air", "Inflateur qui colle", "Purge qui reste bloquée", "Sangle ou boucle cassée", "Poche à plombs abîmée", CauseAutre];
        }

        if (Est("Bloc"))
        {
            return ["Robinet qui fuit", "Robinet dur", "Choc / bosse", "Corrosion visible", "Joint torique abîmé", CauseAutre];
        }

        if (Est("Masque"))
        {
            return ["Verre fêlé", "Jupe déchirée", "Sangle cassée", "Boucle cassée", "Buée permanente", CauseAutre];
        }

        if (Est("Palme"))
        {
            return ["Sangle ou talon cassé", "Voilure fendue", "Boucle cassée", CauseAutre];
        }

        if (Est("Combinaison"))
        {
            return ["Déchirure", "Fermeture bloquée", "Coutures ouvertes", CauseAutre];
        }

        if (Est("Ordinateur"))
        {
            return ["Ne s'allume plus", "Pile faible", "Bracelet cassé", CauseAutre];
        }

        return ["Cassé", "Abîmé", "Ne fonctionne plus", CauseAutre];
    }

    public static string Libelle(GraviteSignalement gravite) => gravite == GraviteSignalement.HorsService ? "Hors service" : "À surveiller";

    /// <summary>Pastille courte : « À surveiller · Fuite / bulles ».</summary>
    public static string Resume(Signalement s) => $"{Libelle(s.Gravite)} · {s.Cause}";

    /// <summary>Signalement ouvert de l'équipement, ou null.</summary>
    public Task<Signalement?> OuvertAsync(long equipementId, CancellationToken ct = default) =>
        signalements.ObtenirOuvertAsync(equipementId, ct);

    /// <summary>Signalements ouverts par équipement (listes du retour, inventaire).</summary>
    public async Task<IReadOnlyDictionary<long, Signalement>> OuvertsAsync(CancellationToken ct = default) =>
        (await signalements.ListerOuvertsAsync(ct)).GroupBy(s => s.EquipementId).ToDictionary(g => g.Key, g => g.First());

    /// <summary>
    /// Signale un problème, ou modifie le signalement ouvert de l'équipement. Hors service : code gestionnaire, et pas
    /// s'il est dehors (le scanner au retour d'abord) ; l'équipement ne peut plus sortir. Redescendre à « À surveiller » :
    /// code aussi, et l'équipement repasse en service si c'est ce signalement qui l'avait arrêté (et s'il n'est pas en atelier).
    /// </summary>
    public async Task<ResultatOperation> SignalerAsync(NouveauSignalement n, CancellationToken ct = default)
    {
        if (await equipements.ObtenirAsync(n.EquipementId, ct) is not { } equipement)
        {
            return ResultatOperation.Echec("Équipement introuvable.");
        }

        if (equipement.Etat == EtatEquipement.Reforme)
        {
            return ResultatOperation.Echec($"{equipement.Code} est réformé : il ne sort plus.");
        }

        var cause = (n.Cause ?? "").Trim();
        if (cause.Length == 0)
        {
            return ResultatOperation.Echec("Choisissez ce qui ne va pas.");
        }

        if (cause.Length > LongueurMaxCause)
        {
            return ResultatOperation.Echec($"Problème : {LongueurMaxCause} caractères au plus.");
        }

        var precision = (n.Precision ?? "").Trim();
        if (precision.Length > LongueurMaxPrecision)
        {
            return ResultatOperation.Echec($"Précision : {LongueurMaxPrecision} caractères au plus.");
        }

        var existant = await signalements.ObtenirOuvertAsync(equipement.Id, ct);
        var versHorsService = n.Gravite == GraviteSignalement.HorsService && existant?.Gravite != GraviteSignalement.HorsService;
        var leveHorsService = n.Gravite == GraviteSignalement.ASurveiller && existant?.Gravite == GraviteSignalement.HorsService;
        if ((versHorsService || leveHorsService) && gestion?.Exiger(ActionProtegee.Revisions) is { Reussi: false } refus)
        {
            return refus;
        }

        if (versHorsService && await sorties.ObtenirAffectationDehorsAsync(equipement.Id, ct) is not null)
        {
            return ResultatOperation.Echec($"{equipement.Code} est dehors dans une sortie. Scannez son retour avant de le mettre hors service.");
        }

        var sortieId = n.SortieId ?? existant?.SortieId;
        if (n.SortieId is { } id && await sorties.ObtenirAsync(id, ct) is null)
        {
            sortieId = existant?.SortieId;
        }

        var signalement = existant ?? new Signalement { EquipementId = equipement.Id, SignaleLe = horloge.MaintenantUtc };
        signalement.SortieId = sortieId;
        signalement.Cause = cause;
        signalement.Gravite = n.Gravite;
        signalement.Precision = precision;

        var etatChange = false;
        if (versHorsService && equipement.Etat == EtatEquipement.EnService)
        {
            equipement.Etat = EtatEquipement.HorsService;
            signalement.MisHorsService = true;
            etatChange = true;
        }
        else if (leveHorsService && signalement.MisHorsService)
        {
            signalement.MisHorsService = false;
            if (equipement.Etat == EtatEquipement.HorsService && await revisions.ObtenirPassageEnCoursAsync(equipement.Id, ct) is null)
            {
                equipement.Etat = EtatEquipement.EnService;
                etatChange = true;
            }
        }

        if (existant is null)
        {
            await signalements.AjouterAsync(signalement, ct);
        }
        else
        {
            await signalements.MettreAJourAsync(signalement, ct);
        }

        if (etatChange)
        {
            await equipements.MettreAJourAsync(equipement, ct);
        }

        var details = $"{equipement.Code} : {cause}{(precision.Length > 0 ? $" ({precision})" : "")}, {Libelle(n.Gravite).ToLower(Libelles.Culture)}";
        await journal.AjouterAsync(new EntreeJournal
        {
            Horodatage = horloge.MaintenantUtc,
            Action = ActionsJournal.ProblemeSignale,
            Details = existant is null ? details : $"{details} (modifié)",
            EquipementId = equipement.Id,
            SortieId = sortieId,
        }, ct);

        var message = n.Gravite == GraviteSignalement.HorsService
            ? $"{equipement.Code} passe hors service : il ne pourra plus sortir avant réparation."
            : leveHorsService && etatChange
                ? $"{equipement.Code} est de nouveau en service, à surveiller."
                : $"Problème signalé sur {equipement.Code}. Le responsable matériel le verra dans Révisions.";
        return ResultatOperation.Ok(message, signalement.Id);
    }

    /// <summary>Catégorie de l'équipement (choix des causes), ou chaîne vide.</summary>
    public async Task<string> CategorieDeAsync(long equipementId, CancellationToken ct = default) =>
        await equipements.ObtenirAsync(equipementId, ct) is { } e
            ? (await categories.ListerAsync(ct)).FirstOrDefault(c => c.Id == e.CategorieId)?.Nom ?? ""
            : "";
}
