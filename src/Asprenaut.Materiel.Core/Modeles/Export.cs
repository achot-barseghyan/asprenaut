namespace Asprenaut.Materiel.Core.Modeles;

// Export de toutes les données en CSV (Paramètres › Données et sauvegardes) : un fichier par table.

/// <summary>Comment présenter une colonne brute de la base dans le CSV.</summary>
public enum GenreColonne
{
    Texte,

    /// <summary>Date seule « 2026-11-06 » → « 06/11/2026 ».</summary>
    Date,

    /// <summary>Horodatage UTC ISO → date et heure locales « 06/11/2026 19:30 ».</summary>
    Horodatage,

    /// <summary>1 / 0 → Oui / Non.</summary>
    OuiNon,

    EtatEquipement,
    TypeSortie,
    StatutSortie,
    StatutSortieEquipement,
    TypeControle,
    ResultatControle,

    /// <summary>Etrier / Din → Étrier / DIN.</summary>
    Montage,

    /// <summary>ASurveiller / HorsService → À surveiller / Hors service.</summary>
    GraviteSignalement,
}

public sealed record ColonneExport(string Nom, GenreColonne Genre = GenreColonne.Texte);

/// <summary>Une table lue pour l'export : valeurs brutes de la base, mises en forme par le service.</summary>
public sealed record TableExport(string Fichier, IReadOnlyList<ColonneExport> Colonnes, IReadOnlyList<string?[]> Lignes);

/// <summary>Un fichier prêt à écrire (UTF-8 avec BOM).</summary>
public sealed record FichierExport(string Nom, string Contenu, int Lignes);
