namespace Asprenaut.Materiel.Core.Modeles;

/// <summary>Un équipement du club, identifié par son code (étiquette QR).</summary>
public sealed class Equipement
{
    public long Id { get; set; }

    /// <summary>Code unique, normalisé en majuscules (ex. BLOC-031).</summary>
    public string Code { get; set; } = "";

    public string Libelle { get; set; } = "";
    public long CategorieId { get; set; }
    public string? Taille { get; set; }
    public EtatEquipement Etat { get; set; } = EtatEquipement.EnService;
    /// <summary>Prochaine TIV (blocs) ou prochaine révision (autres équipements).</summary>
    public DateOnly? DateProchaineRevision { get; set; }

    /// <summary>Prochaine requalification (blocs seulement).</summary>
    public DateOnly? DateRequalification { get; set; }

    public string Notes { get; set; } = "";
    public DateTime CreeLe { get; set; }

    // --- Fiche matériel (migration 005) : marque, modèle, numéro de série ; pour un détendeur, ceux du premier étage ---

    public string Marque { get; set; } = "";
    public string Modele { get; set; } = "";
    public string NumeroSerie { get; set; } = "";

    // --- Détendeurs seulement ---

    public MontageDetendeur? Montage { get; set; }
    public string MarqueDeuxiemeEtage { get; set; } = "";
    public string ModeleDeuxiemeEtage { get; set; } = "";
    public string NumeroDeuxiemeEtage { get; set; } = "";

    /// <summary>Colonne « D.S » du registre du club (oui / non) ; null = non renseigné.</summary>
    public bool? DirectSystem { get; set; }

    /// <summary>Marque du manomètre ; vide = sans manomètre.</summary>
    public string Manometre { get; set; } = "";

    /// <summary>Modèle de l'octopus ; vide = sans octopus.</summary>
    public string Octopus { get; set; } = "";

    public string NumeroOctopus { get; set; } = "";

    /// <summary>Marque de l'octopus (migration 033).</summary>
    public string MarqueOctopus { get; set; } = "";

    /// <summary>Modèle et n° de série du manomètre (migration 033) ; sa marque est <see cref="Manometre"/>.</summary>
    public string ModeleManometre { get; set; } = "";

    public string NumeroManometre { get; set; } = "";

    /// <summary>Bloc ou détendeur nitrox : lors d'une sortie nominative, seul un adhérent certifié nitrox peut le prendre.</summary>
    public bool Nitrox { get; set; }

    /// <summary>
    /// Types de sortie interdits (migration 034) : ids de type_sortie séparés par « ; », vide = toutes les sorties.
    /// Lire et écrire par <see cref="TypesInterdits"/> / <see cref="EcrireTypesInterdits"/>.
    /// </summary>
    public string TypesSortieInterdits { get; set; } = "";

    /// <summary>Types de sortie interdits (ids, sans doublon, triés).</summary>
    public IReadOnlyList<long> TypesInterdits => LireTypesInterdits(TypesSortieInterdits);

    /// <summary>Faux si la fiche interdit ce type de sortie ; une sortie sans type (avant la migration 004) est autorisée.</summary>
    public bool AutoriseEn(long? typeSortieId) => AutoriseEn(TypesSortieInterdits, typeSortieId);

    public static bool AutoriseEn(string? typesInterdits, long? typeSortieId) =>
        typeSortieId is not { } id || !LireTypesInterdits(typesInterdits).Contains(id);

    /// <summary>« 3;5 » → [3, 5] ; morceaux illisibles ignorés.</summary>
    public static IReadOnlyList<long> LireTypesInterdits(string? texte) =>
        (texte ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(m => long.TryParse(m, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) ? id : 0)
            .Where(id => id > 0).Distinct().Order().ToList();

    /// <summary>[5, 3, 3] → « 3;5 ».</summary>
    public static string EcrireTypesInterdits(IEnumerable<long> ids) =>
        string.Join(";", ids.Where(id => id > 0).Distinct().Order().Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>Échéance la plus proche : révision/TIV ou requalification.</summary>
    public DateOnly? ProchaineEcheance =>
        DateProchaineRevision is { } r && DateRequalification is { } q ? (r < q ? r : q) : DateProchaineRevision ?? DateRequalification;

    /// <summary>Échéance dépassée la plus ancienne (révision/TIV ou requalification), ou null.</summary>
    public DateOnly? EcheanceDepassee(DateOnly aujourdHui) =>
        new[] { DateProchaineRevision, DateRequalification }.Where(d => d < aujourdHui).Min();

    /// <summary>Vrai si la révision (ou TIV) ou la requalification est dépassée.</summary>
    public bool RevisionDepassee(DateOnly aujourdHui) =>
        ProchaineEcheance is { } date && date < aujourdHui;

    /// <summary>Texte court affiché à l'utilisateur : "BLOC-031 · Bloc 12 L".</summary>
    public string Designation => string.IsNullOrWhiteSpace(Libelle) ? Code : $"{Code} · {Libelle}";
}
