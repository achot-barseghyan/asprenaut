namespace Asprenaut.Materiel.Core.Modeles;

/// <summary>Moniteur responsable d'une sortie.</summary>
public sealed class Moniteur
{
    public long Id { get; set; }
    public string Nom { get; set; } = "";
    public string Prenom { get; set; } = "";
    public string? Niveau { get; set; }
    public bool Actif { get; set; } = true;

    public string NomComplet => $"{Prenom} {Nom}".Trim();

    public override string ToString() => NomComplet;
}
