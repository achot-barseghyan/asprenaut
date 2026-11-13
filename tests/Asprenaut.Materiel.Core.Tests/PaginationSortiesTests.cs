using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Écran Sorties : les sorties clôturées s'affichent par page (12), la plus récemment clôturée d'abord.</summary>
public class PaginationSortiesTests
{
    private readonly Contexte _ctx = new();

    private async Task CloturerAsync(int nombre)
    {
        _ctx.Equipement("BLOC-031");
        for (var i = 1; i <= nombre; i++)
        {
            _ctx.Horloge.MaintenantUtc = Contexte.Maintenant.AddHours(i);
            var id = await _ctx.SortieEnCoursAsync($"Sortie {i}", "BLOC-031");
            await _ctx.Scan.ScannerRetourAsync(id, "BLOC-031");
            Assert.True((await _ctx.Sorties.CloturerAsync(id)).Reussi);
        }
    }

    [Fact]
    public async Task Les_sorties_cloturees_s_affichent_par_page_la_plus_recente_d_abord()
    {
        await CloturerAsync(27);

        var premiere = await _ctx.Sorties.ListerClotureesAsync(1);
        Assert.Equal((27, 3, 1, 12), (premiere.Total, premiere.NbPages, premiere.Premiere, premiere.Derniere));
        Assert.Equal("Sortie 27", premiere.Sorties[0].Intitule);
        Assert.Equal(12, premiere.Sorties.Count);

        var derniere = await _ctx.Sorties.ListerClotureesAsync(3);
        Assert.Equal((25, 27, 3), (derniere.Premiere, derniere.Derniere, derniere.Sorties.Count));
        Assert.Equal("Sortie 1", derniere.Sorties[^1].Intitule);

        Assert.Equal(3, (await _ctx.Sorties.ListerClotureesAsync(99)).Page); // Hors limites : la dernière page.
        Assert.Equal(1, (await _ctx.Sorties.ListerClotureesAsync(0)).Page);
    }

    [Fact]
    public async Task Sans_sortie_cloturee_une_seule_page_vide()
    {
        var page = await _ctx.Sorties.ListerClotureesAsync(1);

        Assert.Equal((0, 1, 0, 0), (page.Total, page.NbPages, page.Premiere, page.Derniere));
        Assert.Empty(page.Sorties);
    }
}
