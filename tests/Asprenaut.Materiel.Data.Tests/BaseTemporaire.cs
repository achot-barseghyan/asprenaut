using Asprenaut.Materiel.Data.Connexion;
using Asprenaut.Materiel.Data.Migrations;
using Microsoft.Data.Sqlite;

namespace Asprenaut.Materiel.Data.Tests;

/// <summary>Base SQLite dans un dossier temporaire, migrée, supprimée à la fin du test.</summary>
public sealed class BaseTemporaire : IAsyncLifetime
{
    public string Dossier { get; } = Path.Combine(Path.GetTempPath(), "asprenaut-tests", Guid.NewGuid().ToString("N"));

    public string CheminBase => Path.Combine(Dossier, "materiel.db");

    public FabriqueConnexion Fabrique { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Fabrique = new FabriqueConnexion(CheminBase);
        await new GestionnaireMigrations(Fabrique).AppliquerAsync();
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Dossier, recursive: true);
        }
        catch (IOException)
        {
            // Fichier encore verrouillé : sans importance pour un dossier temporaire.
        }

        return Task.CompletedTask;
    }
}
