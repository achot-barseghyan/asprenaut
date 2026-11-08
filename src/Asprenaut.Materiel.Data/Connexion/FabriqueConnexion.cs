using Microsoft.Data.Sqlite;

namespace Asprenaut.Materiel.Data.Connexion;

/// <summary>
/// Ouvre les connexions SQLite. Chaque connexion active les clés étrangères
/// (PRAGMA foreign_keys = ON via la chaîne de connexion). Le mode WAL est
/// persistant et activé par <see cref="Migrations.GestionnaireMigrations"/>.
/// </summary>
public sealed class FabriqueConnexion
{
    private readonly string _chaine;

    public FabriqueConnexion(string cheminBase)
    {
        ConfigurationDapper.Initialiser();
        CheminBase = Path.GetFullPath(cheminBase);
        var dossier = Path.GetDirectoryName(CheminBase);
        if (!string.IsNullOrEmpty(dossier))
        {
            Directory.CreateDirectory(dossier);
        }

        _chaine = new SqliteConnectionStringBuilder
        {
            DataSource = CheminBase,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            DefaultTimeout = 10,
        }.ToString();
    }

    public string CheminBase { get; }

    public async Task<SqliteConnection> OuvrirAsync(CancellationToken ct = default)
    {
        var connexion = new SqliteConnection(_chaine);
        await connexion.OpenAsync(ct);
        return connexion;
    }

    /// <summary>Libère les connexions en cache (avant une copie ou une suppression du fichier).</summary>
    public void LibererPool()
    {
        using var connexion = new SqliteConnection(_chaine);
        SqliteConnection.ClearPool(connexion);
    }
}
