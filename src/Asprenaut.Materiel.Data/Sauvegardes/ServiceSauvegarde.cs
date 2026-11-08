using System.Globalization;
using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Data.Connexion;
using Asprenaut.Materiel.Data.Migrations;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Asprenaut.Materiel.Data.Sauvegardes;

/// <summary>
/// Copie la base avec l'API de backup SQLite (cohérente même en mode WAL) dans
/// <c>Sauvegardes\materiel_AAAA-MM-JJ_HHMM.db</c>, puis ne garde que les plus récentes.
/// Restaure une copie de la même façon, dans l'autre sens, après avoir sauvegardé les données actuelles.
/// </summary>
public sealed class ServiceSauvegarde(
    FabriqueConnexion fabrique,
    string dossierSauvegardes,
    Func<DateTime> maintenantLocal,
    int nombreAConserver = 20) : IServiceSauvegarde
{
    public const string Motif = "materiel_*.db";
    private const string FormatNom = "yyyy-MM-dd_HHmm";

    public string Dossier { get; private set; } = Path.GetFullPath(dossierSauvegardes);

    public int NombreAConserver => nombreAConserver;

    /// <summary>Dossier par défaut : "Sauvegardes" à côté de la base.</summary>
    public static string DossierParDefaut(string cheminBase) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(cheminBase)) ?? ".", "Sauvegardes");

    public Task<string?> SauvegarderAsync(CancellationToken ct = default) => SauvegarderAsync("", nettoyer: true, ct);

    public DateTime? DerniereSauvegarde()
    {
        if (!Directory.Exists(Dossier))
        {
            return null;
        }

        return Directory.GetFiles(Dossier, Motif)
            .Select(f => DateDuNom(Path.GetFileName(f)) ?? File.GetLastWriteTime(f))
            .DefaultIfEmpty()
            .Max() is { } d && d != default ? d : null;
    }

    public ResultatOperation ChangerDossier(string dossier)
    {
        if (string.IsNullOrWhiteSpace(dossier))
        {
            return ResultatOperation.Echec("Choisissez un dossier.");
        }

        try
        {
            var complet = Path.GetFullPath(dossier.Trim());
            Directory.CreateDirectory(complet);

            // Vérifie qu'on peut vraiment écrire (clé USB protégée, dossier en lecture seule...).
            var essai = Path.Combine(complet, $".essai_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(essai, "");
            File.Delete(essai);
            Dossier = complet;
            return ResultatOperation.Ok("Dossier des sauvegardes enregistré.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return ResultatOperation.Echec("Impossible d'écrire dans ce dossier. Choisissez-en un autre.");
        }
    }

    public async Task<ResultatOperation> RestaurerAsync(string fichier, CancellationToken ct = default)
    {
        if (!File.Exists(fichier))
        {
            return ResultatOperation.Echec("Fichier introuvable.");
        }

        if (string.Equals(Path.GetFullPath(fichier), fabrique.CheminBase, StringComparison.OrdinalIgnoreCase))
        {
            return ResultatOperation.Echec("Ce fichier est la base actuelle, pas une sauvegarde.");
        }

        var chaineSource = new SqliteConnectionStringBuilder { DataSource = fichier, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        try
        {
            await using var verification = new SqliteConnection(chaineSource);
            await verification.OpenAsync(ct);
            var tables = await verification.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('schema_version', 'sortie', 'equipement');");
            if (tables != 3)
            {
                return ResultatOperation.Echec("Ce fichier n'est pas une sauvegarde Asprenaut.");
            }

            var version = await verification.ExecuteScalarAsync<int?>("SELECT MAX(version) FROM schema_version;") ?? 0;
            if (version > GestionnaireMigrations.VersionCible)
            {
                return ResultatOperation.Echec("Cette sauvegarde vient d'une version plus récente de l'application.");
            }
        }
        catch (SqliteException)
        {
            return ResultatOperation.Echec("Ce fichier n'est pas une sauvegarde Asprenaut.");
        }

        // Filet de sécurité : les données actuelles sont copiées avant d'être remplacées.
        // Pas de rotation ici : le fichier à restaurer pourrait être parmi les plus anciens.
        var securite = await SauvegarderAsync("_avant-restauration", nettoyer: false, ct);

        await using (var source = new SqliteConnection(chaineSource))
        await using (var cible = await fabrique.OuvrirAsync(ct))
        {
            await source.OpenAsync(ct);
            source.BackupDatabase(cible);
        }

        fabrique.LibererPool();

        // Une copie faite par une version plus ancienne est mise à jour, comme au démarrage.
        await new GestionnaireMigrations(fabrique).AppliquerAsync(ct);
        Nettoyer();

        return ResultatOperation.Ok(securite is null
            ? "Sauvegarde restaurée."
            : $"Sauvegarde restaurée. Les données d'avant sont gardées dans {Path.GetFileName(securite)}.");
    }

    private async Task<string?> SauvegarderAsync(string suffixe, bool nettoyer, CancellationToken ct)
    {
        if (!File.Exists(fabrique.CheminBase))
        {
            return null;
        }

        Directory.CreateDirectory(Dossier);
        var nom = $"materiel_{maintenantLocal().ToString(FormatNom, CultureInfo.InvariantCulture)}{suffixe}.db";
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

        if (nettoyer)
        {
            Nettoyer();
        }

        return destination;
    }

    /// <summary>"materiel_2026-11-08_1902.db" (ou avec suffixe) → 08/11/2026 19:02.</summary>
    private static DateTime? DateDuNom(string nom) =>
        nom.Length >= 9 + FormatNom.Length
        && DateTime.TryParseExact(nom.AsSpan(9, FormatNom.Length), FormatNom, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;

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
