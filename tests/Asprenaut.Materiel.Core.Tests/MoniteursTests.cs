using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Moniteurs : saison, cartes, fiche détaillée, ajout et modification.</summary>
public class MoniteursTests
{
    private readonly Contexte _ctx = new();

    [Theory]
    [InlineData(2026, 11, 6, 2026)]
    [InlineData(2026, 9, 1, 2026)]
    [InlineData(2026, 8, 31, 2025)]
    [InlineData(2027, 1, 15, 2026)]
    public void La_saison_commence_le_1er_septembre(int annee, int mois, int jour, int anneeDebut) =>
        Assert.Equal(new DateOnly(anneeDebut, 9, 1), ServiceMoniteurs.DebutSaison(new DateOnly(annee, mois, jour)));

    [Fact]
    public async Task Carte_compte_les_sorties_de_la_saison_et_le_materiel_dehors()
    {
        _ctx.Equipement("BLOC-031");
        _ctx.Equipement("BLOC-032");
        _ctx.Equipement("BLOC-033");
        await _ctx.SortieEnCoursAsync("Mer", "BLOC-031", "BLOC-032");
        var ancienne = await _ctx.SortieEnCoursAsync("Piscine", "BLOC-033");
        await _ctx.Scan.ScannerRetourAsync(ancienne, "BLOC-033");
        await _ctx.Sorties.CloturerAsync(ancienne);
        _ctx.Db.Sorties.Single(s => s.Id == ancienne).DateSortie = new DateOnly(2026, 6, 1); // Saison précédente.

        var fiche = Assert.Single(await _ctx.Moniteurs.ListerFichesAsync());

        Assert.Equal((1, 2), (fiche.SortiesSaison, fiche.NbDehors));
    }

    [Fact]
    public async Task Fiche_liste_le_materiel_dehors_et_les_dernieres_sorties()
    {
        _ctx.Equipement("BLOC-031");
        _ctx.Equipement("BLOC-032");
        var enCours = await _ctx.SortieEnCoursAsync("Mer", "BLOC-031", "BLOC-032");
        await _ctx.Scan.ScannerRetourAsync(enCours, "BLOC-031");

        var detail = await _ctx.Moniteurs.ObtenirDetailAsync(_ctx.Pascal.Id);

        Assert.NotNull(detail);
        var sortie = Assert.Single(detail.EnCours);
        Assert.Equal(["BLOC-032"], sortie.Dehors.Select(l => l.Code));
        Assert.Empty(detail.Dernieres);
        Assert.Null(await _ctx.Moniteurs.ObtenirDetailAsync(9999));
    }

    [Fact]
    public async Task Ajouter_puis_modifier_un_moniteur()
    {
        var ajout = await _ctx.Moniteurs.EnregistrerAsync(new Moniteur { Prenom = " Julie ", Nom = "Bernard", Niveau = " ", Telephone = " 06 00 " });
        Assert.True(ajout.Reussi, ajout.Message);
        var julie = _ctx.Db.Moniteurs.Single(m => m.Id == ajout.Id);
        Assert.Equal(("Julie", null, "06 00"), (julie.Prenom, julie.Niveau, julie.Telephone));

        var modif = await _ctx.Moniteurs.EnregistrerAsync(new Moniteur { Id = julie.Id, Prenom = "Julie", Nom = "Bernard", Niveau = "MF1", InspecteurTiv = true, Actif = true });
        Assert.True(modif.Reussi, modif.Message);
        Assert.Contains(await _ctx.Moniteurs.ListerInspecteursTivAsync(), m => m.Id == julie.Id);
        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.MoniteurModifie);
    }

    [Fact]
    public async Task Prenom_et_nom_obligatoires()
    {
        Assert.False((await _ctx.Moniteurs.EnregistrerAsync(new Moniteur { Prenom = "Julie", Nom = "  " })).Reussi);
        Assert.False((await _ctx.Moniteurs.EnregistrerAsync(new Moniteur { Id = 9999, Prenom = "A", Nom = "B" })).Reussi);
    }
}
