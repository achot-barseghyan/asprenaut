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

    /// <summary>Abréviation d'une classe (N1, N2…), affichée dans sa pastille.</summary>
    public string Abrege { get; set; } = "";

    /// <summary>Habilité à faire les TIV des blocs du club (jamais pour une classe).</summary>
    public bool InspecteurTiv { get; set; }

    public bool Actif { get; set; } = true;

    public bool EstClasse => Genre == GenreResponsable.Classe;

    public string NomComplet => $"{Prenom} {Nom}".Trim();

    public override string ToString() => NomComplet;
}
