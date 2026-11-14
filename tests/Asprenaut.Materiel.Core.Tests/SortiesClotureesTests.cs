using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Onglet « Clôturées » de l'écran Sorties : période, filtres, tri et totaux (règle 38).</summary>
public class SortiesClotureesTests
{
    private readonly Contexte _ctx = new(); // Aujourd'hui : 06/11/2026.

    private async Task<long> CloturerAsync(string intitule, DateOnly date, params string[] codes)
    {
        var id = await _ctx.SortieEnCoursAsync(intitule, codes);
        foreach (var code in codes.Skip(1))
        {
            await _ctx.Scan.ScannerRetourAsync(id, code); // Le premier reste dehors : manquant à la clôture.
        }

        Assert.True((await _ctx.Sorties.CloturerAsync(id)).Reussi);
        _ctx.Db.Sorties.Single(s => s.Id == id).DateSortie = date;
        return id;
    }

    [Fact]
    public void La_periode_devient_une_date_de_debut()
    {
        Assert.Equal(new DateOnly(2026, 9, 1), _ctx.Sorties.Critere(new FiltreSorties()).Depuis); // Saison : 1er septembre.
        Assert.Equal(new DateOnly(2026, 10, 7), _ctx.Sorties.Critere(new FiltreSorties(Periode: PeriodeSorties.TrenteJours)).Depuis);
        Assert.Equal(new DateOnly(2026, 8, 6), _ctx.Sorties.Critere(new FiltreSorties(Periode: PeriodeSorties.TroisMois)).Depuis);
        Assert.Null(_ctx.Sorties.Critere(new FiltreSorties(Periode: PeriodeSorties.Toutes)).Depuis);
        Assert.False(new FiltreSorties(Tri: TriSorties.Materiel).Actif); // Le tri n'est pas un filtre.
        Assert.True(new FiltreSorties(Bilan: BilanSorties.Manquants).Actif);
    }

    [Fact]
    public async Task La_page_filtree_donne_les_totaux_et_les_sorties_par_type()
    {
        _ctx.Equipement("BLOC-031");
        _ctx.Equipement("BLOC-032");
        _ctx.Equipement("BLOC-033");
        await CloturerAsync("Ancienne", new DateOnly(2026, 6, 1), "BLOC-031");
        await CloturerAsync("Récente", new DateOnly(2026, 10, 20), "BLOC-032", "BLOC-033");

        var saison = await _ctx.Sorties.ListerClotureesAsync(new FiltreSorties(), 1);
        Assert.Equal(["Récente"], saison.Sorties.Select(s => s.Intitule));
        Assert.Equal((1, 2), (saison.Total, saison.NbEquipements));

        var toutes = await _ctx.Sorties.ListerClotureesAsync(new FiltreSorties(Periode: PeriodeSorties.Toutes, Tri: TriSorties.Materiel, Descendant: false), 1);
        Assert.Equal(["Ancienne", "Récente"], toutes.Sorties.Select(s => s.Intitule));
        Assert.Equal(2, toutes.ParType!.Sum(t => t.Nombre));

        var manquants = await _ctx.Sorties.ListerClotureesAsync(new FiltreSorties(Periode: PeriodeSorties.Toutes, Bilan: BilanSorties.Manquants), 1);
        Assert.Equal(["Récente", "Ancienne"], manquants.Sorties.Select(s => s.Intitule)); // Chacune garde un manquant.
        Assert.Empty((await _ctx.Sorties.ListerClotureesAsync(new FiltreSorties(Recherche: "inconnue", Periode: PeriodeSorties.Toutes), 1)).Sorties);
    }
}
