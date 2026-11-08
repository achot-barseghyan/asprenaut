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
        Assert.Null(service.DerniereSauvegarde());
    }

    [Fact]
    public async Task Derniere_sauvegarde_lue_dans_le_nom_du_fichier()
    {
        var instant = new DateTime(2026, 11, 6, 18, 5, 0);
        var service = new ServiceSauvegarde(_base.Fabrique, Path.Combine(_base.Dossier, "Sauvegardes"), () => instant);
        await service.SauvegarderAsync();
        instant = new DateTime(2026, 11, 8, 19, 2, 0);
        await service.SauvegarderAsync();

        Assert.Equal(new DateTime(2026, 11, 8, 19, 2, 0), service.DerniereSauvegarde());
    }

    [Fact]
    public async Task Changer_de_dossier_cree_le_dossier_et_y_ecrit_les_copies()
    {
        var service = new ServiceSauvegarde(_base.Fabrique, Path.Combine(_base.Dossier, "Sauvegardes"), () => new DateTime(2026, 11, 8, 19, 2, 0));
        var cle = Path.Combine(_base.Dossier, "CleUSB", "Asprenaut");

        Assert.True(service.ChangerDossier(cle).Reussi);
        var chemin = await service.SauvegarderAsync();

        Assert.Equal(Path.Combine(cle, "materiel_2026-11-08_1902.db"), chemin);
        Assert.Empty(Directory.GetFiles(cle, "*.tmp")); // Le fichier d'essai d'écriture est supprimé.
        Assert.False(service.ChangerDossier("   ").Reussi);
        Assert.Equal(cle, service.Dossier);
    }

    [Fact]
    public async Task Restaurer_remplace_les_donnees_et_garde_une_copie_des_donnees_d_avant()
    {
        var instant = new DateTime(2026, 11, 6, 18, 0, 0);
        var service = new ServiceSauvegarde(_base.Fabrique, Path.Combine(_base.Dossier, "Sauvegardes"), () => instant);
        await using (var cx = await _base.Fabrique.OuvrirAsync())
        {
            await cx.ExecuteAsync("INSERT INTO moniteur (prenom, nom, actif) VALUES ('Pascal', 'Durand', 1);");
        }

        var copie = await service.SauvegarderAsync();
        await using (var cx = await _base.Fabrique.OuvrirAsync())
        {
            await cx.ExecuteAsync("INSERT INTO moniteur (prenom, nom, actif) VALUES ('Julie', 'Bernard', 1);");
        }

        instant = new DateTime(2026, 11, 8, 19, 0, 0);
        var resultat = await service.RestaurerAsync(copie!);

        Assert.True(resultat.Reussi, resultat.Message);
        await using (var cx = await _base.Fabrique.OuvrirAsync())
        {
            Assert.Equal(1, await cx.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM moniteur;"));
            Assert.Equal("wal", await cx.ExecuteScalarAsync<string>("PRAGMA journal_mode;"));
        }

        var securite = Path.Combine(service.Dossier, "materiel_2026-11-08_1900_avant-restauration.db");
        Assert.True(File.Exists(securite));
        await using var avant = new SqliteConnection($"Data Source={securite};Pooling=False");
        await avant.OpenAsync();
        Assert.Equal(2, await avant.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM moniteur;"));
    }

    [Fact]
    public async Task Restaurer_refuse_un_fichier_qui_n_est_pas_une_sauvegarde()
    {
        var service = new ServiceSauvegarde(_base.Fabrique, Path.Combine(_base.Dossier, "Sauvegardes"), () => DateTime.Now);
        var texte = Path.Combine(_base.Dossier, "materiel_faux.db");
        await File.WriteAllTextAsync(texte, "pas une base");
        var autreBase = Path.Combine(_base.Dossier, "autre.db");
        await using (var cx = new SqliteConnection($"Data Source={autreBase};Pooling=False"))
        {
            await cx.OpenAsync();
            await cx.ExecuteAsync("CREATE TABLE truc (id INTEGER);");
        }

        Assert.False((await service.RestaurerAsync(texte)).Reussi);
        Assert.False((await service.RestaurerAsync(autreBase)).Reussi);
        Assert.False((await service.RestaurerAsync(_base.CheminBase)).Reussi);
        Assert.False(Directory.Exists(service.Dossier) && Directory.GetFiles(service.Dossier).Length > 0); // Aucune copie de sécurité inutile.
    }
}
