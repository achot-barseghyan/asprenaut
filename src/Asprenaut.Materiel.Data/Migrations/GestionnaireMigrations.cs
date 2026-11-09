using System.Reflection;
using System.Text.RegularExpressions;
using Asprenaut.Materiel.Data.Connexion;
using Dapper;

namespace Asprenaut.Materiel.Data.Migrations;

/// <summary>
/// Applique au démarrage les scripts Migrations/NNN_nom.sql (ressources embarquées)
/// dont le numéro dépasse la version enregistrée dans schema_version.
/// Chaque script s'exécute dans sa propre transaction.
/// </summary>
public sealed partial class GestionnaireMigrations(FabriqueConnexion fabrique)
{
    [GeneratedRegex(@"^Migrations\.(\d{3})_[\w-]+\.sql$")]
    private static partial Regex NomScript();

    /// <summary>Scripts disponibles, triés par version.</summary>
    public static IReadOnlyList<(int Version, string Nom)> ScriptsDisponibles() =>
        typeof(GestionnaireMigrations).Assembly.GetManifestResourceNames()
            .Select(n => (Match: NomScript().Match(n), Nom: n))
            .Where(x => x.Match.Success)
            .Select(x => (int.Parse(x.Match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), x.Nom))
            .OrderBy(x => x.Item1)
            .ToList();

    public static int VersionCible => ScriptsDisponibles().Select(s => s.Version).DefaultIfEmpty(0).Max();

    /// <summary>Met la base à jour. Retourne la version finale.</summary>
    public Task<int> AppliquerAsync(CancellationToken ct = default) => AppliquerAsync(int.MaxValue, ct);

    /// <summary>Met la base à jour sans dépasser la version <paramref name="jusqua"/> (tests des reprises de données).</summary>
    public async Task<int> AppliquerAsync(int jusqua, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        await cx.ExecuteScalarAsync<string>("PRAGMA journal_mode = WAL;");
        await cx.ExecuteAsync("CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL PRIMARY KEY);");

        var actuelle = await cx.ExecuteScalarAsync<int?>("SELECT MAX(version) FROM schema_version;") ?? 0;

        foreach (var (version, nom) in ScriptsDisponibles().Where(s => s.Version > actuelle && s.Version <= jusqua))
        {
            var sql = LireScript(nom);
            await using var tx = await cx.BeginTransactionAsync(ct);
            await cx.ExecuteAsync(sql, transaction: tx);
            await cx.ExecuteAsync("INSERT INTO schema_version (version) VALUES (@version);", new { version }, tx);
            await tx.CommitAsync(ct);
            actuelle = version;
        }

        return actuelle;
    }

    private static string LireScript(string nom)
    {
        using var flux = Assembly.GetExecutingAssembly().GetManifestResourceStream(nom)
            ?? throw new InvalidOperationException($"Script de migration introuvable : {nom}");
        using var lecteur = new StreamReader(flux);
        return lecteur.ReadToEnd();
    }
}
