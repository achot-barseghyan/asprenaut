using Asprenaut.Materiel.Data.Depots;
using Dapper;

namespace Asprenaut.Materiel.Data.Tests;

/// <summary>Page des sorties clôturées sur SQLite : seules les sorties de la page sont lues, avec leurs totaux.</summary>
public class PaginationSortiesDepotsTests : IAsyncLifetime
{
    private readonly BaseTemporaire _base = new();

    public Task InitializeAsync() => _base.InitializeAsync();

    public Task DisposeAsync() => _base.DisposeAsync();

    [Fact]
    public async Task Une_page_des_sorties_cloturees_avec_leurs_totaux()
    {
        await using (var cx = await _base.Fabrique.OuvrirAsync())
        {
            await cx.ExecuteAsync("INSERT INTO moniteur (prenom, nom) VALUES ('Pascal', 'Martin');");
            await cx.ExecuteAsync("INSERT INTO equipement (code, libelle, categorie_id, cree_le) VALUES ('BLOC-031', 'Bloc', 1, '2026-01-01T00:00:00.000Z');");
            for (var i = 1; i <= 15; i++)
            {
                await cx.ExecuteAsync(
                    "INSERT INTO sortie (date_sortie, type, intitule, moniteur_id, statut, cloturee_le) VALUES ('2026-10-01', 'Piscine', @intitule, 1, 'Cloturee', @le);",
                    new { intitule = $"Sortie {i}", le = $"2026-10-{i:00}T18:00:00.000Z" });
            }

            await cx.ExecuteAsync("INSERT INTO sortie (date_sortie, type, intitule, moniteur_id, statut) VALUES ('2026-11-01', 'Piscine', 'En cours', 1, 'EnCours');");
            await cx.ExecuteAsync("""
                INSERT INTO sortie_equipement (sortie_id, equipement_id, statut, scanne_depart_le)
                VALUES ((SELECT id FROM sortie WHERE intitule = 'Sortie 14'), 1, 'Manquant', '2026-10-14T08:00:00.000Z');
                """);
        }

        var depot = new SortieRepository(_base.Fabrique);

        Assert.Equal(15, await depot.CompterClotureesAsync());
        var page = await depot.ListerClotureesAsync(0, 4);
        Assert.Equal(["Sortie 15", "Sortie 14", "Sortie 13", "Sortie 12"], page.Select(s => s.Intitule));
        Assert.Equal((1, 1), (page[1].NbTotal, page[1].NbManquants));
        Assert.Equal(["Sortie 3", "Sortie 2", "Sortie 1"], (await depot.ListerClotureesAsync(12, 4)).Select(s => s.Intitule));
    }
}
