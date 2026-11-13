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
    ServiceGestion? gestion = null,
    Reglages? reglages = null)
{
    /// <summary>Tuiles de la fenêtre pour cette catégorie (réglées dans Paramètres, sinon d'origine).</summary>
    public IReadOnlyList<CauseSignalement> Tuiles(string? categorie) => TuilesPour(categorie, reglages);

    public const int LongueurMaxCause = 60;
    public const int LongueurMaxPrecision = 200;
    public const string CauseAutre = "Autre";

    /// <summary>Nombre de tuiles d'une catégorie : de 1 à 12 (quatre rangées de trois dans la fenêtre).</summary>
    public const int TuilesMax = 12;

    /// <summary>Libellés des tuiles proposées pour cette catégorie (réglées dans Paramètres, sinon celles d'origine).</summary>
    public static IReadOnlyList<string> CausesPour(string? categorie, Reglages? reglages = null) =>
        TuilesPour(categorie, reglages).Select(c => c.Libelle).ToList();

    /// <summary>
    /// Tuiles de la fenêtre « Problème sur A07 » pour cette catégorie : celles réglées dans Paramètres › Problèmes signalés
    /// (nom de catégorie, sans tenir compte des majuscules), sinon celles d'origine (<see cref="CausesParDefaut"/>). Copies.
    /// </summary>
    public static IReadOnlyList<CauseSignalement> TuilesPour(string? categorie, Reglages? reglages)
    {
        var nom = (categorie ?? "").Trim();
        var reglees = reglages?.CausesSignalement
            .FirstOrDefault(p => string.Equals(p.Key.Trim(), nom, StringComparison.OrdinalIgnoreCase)).Value;
        return (reglees is { Count: > 0 } ? reglees : CausesParDefaut(categorie)).Select(c => c.Copie()).ToList();
    }

    /// <summary>Tuiles réglées pour cette catégorie (plutôt que celles d'origine).</summary>
    public static bool TuilesPersonnalisees(string? categorie, Reglages reglages) =>
        reglages.CausesSignalement.Any(p => string.Equals(p.Key.Trim(), (categorie ?? "").Trim(), StringComparison.OrdinalIgnoreCase) && p.Value.Count > 0);

    /// <summary>
    /// Tuiles d'origine selon la catégorie (maquette « Retour du matériel »), « Autre » en dernier, précision facultative.
    /// Catégorie inconnue : choix génériques. Pictogrammes : clés de <c>Icones.PictosCause</c>.
    /// </summary>
    public static IReadOnlyList<CauseSignalement> CausesParDefaut(string? categorie)
    {
        var c = (categorie ?? "").Trim();
        bool Est(params string[] debuts) => debuts.Any(d => c.StartsWith(d, StringComparison.OrdinalIgnoreCase));
        (string Libelle, string Picto)[] tuiles =
            Est("Détendeur", "Detendeur") ? [("Fuite / bulles", "Goutte"), ("Débit continu", "Debit"), ("Respiration dure", "Respiration"), ("Embout abîmé", "Embout"), ("Flexible abîmé", "Flexible")]
            : Est("Gilet", "Stab") ? [("Ne tient pas l'air", "Goutte"), ("Inflateur qui colle", "Boucle"), ("Purge qui reste bloquée", "Bloque"), ("Sangle ou boucle cassée", "Casse"), ("Poche à plombs abîmée", "Dechire")]
            : Est("Bloc") ? [("Robinet qui fuit", "Robinet"), ("Robinet dur", "Bloque"), ("Choc / bosse", "Choc"), ("Corrosion visible", "Corrosion"), ("Joint torique abîmé", "Goutte")]
            : Est("Masque") ? [("Verre fêlé", "Verre"), ("Jupe déchirée", "Dechire"), ("Sangle cassée", "Casse"), ("Boucle cassée", "Boucle"), ("Buée permanente", "Buee")]
            : Est("Palme") ? [("Sangle ou talon cassé", "Casse"), ("Voilure fendue", "Palmes"), ("Boucle cassée", "Boucle")]
            : Est("Combinaison") ? [("Déchirure", "Dechire"), ("Fermeture bloquée", "Fermeture"), ("Coutures ouvertes", "Casse")]
            : Est("Ordinateur") ? [("Ne s'allume plus", "Ecran"), ("Pile faible", "Pile"), ("Bracelet cassé", "Casse")]
            : [("Cassé", "Casse"), ("Abîmé", "Dechire"), ("Ne fonctionne plus", "Ecran")];
        return [.. tuiles.Select(t => new CauseSignalement { Libelle = t.Libelle, Pictogramme = t.Picto }), new CauseSignalement { Libelle = CauseAutre, Pictogramme = "Autre" }];
    }

    /// <summary>
    /// Vérifie une liste de tuiles avant de l'enregistrer : 1 à <see cref="TuilesMax"/>, libellé obligatoire
    /// (<see cref="LongueurMaxCause"/> car.), sans doublon (majuscules ignorées). Message d'erreur, ou null.
    /// </summary>
    public static string? VerifierTuiles(IReadOnlyList<CauseSignalement> tuiles)
    {
        if (tuiles.Count == 0)
        {
            return "Gardez au moins une tuile.";
        }

        if (tuiles.Count > TuilesMax)
        {
            return $"{TuilesMax} tuiles au plus.";
        }

        if (tuiles.Any(t => string.IsNullOrWhiteSpace(t.Libelle)))
        {
            return "Donnez un nom à chaque tuile.";
        }

        if (tuiles.FirstOrDefault(t => t.Libelle.Trim().Length > LongueurMaxCause) is { } longue)
        {
            return $"« {longue.Libelle.Trim()[..20]}… » : {LongueurMaxCause} caractères au plus.";
        }

        var doublon = tuiles.GroupBy(t => t.Libelle.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        return doublon is null ? null : $"« {doublon.Key} » existe déjà.";
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

        // Tuile qui demande une précision (réglage de Paramètres › Problèmes signalés).
        var categorie = (await categories.ListerAsync(ct)).FirstOrDefault(c => c.Id == equipement.CategorieId)?.Nom;
        if (precision.Length == 0 && Tuiles(categorie).Any(t => t.PrecisionObligatoire && string.Equals(t.Libelle.Trim(), cause, StringComparison.OrdinalIgnoreCase)))
        {
            return ResultatOperation.Echec($"Précisez le problème : « {cause} » demande une précision.");
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
