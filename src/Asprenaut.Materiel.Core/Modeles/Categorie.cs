namespace Asprenaut.Materiel.Core.Modeles;

/// <summary>
/// Catégorie de matériel (Blocs, Gilets, Détendeurs...), avec son pictogramme et sa couleur (migration 020 ; vides = choisis
/// d'après le nom, voir <see cref="IconeEffective"/> et <see cref="CouleurEffective"/>). Les clés sont traduites en tracés et en
/// couleurs du thème par l'application.
/// </summary>
public sealed class Categorie
{
    /// <summary>Longueur maximale d'un nom de catégorie.</summary>
    public const int LongueurMaxNom = 30;

    /// <summary>Tous les pictogrammes possibles d'une catégorie (clés).</summary>
    public static IReadOnlyList<string> Icones { get; } =
        ["bloc", "gilet", "detendeur", "combinaison", "palme", "masque", "ordinateur", "lampe", "plomb", "sac", "tuba", "autre"];

    /// <summary>Pictogrammes proposés pour une nouvelle catégorie (les catégories d'origine ont déjà les leurs).</summary>
    public static IReadOnlyList<string> IconesNouvelle { get; } = ["lampe", "plomb", "sac", "tuba", "palme", "masque", "ordinateur", "autre"];

    /// <summary>Couleurs possibles (clés), traduites en couleurs du thème.</summary>
    public static IReadOnlyList<string> Couleurs { get; } = ["cyan", "vert", "jaune", "corail", "violet", "bleu"];

    public long Id { get; set; }
    public string Nom { get; set; } = "";

    /// <summary>Clé du pictogramme (<see cref="Icones"/>), ou null : d'après le nom.</summary>
    public string? Icone { get; set; }

    /// <summary>Clé de la couleur (<see cref="Couleurs"/>), ou null : d'après le nom.</summary>
    public string? Couleur { get; set; }

    public string IconeEffective => Icone is { } i && Icones.Contains(i) ? i : IconeParDefaut(Nom);

    public string CouleurEffective => Couleur is { } c && Couleurs.Contains(c) ? c : CouleurParDefaut(Nom);

    /// <summary>Pictogramme d'après le nom : « Blocs » → bloc, « Stabs » → gilet… ; inconnu → autre.</summary>
    public static string IconeParDefaut(string? nom) => Cle(nom) switch
    {
        var n when n.StartsWith("BLOC", StringComparison.Ordinal) => "bloc",
        var n when n.StartsWith("GILET", StringComparison.Ordinal) || n.StartsWith("STAB", StringComparison.Ordinal) => "gilet",
        var n when n.StartsWith("DETENDEUR", StringComparison.Ordinal) => "detendeur",
        var n when n.StartsWith("COMBI", StringComparison.Ordinal) => "combinaison",
        var n when n.StartsWith("PALME", StringComparison.Ordinal) => "palme",
        var n when n.StartsWith("MASQUE", StringComparison.Ordinal) => "masque",
        var n when n.StartsWith("ORDI", StringComparison.Ordinal) => "ordinateur",
        var n when n.StartsWith("LAMPE", StringComparison.Ordinal) || n.StartsWith("PHARE", StringComparison.Ordinal) => "lampe",
        var n when n.StartsWith("PLOMB", StringComparison.Ordinal) || n.StartsWith("LEST", StringComparison.Ordinal) => "plomb",
        var n when n.StartsWith("SAC", StringComparison.Ordinal) => "sac",
        var n when n.StartsWith("TUBA", StringComparison.Ordinal) => "tuba",
        _ => "autre",
    };

    /// <summary>Couleur d'après le nom (celles de la maquette) ; inconnu → cyan.</summary>
    public static string CouleurParDefaut(string? nom) => IconeParDefaut(nom) switch
    {
        "gilet" => "vert",
        "detendeur" => "bleu",
        "combinaison" => "violet",
        "palme" => "corail",
        "masque" or "lampe" => "jaune",
        _ => "cyan",
    };

    private static string Cle(string? nom) =>
        new string((nom ?? "").Trim().ToUpperInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());

    public override string ToString() => Nom;
}

/// <summary>Une catégorie et ce qui l'utilise : elle ne se supprime que si rien ne l'utilise.</summary>
public sealed record CategorieUsage(Categorie Categorie, int NbEquipements, int NbManuels)
{
    public bool Supprimable => NbEquipements == 0 && NbManuels == 0;
}
