using Asprenaut.Materiel.Core.Abstractions;

namespace Asprenaut.Materiel.Data.Manuels;

/// <summary>
/// PDF des manuels rangés dans un dossier « Manuels » à côté de la base (comme « Sauvegardes ») :
/// la copie garde le nom d'origine ; un nom déjà pris reçoit « (2) », « (3) »...
/// </summary>
public sealed class StockageManuels(string dossier) : IStockageManuels
{
    public string Dossier { get; } = dossier;

    /// <summary>Dossier par défaut : « Manuels » à côté du fichier de base.</summary>
    public static string DossierParDefaut(string cheminBase) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(cheminBase)) ?? ".", "Manuels");

    public string Importer(string cheminSource)
    {
        Directory.CreateDirectory(Dossier);
        var nom = NomPropre(Path.GetFileNameWithoutExtension(cheminSource));
        var fichier = $"{nom}.pdf";
        for (var i = 2; File.Exists(Chemin(fichier)); i++)
        {
            fichier = $"{nom} ({i}).pdf";
        }

        File.Copy(cheminSource, Chemin(fichier));
        return fichier;
    }

    public string Chemin(string fichier) => Path.Combine(Dossier, Path.GetFileName(fichier));

    public bool Existe(string fichier) => !string.IsNullOrWhiteSpace(fichier) && File.Exists(Chemin(fichier));

    public void Supprimer(string fichier)
    {
        if (Existe(fichier))
        {
            File.Delete(Chemin(fichier));
        }
    }

    /// <summary>Nom sans caractère interdit par Windows ; « manuel » s'il ne reste rien.</summary>
    private static string NomPropre(string nom)
    {
        var interdits = Path.GetInvalidFileNameChars();
        var propre = new string(nom.Select(c => interdits.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        return propre.Length == 0 ? "manuel" : propre;
    }
}
