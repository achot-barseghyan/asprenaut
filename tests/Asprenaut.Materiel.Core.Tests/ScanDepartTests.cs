using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

public class ScanDepartTests
{
    private readonly Contexte _ctx = new();

    [Fact]
    public async Task Equipement_en_service_part_avec_succes()
    {
        var bloc = _ctx.Equipement("BLOC-031");
        var sortie = await _ctx.SortieAsync();

        var r = await _ctx.Scan.ScannerDepartAsync(sortie, "BLOC-031");

        Assert.Equal(NiveauResultat.Succes, r.Niveau);
        Assert.Same(bloc, r.Equipement);
        Assert.Equal(StatutSortieEquipement.Dehors, _ctx.StatutDe(sortie, bloc));
        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.ScanDepart);
    }

    [Fact]
    public async Task Code_brut_est_nettoye_avant_recherche()
    {
        _ctx.Equipement("BLOC-031");
        var sortie = await _ctx.SortieAsync();

        var r = await _ctx.Scan.ScannerDepartAsync(sortie, "  asp:bloc-031 ");

        Assert.Equal(NiveauResultat.Succes, r.Niveau);
    }

    // Règle : un équipement ne peut être "Dehors" que dans une seule sortie à la fois.
    [Fact]
    public async Task Equipement_deja_dehors_ailleurs_est_refuse_avec_nom_de_la_sortie()
    {
        var bloc = _ctx.Equipement("BLOC-031");
        var premiere = await _ctx.SortieEnCoursAsync("Fosse Villeneuve", "BLOC-031");
        var seconde = await _ctx.SortieAsync("Cours en piscine");

        var r = await _ctx.Scan.ScannerDepartAsync(seconde, "BLOC-031");

        Assert.Equal(NiveauResultat.Erreur, r.Niveau);
        Assert.Contains("Fosse Villeneuve", r.Message);
        Assert.Equal(premiere, r.AutreSortieId);
        Assert.DoesNotContain(_ctx.Db.Affectations, a => a.SortieId == seconde && a.EquipementId == bloc.Id);
    }

    // Règle : un équipement HorsService ou Reforme ne peut pas partir.
    [Theory]
    [InlineData(EtatEquipement.HorsService, "hors service")]
    [InlineData(EtatEquipement.Reforme, "réformé")]
    public async Task Equipement_hors_service_ou_reforme_ne_part_pas(EtatEquipement etat, string motCle)
    {
        _ctx.Equipement("DET-004", etat);
        var sortie = await _ctx.SortieAsync();

        var r = await _ctx.Scan.ScannerDepartAsync(sortie, "DET-004");

        Assert.Equal(NiveauResultat.Erreur, r.Niveau);
        Assert.Contains(motCle, r.Message);
        Assert.Empty(_ctx.Db.Affectations);
    }

    // Règle : deux scans au départ de la même sortie → ignoré, "déjà scanné".
    [Fact]
    public async Task Double_scan_dans_la_meme_sortie_est_ignore()
    {
        _ctx.Equipement("BLOC-031");
        var sortie = await _ctx.SortieAsync();
        await _ctx.Scan.ScannerDepartAsync(sortie, "BLOC-031");

        var r = await _ctx.Scan.ScannerDepartAsync(sortie, "BLOC-031");

        Assert.Equal(NiveauResultat.Avertissement, r.Niveau);
        Assert.Contains("déjà scanné", r.Message);
        Assert.Single(_ctx.Db.Affectations);
    }

    // Règle : code inconnu → proposer la création.
    [Fact]
    public async Task Code_inconnu_propose_la_creation()
    {
        var sortie = await _ctx.SortieAsync();

        var r = await _ctx.Scan.ScannerDepartAsync(sortie, "palme-099");

        Assert.Equal(NiveauResultat.Erreur, r.Niveau);
        Assert.Equal(ActionProposee.CreerEquipement, r.Action);
        Assert.Equal("PALME-099", r.Code);
        Assert.Empty(_ctx.Db.Affectations);
    }

    // Règle : révision dépassée → part quand même, avec avertissement.
    [Fact]
    public async Task Revision_depassee_part_avec_avertissement()
    {
        var bloc = _ctx.Equipement("BLOC-012", revision: new DateOnly(2026, 10, 1));
        var sortie = await _ctx.SortieAsync();

        var r = await _ctx.Scan.ScannerDepartAsync(sortie, "BLOC-012");

        Assert.Equal(NiveauResultat.Avertissement, r.Niveau);
        Assert.Contains("révision", r.Message);
        Assert.Equal(StatutSortieEquipement.Dehors, _ctx.StatutDe(sortie, bloc));
    }

    [Fact]
    public async Task Revision_a_venir_part_sans_avertissement()
    {
        _ctx.Equipement("BLOC-013", revision: new DateOnly(2027, 3, 1));
        var sortie = await _ctx.SortieAsync();

        var r = await _ctx.Scan.ScannerDepartAsync(sortie, "BLOC-013");

        Assert.Equal(NiveauResultat.Succes, r.Niveau);
    }

    [Fact]
    public async Task Ajout_impossible_sur_une_sortie_cloturee()
    {
        _ctx.Equipement("BLOC-031");
        _ctx.Equipement("BLOC-032");
        var sortie = await _ctx.SortieEnCoursAsync("Mer", "BLOC-031");
        await _ctx.Sorties.CloturerAsync(sortie);

        var r = await _ctx.Scan.ScannerDepartAsync(sortie, "BLOC-032");

        Assert.Equal(NiveauResultat.Erreur, r.Niveau);
        Assert.Contains("clôturée", r.Message);
    }

    [Fact]
    public async Task Ajout_possible_apres_validation_du_depart()
    {
        _ctx.Equipement("BLOC-031");
        _ctx.Equipement("BLOC-032");
        var sortie = await _ctx.SortieEnCoursAsync("Mer", "BLOC-031");

        var r = await _ctx.Scan.ScannerDepartAsync(sortie, "BLOC-032");

        Assert.Equal(NiveauResultat.Succes, r.Niveau);
    }

    // Règle : pendant la préparation, on peut retirer un équipement scanné par erreur.
    [Fact]
    public async Task Retrait_possible_pendant_la_preparation()
    {
        var bloc = _ctx.Equipement("BLOC-031");
        var sortie = await _ctx.SortieAsync();
        await _ctx.Scan.ScannerDepartAsync(sortie, "BLOC-031");

        var r = await _ctx.Scan.RetirerDuDepartAsync(sortie, bloc.Id);

        Assert.True(r.Reussi);
        Assert.Empty(_ctx.Db.Affectations);
        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.RetraitDepart);
    }

    [Fact]
    public async Task Retrait_refuse_apres_validation_du_depart()
    {
        var bloc = _ctx.Equipement("BLOC-031");
        var sortie = await _ctx.SortieEnCoursAsync("Mer", "BLOC-031");

        var r = await _ctx.Scan.RetirerDuDepartAsync(sortie, bloc.Id);

        Assert.False(r.Reussi);
        Assert.Single(_ctx.Db.Affectations);
    }
}
