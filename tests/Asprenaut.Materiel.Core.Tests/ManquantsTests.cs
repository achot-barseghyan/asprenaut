using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Manquants retrouvés : au départ d'une autre sortie (automatique), ou dans la sortie clôturée (mode gestion) (règle 32).</summary>
public class ManquantsTests
{
    private readonly Contexte _ctx = new();

    /// <summary>Sortie « Lac » clôturée avec BLOC-031 manquant.</summary>
    private async Task<(Equipement Bloc, long Sortie)> ManquantAsync()
    {
        var bloc = _ctx.Equipement("BLOC-031");
        var sortie = await _ctx.SortieEnCoursAsync("Lac des Settons", "BLOC-031");
        Assert.True((await _ctx.Sorties.CloturerAsync(sortie)).Reussi);
        Assert.Equal(StatutSortieEquipement.Manquant, _ctx.StatutDe(sortie, bloc));
        return (bloc, sortie);
    }

    private void CodeGestionnaire()
    {
        Assert.True(_ctx.Gestion.DefinirCode(null, "1234", "1234").Reussi);
        _ctx.Gestion.Verrouiller();
    }

    [Fact]
    public async Task Un_manquant_scanne_au_depart_est_note_retrouve_dans_son_ancienne_sortie_puis_part()
    {
        var (bloc, ancienne) = await ManquantAsync();
        _ctx.Horloge.MaintenantUtc = _ctx.Horloge.MaintenantUtc.AddDays(2);
        var nouvelle = await _ctx.SortieAsync("Fosse");

        var r = await _ctx.Scan.ScannerDepartAsync(nouvelle, "BLOC-031");

        Assert.Equal(NiveauResultat.Succes, r.Niveau);
        Assert.StartsWith("BLOC-031 était manquant (sortie « Lac des Settons » du 06/11) : noté retrouvé.", r.Message);
        Assert.Equal(StatutSortieEquipement.Rendu, _ctx.StatutDe(ancienne, bloc));
        Assert.Equal(StatutSortieEquipement.Dehors, _ctx.StatutDe(nouvelle, bloc));
        var entree = _ctx.Db.Journal.Single(j => j.Action == ActionsJournal.ManquantRetrouve);
        Assert.Equal((bloc.Id, ancienne), (entree.EquipementId!.Value, entree.SortieId!.Value));

        // Inventaire : plus « Manquant », dehors dans la nouvelle sortie ; rentré, il est disponible.
        Assert.Equal(StatutInventaire.Dehors, (await _ctx.Inventaire.ListerAsync("BLOC-031", null)).Single().Statut);
        await _ctx.Sorties.ValiderDepartAsync(nouvelle);
        await _ctx.Scan.ScannerRetourAsync(nouvelle, "BLOC-031");
        Assert.Equal(StatutInventaire.Disponible, (await _ctx.Inventaire.ListerAsync("BLOC-031", null)).Single().Statut);
    }

    [Fact]
    public async Task Un_manquant_est_retrouve_meme_si_son_depart_est_refuse()
    {
        var (bloc, ancienne) = await ManquantAsync();
        bloc.Etat = EtatEquipement.HorsService;

        var r = await _ctx.Scan.ScannerDepartAsync(await _ctx.SortieAsync("Fosse"), "BLOC-031");

        Assert.Equal(NiveauResultat.Erreur, r.Niveau);
        Assert.Contains("noté retrouvé", r.Message);
        Assert.Contains("hors service", r.Message);
        Assert.Equal(StatutSortieEquipement.Rendu, _ctx.StatutDe(ancienne, bloc));
    }

    [Fact]
    public async Task Dans_une_sortie_cloturee_scanner_un_manquant_demande_le_mode_gestion()
    {
        var (bloc, sortie) = await ManquantAsync();
        CodeGestionnaire();

        var refus = await _ctx.Scan.ScannerRetourAsync(sortie, "BLOC-031");
        Assert.Equal(NiveauResultat.Erreur, refus.Niveau);
        Assert.StartsWith("Sortie clôturée :", refus.Message);
        Assert.Equal(StatutSortieEquipement.Manquant, _ctx.StatutDe(sortie, bloc));

        Assert.True(_ctx.Gestion.Deverrouiller("1234").Reussi);
        Assert.Equal(NiveauResultat.Succes, (await _ctx.Scan.ScannerRetourAsync(sortie, "BLOC-031")).Niveau);
        Assert.Equal(StatutSortieEquipement.Rendu, _ctx.StatutDe(sortie, bloc));
    }

    [Fact]
    public async Task Marquer_rendu_un_manquant_demande_le_mode_gestion_et_le_journalise()
    {
        var (bloc, sortie) = await ManquantAsync();
        CodeGestionnaire();

        Assert.False((await _ctx.Scan.RendreManquantAsync(sortie, bloc.Id)).Reussi);
        Assert.Equal(StatutSortieEquipement.Manquant, _ctx.StatutDe(sortie, bloc));

        Assert.True(_ctx.Gestion.Deverrouiller("1234").Reussi);
        var r = await _ctx.Scan.RendreManquantAsync(sortie, bloc.Id);
        Assert.Equal("BLOC-031 est noté rendu.", r.Message);
        Assert.Equal(StatutSortieEquipement.Rendu, _ctx.StatutDe(sortie, bloc));
        Assert.Equal(ActionsJournal.ManquantRetrouve, _ctx.Db.Journal.Last().Action);
        Assert.Equal(StatutInventaire.Disponible, (await _ctx.Inventaire.ListerAsync("BLOC-031", null)).Single().Statut);

        Assert.False((await _ctx.Scan.RendreManquantAsync(sortie, bloc.Id)).Reussi); // Déjà rendu.
    }

    [Fact]
    public async Task Sans_code_gestionnaire_tout_reste_libre()
    {
        var (bloc, sortie) = await ManquantAsync();

        Assert.True((await _ctx.Scan.RendreManquantAsync(sortie, bloc.Id)).Reussi);
    }
}
