using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Data.Connexion;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Asprenaut.Materiel.Data.Sauvegardes;

/// <summary>
/// Sauvegarde complète dans un .zip : <c>asprenaut-sauvegarde.json</c> (description), <c>materiel.db</c> (copie par l'API de
/// backup SQLite), <c>reglages.json</c> et <c>Manuels\*.pdf</c>. La restauration vérifie tout le fichier avant de toucher à
/// quoi que ce soit, puis remet la base par <see cref="IServiceSauvegarde.RestaurerAsync"/> (copie de sécurité, migrations),
/// les réglages et les manuels.
/// </summary>
public sealed class SauvegardeComplete(
    FabriqueConnexion fabrique,
    IServiceSauvegarde sauvegarde,
    IReglagesSauvegardables reglages,
    string dossierManuels,
    Func<DateTime> maintenantLocal) : ISauvegardeComplete
{
    public const string EntreeDescription = "asprenaut-sauvegarde.json";
    public const string EntreeBase = "materiel.db";
    public const string EntreeReglages = "reglages.json";
    public const string DossierManuelsZip = "Manuels/";
    private const int Format = 1;

    /// <summary>Description écrite dans le fichier : format, date, version de la base, nombre de manuels.</summary>
    public sealed record Description(int Format, string Creee, int VersionBase, int Manuels);

    public string NomPropose() =>
        $"Asprenaut sauvegarde complète {maintenantLocal().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}{ISauvegardeComplete.Extension}";

    public async Task<ResultatOperation> CreerAsync(string fichierZip, CancellationToken ct = default)
    {
        if (!File.Exists(fabrique.CheminBase))
        {
            return ResultatOperation.Echec("Rien à sauvegarder : la base n'existe pas encore.");
        }

        var temporaire = Path.Combine(Path.GetTempPath(), $"asprenaut-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaire);
        try
        {
            // Copie cohérente de la base (mode WAL), puis le .zip à côté, déplacé à la fin : jamais de fichier à moitié écrit.
            var copieBase = Path.Combine(temporaire, EntreeBase);
            int version;
            await using (var source = await fabrique.OuvrirAsync(ct))
            await using (var cible = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copieBase, Pooling = false }.ToString()))
            {
                await cible.OpenAsync(ct);
                source.BackupDatabase(cible);
                version = await source.ExecuteScalarAsync<int?>("SELECT MAX(version) FROM schema_version;") ?? 0;
            }

            var manuels = Directory.Exists(dossierManuels) ? Directory.GetFiles(dossierManuels) : [];
            var zip = Path.Combine(temporaire, "sauvegarde.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                var description = new Description(Format, maintenantLocal().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), version, manuels.Length);
                EcrireTexte(archive, EntreeDescription, JsonSerializer.Serialize(description));
                archive.CreateEntryFromFile(copieBase, EntreeBase);
                EcrireTexte(archive, EntreeReglages, reglages.Lire());
                foreach (var manuel in manuels)
                {
                    archive.CreateEntryFromFile(manuel, DossierManuelsZip + Path.GetFileName(manuel), CompressionLevel.Fastest);
                }
            }

            File.Copy(zip, fichierZip, overwrite: true);
            return ResultatOperation.Ok($"Sauvegarde complète faite : {Path.GetFileName(fichierZip)} (base, réglages et {Libelle(manuels.Length)}).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ResultatOperation.Echec("Impossible d'écrire la sauvegarde ici. Choisissez un autre endroit (Documents, clé USB…).");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Supprimer(temporaire);
        }
    }

    public async Task<ResultatOperation> RestaurerAsync(string fichierZip, CancellationToken ct = default)
    {
        if (!File.Exists(fichierZip))
        {
            return ResultatOperation.Echec("Fichier introuvable.");
        }

        var temporaire = Path.Combine(Path.GetTempPath(), $"asprenaut-{Guid.NewGuid():N}");
        try
        {
            // 1. Tout vérifier d'abord : rien n'est changé si le fichier est incomplet.
            string json;
            List<ZipArchiveEntry> manuels;
            using var archive = ZipFile.OpenRead(fichierZip);
            var description = Lire<Description>(archive.GetEntry(EntreeDescription));
            var entreeBase = archive.GetEntry(EntreeBase);
            var entreeReglages = archive.GetEntry(EntreeReglages);
            if (description is not { Format: Format } || entreeBase is null || entreeReglages is null)
            {
                return ResultatOperation.Echec("Ce fichier n'est pas une sauvegarde complète Asprenaut.");
            }

            using (var lecteur = new StreamReader(entreeReglages.Open(), Encoding.UTF8))
            {
                json = await lecteur.ReadToEndAsync(ct);
            }

            if (!reglages.EstLisible(json))
            {
                return ResultatOperation.Echec("Les réglages de cette sauvegarde sont illisibles : rien n'a été changé.");
            }

            manuels = archive.Entries
                .Where(e => e.FullName.StartsWith(DossierManuelsZip, StringComparison.Ordinal) && Path.GetFileName(e.FullName).Length > 0)
                .ToList();

            // 2. La base : vérifiée et remise par la restauration simple (copie de sécurité des données actuelles, migrations).
            Directory.CreateDirectory(temporaire);
            var copieBase = Path.Combine(temporaire, EntreeBase);
            entreeBase.ExtractToFile(copieBase);
            var resultat = await sauvegarde.RestaurerAsync(copieBase, ct);
            if (!resultat.Reussi)
            {
                return resultat;
            }

            // 3. Les réglages, puis les manuels (le nom seul : jamais hors du dossier des manuels).
            reglages.Remplacer(json);
            Directory.CreateDirectory(dossierManuels);
            foreach (var manuel in manuels)
            {
                manuel.ExtractToFile(Path.Combine(dossierManuels, Path.GetFileName(manuel.FullName)), overwrite: true);
            }

            return ResultatOperation.Ok($"Sauvegarde complète du {description.Creee} restaurée : base, réglages et {Libelle(manuels.Count)}. " +
                "Les données et les réglages d'avant sont gardés dans le dossier des sauvegardes.", manuels.Count);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            return ResultatOperation.Echec("Ce fichier n'est pas une sauvegarde complète Asprenaut.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ResultatOperation.Echec($"Lecture ou écriture impossible : {ex.Message}");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Supprimer(temporaire);
        }
    }

    private static string Libelle(int manuels) => manuels == 1 ? "1 manuel" : $"{manuels} manuels";

    private static void EcrireTexte(ZipArchive archive, string nom, string texte)
    {
        using var flux = new StreamWriter(archive.CreateEntry(nom).Open(), new UTF8Encoding(false));
        flux.Write(texte);
    }

    private static T? Lire<T>(ZipArchiveEntry? entree)
    {
        if (entree is null)
        {
            return default;
        }

        using var flux = entree.Open();
        return JsonSerializer.Deserialize<T>(flux);
    }

    private static void Supprimer(string dossier)
    {
        try
        {
            if (Directory.Exists(dossier))
            {
                Directory.Delete(dossier, recursive: true);
            }
        }
        catch (IOException)
        {
            // Dossier temporaire : sans importance s'il reste.
        }
    }
}
