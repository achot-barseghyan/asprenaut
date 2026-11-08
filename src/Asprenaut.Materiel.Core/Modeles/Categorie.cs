namespace Asprenaut.Materiel.Core.Modeles;

/// <summary>Catégorie de matériel (Blocs, Gilets, Détendeurs...).</summary>
public sealed class Categorie
{
    public long Id { get; set; }
    public string Nom { get; set; } = "";

    public override string ToString() => Nom;
}
