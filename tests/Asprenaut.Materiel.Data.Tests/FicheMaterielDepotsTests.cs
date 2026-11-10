using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Data.Depots;
using Dapper;

namespace Asprenaut.Materiel.Data.Tests;

public class FicheMaterielDepotsTests : IAsyncLifetime
{
    private readonly BaseTemporaire _base = new();

    public Task InitializeAsync() => _base.InitializeAsync();

    public Task DisposeAsync() => _base.DisposeAsync();

    private sealed class HorlogeFixe : IHorloge
    {
        public DateTime MaintenantUtc => new(2026, 11, 6, 18, 30, 0, DateTimeKind.Utc);

        public DateOnly AujourdHui => DateOnly.FromDateTime(MaintenantUtc);
    }

    [Fact]
    public async Task La_fiche_d_un_detendeur_est_enregistree_relue_et_exportee()
    {
        var depot = new EquipementRepository(_base.Fabrique);
        var categories = new CategorieRepository(_base.Fabrique);
        var inventaire = new ServiceInventaire(depot, categories, new JournalRepository(_base.Fabrique), new HorlogeFixe(), new Reglages(),
            null, new SortieRepository(_base.Fabrique), new RevisionRepository(_base.Fabrique));
        var detendeurs = (await categories.ListerAsync()).Single(c => c.Nom == "Détendeurs").Id;

        var r = await inventaire.CreerAsync(new Equipement
        {
            Code = "A05",
            Libelle = "Détendeur Scubapro",
            CategorieId = detendeurs,
            Marque = "Scubapro",
            Modele = "MK2",
            NumeroSerie = "0614500681",
            Montage = MontageDetendeur.Din,
            MarqueDeuxiemeEtage = "Scub",
            ModeleDeuxiemeEtage = "R 295",
            NumeroDeuxiemeEtage = "0614500682",
            DirectSystem = true,
            Manometre = "Scubapro",
            Octopus = "R 095",
            NumeroOctopus = "1641000963",
        }, derniereRevision: new DateOnly(2021, 3, 17));
        Assert.True(r.Reussi, r.Message);

        // Stockage brut : énumération par son nom, D.S en 0/1, date de la prochaine révision à 2 ans.
        await using (var cx = await _base.Fabrique.OuvrirAsync())
        {
            Assert.Equal(("Din", 1L, "2023-03-17"), await cx.QuerySingleAsync<(string, long, string)>(
                "SELECT montage, direct_system, date_prochaine_revision FROM equipement WHERE code = 'A05';"));
            Assert.Equal(("Revision", "2021-03-17"), await cx.QuerySingleAsync<(string, string)>(
                "SELECT type, date_controle FROM controle WHERE equipement_id = @id;", new { id = r.Id }));
        }

        var relu = (await depot.ObtenirAsync(r.Id!.Value))!;
        Assert.Equal((MontageDetendeur.Din, (bool?)true, "R 295", "1641000963"), (relu.Montage, relu.DirectSystem, relu.ModeleDeuxiemeEtage, relu.NumeroOctopus));

        var ligne = (await inventaire.ListerAsync("0614500682", null)).Single(); // Recherche par numéro du deuxième étage.
        Assert.Equal(("Scubapro", "MK2", new DateOnly(2021, 3, 17)), (ligne.Marque, ligne.Modele, ligne.DerniereRevision!.Value));

        // Sans fiche : colonnes vides, D.S et montage non renseignés.
        relu.Montage = null;
        relu.DirectSystem = null;
        await depot.MettreAJourAsync(relu);
        Assert.Equal((null, (bool?)null), ((await depot.ObtenirAsync(relu.Id))!.Montage, (await depot.ObtenirAsync(relu.Id))!.DirectSystem));

        var csv = (await new ServiceExport(new ExportRepository(_base.Fabrique), new HorlogeFixe()).ExporterAsync()).Single(f => f.Nom == "materiel.csv").Contenu;
        Assert.Contains("A05;Détendeur Scubapro;Détendeurs;Scubapro;MK2;;0614500681;En service;17/03/2021;17/03/2023;;;Scub;R 295;0614500682;;Scubapro;R 095;1641000963;",
            csv, StringComparison.Ordinal);
    }
}
