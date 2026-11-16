namespace Asprenaut.Materiel.Core.Modeles;

/// <summary>
/// Responsable d'une sortie : un moniteur, ou une classe (<see cref="Genre"/>). Les classes sont dans la même table
/// que les moniteurs : une sortie a toujours un seul responsable (sortie.moniteur_id). Pour une classe, le nom est
/// dans <see cref="Nom"/> (« Niveau 2 »), le prénom est vide et <see cref="Abrege"/> donne la pastille (« N2 »).
/// </summary>
public sealed class Moniteur
{
    public long Id { get; set; }
    public GenreResponsable Genre { get; set; } = GenreResponsable.Moniteur;
    public string Nom { get; set; } = "";
    public string Prenom { get; set; } = "";
    public string? Niveau { get; set; }
    public string Telephone { get; set; } = "";

    /// <summary>Numéro de licence d'un moniteur (vide = non renseigné ; toujours vide pour une classe).</summary>
    public string Licence { get; set; } = "";

    /// <summary>Abréviation d'une classe (N1, N2…), affichée dans sa pastille.</summary>
    public string Abrege { get; set; } = "";

    /// <summary>Couleur de la pastille d'une classe (clé de <see cref="CouleursClasse"/>), ou null : d'après son numéro.</summary>
    public string? Couleur { get; set; }

    /// <summary>Couleurs possibles de la pastille d'une classe (clés, traduites en couleurs par l'interface).</summary>
    public static IReadOnlyList<string> CouleursClasse { get; } =
        ["cyan", "turquoise", "vert", "lime", "jaune", "orange", "corail", "rouge", "rose", "violet", "indigo", "bleu", "sable", "gris"];

    /// <summary>Couleur choisie si elle est connue, sinon une couleur fixe d'après le numéro de la classe.</summary>
    public string CouleurEffective => Couleur is { } c && CouleursClasse.Contains(c) ? c : CouleursClasse[(int)(Math.Abs(Id) % CouleursClasse.Count)];

    /// <summary>Habilité à faire les TIV des blocs du club (jamais pour une classe).</summary>
    public bool InspecteurTiv { get; set; }

    public bool Actif { get; set; } = true;

    public bool EstClasse => Genre == GenreResponsable.Classe;

    public string NomComplet => $"{Prenom} {Nom}".Trim();

    public override string ToString() => NomComplet;
}
