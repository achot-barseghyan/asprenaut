namespace Asprenaut.Materiel.Core.Modeles;

/// <summary>Catégorie de matériel (Blocs, Gilets, Détendeurs...).</summary>
public sealed class Categorie
{
    /// <summary>Longueur maximale d'un nom de catégorie.</summary>
    public const int LongueurMaxNom = 30;

    public long Id { get; set; }
    public string Nom { get; set; } = "";

    public override string ToString() => Nom;
}
