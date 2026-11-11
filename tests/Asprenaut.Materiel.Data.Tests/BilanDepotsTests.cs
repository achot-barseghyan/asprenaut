using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Data.Demo;
using Asprenaut.Materiel.Data.Depots;

namespace Asprenaut.Materiel.Data.Tests;

public class BilanDepotsTests : IAsyncLifetime
{
    private readonly BaseTemporaire _base = new();

    public Task InitializeAsync() => _base.InitializeAsync();

    public Task DisposeAsync() => _base.DisposeAsync();

    private sealed class HorlogeFixe : IHorloge
    {
        public DateTime MaintenantUtc { get; set; } = new(2026, 11, 6, 18, 30, 0, DateTimeKind.Utc);

        public DateOnly AujourdHui => DateOnly.FromDateTime(MaintenantUtc);
    }

    [Fact]
    public async Task Le_bilan_lit_les_passages_et_les_controles_de_la_periode()
    {
        var horloge = new HorlogeFixe();
        var equipements = new EquipementRepository(_base.Fabrique);
        var sorties = new SortieRepository(_base.Fabrique);
        var journal = new JournalRepository(_base.Fabrique);
        var moniteurs = new MoniteurRepository(_base.Fabrique);
        var revisions = new RevisionRepository(_base.Fabrique);
        var reglages = new Reglages();
        var scan = new ServiceScan(equipements, sorties, journal, horloge, reglages);
        var serviceSortie = new ServiceSortie(sorties, moniteurs, journal, horloge, reglages);
        Assert.True(await DonneesDemo.ChargerSiVideAsync(_base.Fabrique, horloge.AujourdHui));
        var pascal = (await moniteurs.ListerAsync(true)).Single(m => m.Prenom == "Pascal");

        async Task<long> SortieAsync(DateOnly date, TypeSortie type, params string[] codes)
        {
            var id = (await serviceSortie.CreerAsync(new NouvelleSortie(date, type, "Sortie", pascal.Id))).Id!.Value;
            foreach (var code in codes)
            {
                await scan.ScannerDepartAsync(id, code);
            }

            return id;
        }

        var fosse = await SortieAsync(new DateOnly(2026, 10, 3), TypeSortie.Fosse, "S01", "BLOC-031");
        await serviceSortie.ValiderDepartAsync(fosse);
        await scan.ScannerRetourAsync(fosse, "BLOC-031");
        await serviceSortie.CloturerAsync(fosse); // S01 manquant.
        await SortieAsync(new DateOnly(2026, 8, 31), TypeSortie.Mer, "A01"); // La veille de la saison.
        await SortieAsync(new DateOnly(2027, 8, 31), TypeSortie.Mer, "A02"); // Dernier jour de la saison.

        var passages = await sorties.ListerPassagesPeriodeAsync(new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31));

        Assert.Equal(["S01", "BLOC-031", "A02"], passages.Select(p => p.Code));
        var gilet = passages[0];
        Assert.Equal((fosse, new DateOnly(2026, 10, 3), TypeSortie.Fosse, StatutSortie.Cloturee), (gilet.SortieId, gilet.DateSortie, gilet.TypeSortie, gilet.StatutSortie));
        Assert.Equal(("Gilet Aqualung", "Gilets", "M", StatutSortieEquipement.Manquant), (gilet.Libelle, gilet.Categorie, gilet.Taille, gilet.Statut));
        Assert.Equal(("Pascal Durand", horloge.MaintenantUtc, (DateTime?)null), (gilet.MoniteurNom, gilet.ClotureeLe!.Value, gilet.ScanneRetourLe));
        Assert.Equal(horloge.MaintenantUtc, passages[1].ScanneRetourLe);
        Assert.Equal(StatutSortie.EnPreparation, passages[2].StatutSortie);

        // Contrôles : la démo a une TIV datée d'il y a environ 5 mois ; bornes incluses.
        var tiv = (await revisions.ListerControlesPeriodeAsync(DateOnly.MinValue, DateOnly.MaxValue)).Single();
        Assert.Equal(TypeControle.Tiv, tiv.Type);
        Assert.Single(await revisions.ListerControlesPeriodeAsync(tiv.DateControle, tiv.DateControle));
        Assert.Empty(await revisions.ListerControlesPeriodeAsync(tiv.DateControle.AddDays(1), DateOnly.MaxValue));

        // De bout en bout sur SQLite.
        var bilan = await new ServiceBilan(sorties, revisions, equipements, horloge).ObtenirAsync(2026);
        Assert.Equal((1, 2), (bilan.Sorties, bilan.EquipementsSortis));
        Assert.Equal("S01", bilan.Pertes.Single().Code);
    }
}
