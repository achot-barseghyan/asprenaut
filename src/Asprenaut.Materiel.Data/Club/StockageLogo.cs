using Asprenaut.Materiel.Core.Abstractions;

namespace Asprenaut.Materiel.Data.Club;

/// <summary>Logo du club rangé dans un dossier « Club » à côté de la base (comme « Manuels »), sous le nom « logo.png ».</summary>
public sealed class StockageLogo(string dossier) : IStockageLogo
{
    public const string NomFichier = "logo.png";

    public string Dossier { get; } = dossier;

    /// <summary>Dossier par défaut : « Club » à côté du fichier de base.</summary>
    public static string DossierParDefaut(string cheminBase) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(cheminBase)) ?? ".", "Club");

    private string Fichier => Path.Combine(Dossier, NomFichier);

    public string? Chemin() => File.Exists(Fichier) ? Fichier : null;

    public void Enregistrer(byte[] png)
    {
        Directory.CreateDirectory(Dossier);
        var temporaire = Fichier + ".tmp";
        File.WriteAllBytes(temporaire, png);
        File.Move(temporaire, Fichier, overwrite: true);
    }

    public void Supprimer()
    {
        if (File.Exists(Fichier))
        {
            File.Delete(Fichier);
        }
    }
}
