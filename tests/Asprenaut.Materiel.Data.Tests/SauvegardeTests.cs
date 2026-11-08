using Asprenaut.Materiel.Data.Connexion;
using Asprenaut.Materiel.Data.Sauvegardes;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Asprenaut.Materiel.Data.Tests;

public class SauvegardeTests : IAsyncLifetime
{
    private readonly BaseTemporaire _base = new();

    public Task InitializeAsync() => _base.InitializeAsync();

    public Task DisposeAsync() => _base.DisposeAsync();

    [Fact]
    public async Task Sauvegarde_cree_une_copie_lisible_nommee_avec_date_et_heure()
    {
        var dossier = ServiceSauvegarde.DossierParDefaut(_base.CheminBase);
        var service = new ServiceSauvegarde(_base.Fabrique, dossier, () => new DateTime(2026, 11, 6, 18, 5, 0));

        var chemin = await service.SauvegarderAsync();

        Assert.NotNull(chemin);
        Assert.Equal(Path.Combine(_base.Dossier, "Sauvegardes", "materiel_2026-11-06_1805.db"), chemin);
        await using var copie = new SqliteConnection($"Data Source={chemin};Pooling=False");
        await copie.OpenAsync();
        Assert.Equal(8, await copie.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM categorie;"));
    }

    [Fact]
    public async Task Sauvegarde_ne_garde_que_les_20_plus_recentes()
    {
        var dossier = Path.Combine(_base.Dossier, "Sauvegardes");
        var instant = new DateTime(2026, 1, 1, 8, 0, 0);
        var service = new ServiceSauvegarde(_base.Fabrique, dossier, () => instant);

        for (var i = 0; i < 23; i++)
        {
            instant = instant.AddDays(1);
            await service.SauvegarderAsync();
        }

        var fichiers = Directory.GetFiles(dossier, ServiceSauvegarde.Motif).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(20, fichiers.Count);
        Assert.Equal("materiel_2026-01-05_0800.db", fichiers[0]);
        Assert.Equal("materiel_2026-01-24_0800.db", fichiers[^1]);
    }

    [Fact]
    public async Task Pas_de_sauvegarde_si_la_base_n_existe_pas()
    {
        var fabrique = new FabriqueConnexion(Path.Combine(_base.Dossier, "absente", "materiel.db"));
        var service = new ServiceSauvegarde(fabrique, Path.Combine(_base.Dossier, "absente", "Sauvegardes"), () => DateTime.Now);

        Assert.Null(await service.SauvegarderAsync());
    }
}
