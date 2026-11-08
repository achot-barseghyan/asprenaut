using System.Globalization;
using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Data.Connexion;
using Microsoft.Data.Sqlite;

namespace Asprenaut.Materiel.Data.Sauvegardes;

/// <summary>
/// Copie la base avec l'API de backup SQLite (cohérente même en mode WAL) dans
/// <c>Sauvegardes\materiel_AAAA-MM-JJ_HHMM.db</c>, puis ne garde que les plus récentes.
/// </summary>
public sealed class ServiceSauvegarde(
    FabriqueConnexion fabrique,
    string dossierSauvegardes,
    Func<DateTime> maintenantLocal,
    int nombreAConserver = 20) : IServiceSauvegarde
{
    public const string Motif = "materiel_*.db";

    public string Dossier { get; } = Path.GetFullPath(dossierSauvegardes);

    /// <summary>Dossier par défaut : "Sauvegardes" à côté de la base.</summary>
    public static string DossierParDefaut(string cheminBase) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(cheminBase)) ?? ".", "Sauvegardes");

    public async Task<string?> SauvegarderAsync(CancellationToken ct = default)
    {
        if (!File.Exists(fabrique.CheminBase))
        {
            return null;
        }

        Directory.CreateDirectory(Dossier);
        var nom = $"materiel_{maintenantLocal().ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture)}.db";
        var destination = Path.Combine(Dossier, nom);
        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        await using (var source = await fabrique.OuvrirAsync(ct))
        await using (var cible = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = destination,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString()))
        {
            await cible.OpenAsync(ct);
            source.BackupDatabase(cible);
        }

        Nettoyer();
        return destination;
    }

    /// <summary>Supprime les sauvegardes au-delà des <c>nombreAConserver</c> plus récentes (le nom est triable).</summary>
    private void Nettoyer()
    {
        var anciennes = Directory.GetFiles(Dossier, Motif)
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(nombreAConserver);
        foreach (var fichier in anciennes)
        {
            File.Delete(fichier);
        }
    }
}
