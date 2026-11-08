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
