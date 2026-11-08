using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

public class ScanRetourTests
{
    private readonly Contexte _ctx = new();

    [Fact]
    public async Task Equipement_dehors_passe_a_rendu()
    {
        var bloc = _ctx.Equipement("BLOC-031");
        var sortie = await _ctx.SortieEnCoursAsync("Piscine", "BLOC-031");

        var r = await _ctx.Scan.ScannerRetourAsync(sortie, "BLOC-031");

        Assert.Equal(NiveauResultat.Succes, r.Niveau);
        Assert.Equal(StatutSortieEquipement.Rendu, _ctx.StatutDe(sortie, bloc));
        Assert.NotNull(_ctx.Db.Affectations.Single().ScanneRetourLe);
        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.ScanRetour);
    }

    [Fact]
    public async Task Equipement_deja_rendu_donne_un_avertissement()
    {
        _ctx.Equipement("BLOC-031");
        var sortie = await _ctx.SortieEnCoursAsync("Piscine", "BLOC-031");
        await _ctx.Scan.ScannerRetourAsync(sortie, "BLOC-031");

        var r = await _ctx.Scan.ScannerRetourAsync(sortie, "BLOC-031");

        Assert.Equal(NiveauResultat.Avertissement, r.Niveau);
        Assert.Contains("déjà rendu", r.Message);
    }

    [Fact]
    public async Task Retour_refuse_tant_que_le_depart_n_est_pas_valide()
    {
        _ctx.Equipement("BLOC-031");
        var sortie = await _ctx.SortieAsync();
        await _ctx.Scan.ScannerDepartAsync(sortie, "BLOC-031");

        var r = await _ctx.Scan.ScannerRetourAsync(sortie, "BLOC-031");

        Assert.Equal(NiveauResultat.Erreur, r.Niveau);
    }

    // Règle : au retour, un équipement absent de la sortie → avertir, ne rien modifier.
    [Fact]
    public async Task Equipement_absent_de_la_sortie_avertit_sans_rien_modifier()
    {
        _ctx.Equipement("BLOC-031");
        _ctx.Equipement("BLOC-099");
        var sortie = await _ctx.SortieEnCoursAsync("Piscine", "BLOC-031");
        var journalAvant = _ctx.Db.Journal.Count;

        var r = await _ctx.Scan.ScannerRetourAsync(sortie, "BLOC-099");

        Assert.Equal(NiveauResultat.Avertissement, r.Niveau);
        Assert.Equal(ActionProposee.Aucune, r.Action);
        Assert.Single(_ctx.Db.Affectations);
        Assert.Equal(StatutSortieEquipement.Dehors, _ctx.Db.Affectations.Single().Statut);
        Assert.Equal(journalAvant, _ctx.Db.Journal.Count);
    }

    // Règle : s'il est dehors dans une autre sortie, proposer de le rentrer dans cette autre sortie.
    [Fact]
    public async Task Equipement_dehors_dans_une_autre_sortie_propose_de_le_rentrer_la_bas()
    {
        var bloc = _ctx.Equipement("BLOC-031");
        _ctx.Equipement("BLOC-032");
        var fosse = await _ctx.SortieEnCoursAsync("Fosse", "BLOC-031");
        var piscine = await _ctx.SortieEnCoursAsync("Piscine", "BLOC-032");

        var r = await _ctx.Scan.ScannerRetourAsync(piscine, "BLOC-031");

        Assert.Equal(NiveauResultat.Avertissement, r.Niveau);
        Assert.Equal(ActionProposee.RentrerDansAutreSortie, r.Action);
        Assert.Equal(fosse, r.AutreSortieId);
        Assert.Equal(StatutSortieEquipement.Dehors, _ctx.StatutDe(fosse, bloc));

        // L'utilisateur accepte : on rescanne dans l'autre sortie.
        var accepte = await _ctx.Scan.ScannerRetourAsync(r.AutreSortieId!.Value, r.Code!);
        Assert.Equal(NiveauResultat.Succes, accepte.Niveau);
        Assert.Equal(StatutSortieEquipement.Rendu, _ctx.StatutDe(fosse, bloc));
    }

    // Règle : un équipement "Manquant" scanné plus tard au retour passe à "Rendu", et le journal le note.
    [Fact]
    public async Task Manquant_scanne_dans_sa_sortie_cloturee_passe_a_rendu()
    {
        var bloc = _ctx.Equipement("BLOC-031");
        var sortie = await _ctx.SortieEnCoursAsync("Mer", "BLOC-031");
        await _ctx.Sorties.CloturerAsync(sortie);
        Assert.Equal(StatutSortieEquipement.Manquant, _ctx.StatutDe(sortie, bloc));

        var r = await _ctx.Scan.ScannerRetourAsync(sortie, "BLOC-031");

        Assert.Equal(NiveauResultat.Succes, r.Niveau);
        Assert.Equal(StatutSortieEquipement.Rendu, _ctx.StatutDe(sortie, bloc));
        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.ManquantRetrouve);
    }

    [Fact]
    public async Task Manquant_scanne_au_retour_d_une_autre_sortie_passe_a_rendu()
    {
        var bloc = _ctx.Equipement("BLOC-031");
        _ctx.Equipement("BLOC-032");
        var mer = await _ctx.SortieEnCoursAsync("Mer", "BLOC-031");
        await _ctx.Sorties.CloturerAsync(mer);
        var piscine = await _ctx.SortieEnCoursAsync("Piscine", "BLOC-032");

        var r = await _ctx.Scan.ScannerRetourAsync(piscine, "BLOC-031");

        Assert.Equal(NiveauResultat.Succes, r.Niveau);
        Assert.Contains("manquant", r.Message);
        Assert.Equal(StatutSortieEquipement.Rendu, _ctx.StatutDe(mer, bloc));
        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.ManquantRetrouve && j.Details.Contains("BLOC-031"));
    }

    [Fact]
    public async Task Code_inconnu_au_retour_est_une_erreur()
    {
        _ctx.Equipement("BLOC-031");
        var sortie = await _ctx.SortieEnCoursAsync("Piscine", "BLOC-031");

        var r = await _ctx.Scan.ScannerRetourAsync(sortie, "XYZ");

        Assert.Equal(NiveauResultat.Erreur, r.Niveau);
        Assert.Contains("inconnu", r.Message);
    }
}
