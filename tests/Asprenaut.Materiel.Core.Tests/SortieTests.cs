using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

public class SortieTests
{
    private readonly Contexte _ctx = new();

    [Fact]
    public async Task Creation_d_une_sortie_en_preparation()
    {
        var id = await _ctx.SortieAsync("Cours en piscine");

        var sortie = _ctx.Db.Sorties.Single(s => s.Id == id);
        Assert.Equal(StatutSortie.EnPreparation, sortie.Statut);
        Assert.Equal(_ctx.Pascal.Id, sortie.MoniteurId);
        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.SortieCreee);
    }

    [Theory]
    [InlineData("  ", true)]
    [InlineData("Piscine", false)]
    public async Task Creation_exige_intitule_et_moniteur(string intitule, bool avecMoniteur)
    {
        var r = await _ctx.Sorties.CreerAsync(new NouvelleSortie(
            new DateOnly(2026, 11, 6), TypeSortie.Piscine, intitule, avecMoniteur ? _ctx.Pascal.Id : null));

        Assert.False(r.Reussi);
        Assert.Empty(_ctx.Db.Sorties);
    }

    [Fact]
    public async Task Validation_du_depart_passe_la_sortie_en_cours()
    {
        _ctx.Equipement("BLOC-031");
        var id = await _ctx.SortieAsync();
        await _ctx.Scan.ScannerDepartAsync(id, "BLOC-031");

        var r = await _ctx.Sorties.ValiderDepartAsync(id);

        Assert.True(r.Reussi);
        var sortie = _ctx.Db.Sorties.Single();
        Assert.Equal(StatutSortie.EnCours, sortie.Statut);
        Assert.Equal(Contexte.Maintenant, sortie.DepartValideLe);
    }

    [Fact]
    public async Task Validation_du_depart_refusee_sans_materiel()
    {
        var id = await _ctx.SortieAsync();

        var r = await _ctx.Sorties.ValiderDepartAsync(id);

        Assert.False(r.Reussi);
        Assert.Equal(StatutSortie.EnPreparation, _ctx.Db.Sorties.Single().Statut);
    }

    // Règle : à la clôture, les équipements encore "Dehors" passent à "Manquant".
    [Fact]
    public async Task Cloture_passe_les_dehors_en_manquant()
    {
        var b1 = _ctx.Equipement("BLOC-031");
        var b2 = _ctx.Equipement("BLOC-032");
        var id = await _ctx.SortieEnCoursAsync("Mer", "BLOC-031", "BLOC-032");
        await _ctx.Scan.ScannerRetourAsync(id, "BLOC-031");

        var r = await _ctx.Sorties.CloturerAsync(id);

        Assert.True(r.Reussi);
        Assert.Equal(StatutSortieEquipement.Rendu, _ctx.StatutDe(id, b1));
        Assert.Equal(StatutSortieEquipement.Manquant, _ctx.StatutDe(id, b2));
        Assert.Equal(StatutSortie.Cloturee, _ctx.Db.Sorties.Single().Statut);
        Assert.NotNull(_ctx.Db.Sorties.Single().ClotureeLe);
        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.ManquantDeclare && j.Details.Contains("BLOC-032"));
    }

    // Règle : demander confirmation avec le nombre de manquants.
    [Fact]
    public async Task Apercu_de_cloture_donne_le_nombre_de_manquants()
    {
        _ctx.Equipement("BLOC-031");
        _ctx.Equipement("BLOC-032");
        _ctx.Equipement("BLOC-033");
        var id = await _ctx.SortieEnCoursAsync("Mer", "BLOC-031", "BLOC-032", "BLOC-033");
        await _ctx.Scan.ScannerRetourAsync(id, "BLOC-031");

        var apercu = await _ctx.Sorties.ApercuClotureAsync(id);

        Assert.Equal(2, apercu.NbManquants);
        Assert.Contains("2 équipements", apercu.MessageConfirmation);
    }

    [Fact]
    public async Task Apercu_de_cloture_quand_tout_est_rendu()
    {
        _ctx.Equipement("BLOC-031");
        var id = await _ctx.SortieEnCoursAsync("Mer", "BLOC-031");
        await _ctx.Scan.ScannerRetourAsync(id, "BLOC-031");

        var apercu = await _ctx.Sorties.ApercuClotureAsync(id);

        Assert.Equal(0, apercu.NbManquants);
    }

    [Fact]
    public async Task Cloture_refusee_pendant_la_preparation()
    {
        var id = await _ctx.SortieAsync();

        var r = await _ctx.Sorties.CloturerAsync(id);

        Assert.False(r.Reussi);
    }

    [Fact]
    public async Task Resume_compte_rendus_et_total()
    {
        _ctx.Equipement("BLOC-031");
        _ctx.Equipement("BLOC-032");
        var id = await _ctx.SortieEnCoursAsync("Mer", "BLOC-031", "BLOC-032");
        await _ctx.Scan.ScannerRetourAsync(id, "BLOC-031");

        var resume = await _ctx.Sorties.ObtenirResumeAsync(id);

        Assert.NotNull(resume);
        Assert.Equal(2, resume.NbTotal);
        Assert.Equal(1, resume.NbRendus);
        Assert.Equal(1, resume.NbDehors);
        Assert.Equal(0.5, resume.Progression);
    }
}
