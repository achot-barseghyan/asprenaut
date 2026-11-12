using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Data.Depots;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Asprenaut.Materiel.Data.Tests;

/// <summary>Problèmes signalés (migration 013) sur SQLite : formats stockés, un seul ouvert par équipement, fermeture.</summary>
public class SignalementsDepotsTests : IAsyncLifetime
{
    private readonly BaseTemporaire _base = new();

    public Task InitializeAsync() => _base.InitializeAsync();

    public Task DisposeAsync() => _base.DisposeAsync();

    private async Task<Equipement> EquipementAsync(string code)
    {
        var e = new Equipement { Code = code, Libelle = "Détendeur", CategorieId = 1, CreeLe = DateTime.UtcNow };
        await new EquipementRepository(_base.Fabrique).AjouterAsync(e);
        return e;
    }

    [Fact]
    public async Task Signalement_stocke_en_texte_relu_modifie_puis_traite()
    {
        var a07 = await EquipementAsync("A07");
        var depot = new SignalementRepository(_base.Fabrique);
        var signale = new DateTime(2026, 11, 6, 20, 58, 0, DateTimeKind.Utc);
        var s = new Signalement { EquipementId = a07.Id, Cause = "Fuite / bulles", Gravite = GraviteSignalement.HorsService, Precision = "flexible MP", SignaleLe = signale, MisHorsService = true };
        await depot.AjouterAsync(s);

        await using (var cx = await _base.Fabrique.OuvrirAsync())
        {
            var brut = await cx.QuerySingleAsync<(string Gravite, string SignaleLe, long MisHorsService)>("SELECT gravite, signale_le, mis_hors_service FROM signalement;");
            Assert.Equal(("HorsService", "2026-11-06T20:58:00.000Z", 1L), brut);
        }

        var lu = await depot.ObtenirOuvertAsync(a07.Id);
        Assert.Equal(("Fuite / bulles", GraviteSignalement.HorsService, "flexible MP", signale, true, (long?)null),
            (lu!.Cause, lu.Gravite, lu.Precision, lu.SignaleLe, lu.MisHorsService, lu.SortieId));

        lu.Gravite = GraviteSignalement.ASurveiller;
        lu.MisHorsService = false;
        await depot.MettreAJourAsync(lu);
        Assert.Equal(GraviteSignalement.ASurveiller, Assert.Single(await depot.ListerOuvertsAsync()).Gravite);

        Assert.Equal(1, await depot.TraiterAsync(a07.Id, signale.AddDays(3)));
        Assert.Null(await depot.ObtenirOuvertAsync(a07.Id));
        Assert.Empty(await depot.ListerOuvertsAsync());
        var traite = Assert.Single(await depot.ListerAsync(a07.Id));
        Assert.Equal(signale.AddDays(3), traite.TraiteLe);
    }

    [Fact]
    public async Task Un_seul_signalement_ouvert_par_equipement()
    {
        var a07 = await EquipementAsync("A07");
        var depot = new SignalementRepository(_base.Fabrique);
        Signalement Nouveau() => new() { EquipementId = a07.Id, Cause = "Autre", SignaleLe = DateTime.UtcNow };
        await depot.AjouterAsync(Nouveau());

        await Assert.ThrowsAsync<SqliteException>(() => depot.AjouterAsync(Nouveau()));

        await depot.TraiterAsync(a07.Id, DateTime.UtcNow);
        await depot.AjouterAsync(Nouveau()); // Une fois traité, un nouveau peut s'ouvrir.
        Assert.Equal(2, (await depot.ListerAsync(a07.Id)).Count);
    }
}
