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
    public DateOnly? DateProchaineRevision { get; set; }
    public string Notes { get; set; } = "";
    public DateTime CreeLe { get; set; }

    /// <summary>Vrai si la date de prochaine révision est passée.</summary>
    public bool RevisionDepassee(DateOnly aujourdHui) =>
        DateProchaineRevision is { } date && date < aujourdHui;

    /// <summary>Texte court affiché à l'utilisateur : "BLOC-031 · Bloc 12 L".</summary>
    public string Designation => string.IsNullOrWhiteSpace(Libelle) ? Code : $"{Code} · {Libelle}";
}
