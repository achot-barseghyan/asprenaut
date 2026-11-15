using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Data.Depots;
using Dapper;

namespace Asprenaut.Materiel.Data.Tests;

/// <summary>Modèles de sortie (migration 024).</summary>
public class ModelesSortieDepotsTests : IAsyncLifetime
{
    private readonly BaseTemporaire _base = new();

    public Task InitializeAsync() => _base.InitializeAsync();

    public Task DisposeAsync() => _base.DisposeAsync();

    [Fact]
    public async Task Modele_ajoute_relu_modifie_puis_supprime()
    {
        var pascal = await new MoniteurRepository(_base.Fabrique).AjouterAsync(new Moniteur { Prenom = "Pascal", Nom = "Durand" });
        var types = await new TypeSortieRepository(_base.Fabrique).ListerAsync();
        var depot = new ModeleSortieRepository(_base.Fabrique);

        var id = await depot.AjouterAsync(new ModeleSortie
        {
            Nom = "Cours du lundi", Jour = DayOfWeek.Monday, TypeId = types[0].Id, MoniteurId = pascal, Lieu = "Piscine", Intitule = "",
            Utilisations = 2, UnParCategorie = false, Notes = "N1",
        });
        var lu = (await depot.ObtenirAsync(id))!;
        Assert.Equal(("Cours du lundi", (DayOfWeek?)DayOfWeek.Monday, (long?)types[0].Id, (long?)pascal), (lu.Nom, lu.Jour, lu.TypeId, lu.MoniteurId));
        Assert.Equal(("Piscine", (int?)2, false, "N1"), (lu.Lieu, lu.Utilisations, lu.UnParCategorie, lu.Notes));

        await using (var cx = await _base.Fabrique.OuvrirAsync())
        {
            Assert.Equal(1L, await cx.ExecuteScalarAsync<long>("SELECT jour FROM modele_sortie;")); // 0 = dimanche.
        }

        lu.Jour = null;
        lu.Utilisations = null;
        lu.Nom = "Cours";
        await depot.MettreAJourAsync(lu);
        var relu = Assert.Single(await depot.ListerAsync());
        Assert.Equal(("Cours", (DayOfWeek?)null, (int?)null), (relu.Nom, relu.Jour, relu.Utilisations));

        Assert.True(await depot.SupprimerAsync(id));
        Assert.Empty(await depot.ListerAsync());
    }

    [Fact]
    public async Task Le_nom_est_unique_sans_les_majuscules_et_un_type_supprime_laisse_le_modele()
    {
        var typesDepot = new TypeSortieRepository(_base.Fabrique);
        var nouveau = await typesDepot.AjouterAsync(new TypeDeSortie { Nom = "Bateau", Ordre = 9 });
        var depot = new ModeleSortieRepository(_base.Fabrique);
        var id = await depot.AjouterAsync(new ModeleSortie { Nom = "Bateau", TypeId = nouveau });
        await Assert.ThrowsAnyAsync<Exception>(() => depot.AjouterAsync(new ModeleSortie { Nom = "BATEAU" }));

        Assert.True(await typesDepot.SupprimerAsync(nouveau)); // Jamais utilisé par une sortie : supprimé, le modèle reste.
        Assert.Null((await depot.ObtenirAsync(id))!.TypeId);
    }
}
