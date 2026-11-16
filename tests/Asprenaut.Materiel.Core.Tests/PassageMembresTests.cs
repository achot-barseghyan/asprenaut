using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Règle 44 : un adhérent devient moniteur, un moniteur redevient adhérent.</summary>
public class PassageMembresTests
{
    private readonly Contexte _ctx = new();

    private async Task<Adherent> AdherentAsync(string prenom, string nom, string? telephone = null)
    {
        var r = await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = prenom, Nom = nom, Telephone = telephone });
        Assert.True(r.Reussi, r.Message);
        return _ctx.Db.Adherents.Single(x => x.Id == r.Id);
    }

    /// <summary>L'adhérent a déjà pris du matériel dans une sortie (ligne sortie_equipement à son nom).</summary>
    private void DejaSorti(Adherent a) => _ctx.Db.Affectations.Add(new SortieEquipement
    {
        Id = _ctx.Db.ProchainId(), SortieId = 1, EquipementId = 1, AdherentId = a.Id, Statut = StatutSortieEquipement.Rendu,
    });

    [Fact]
    public async Task Un_adherent_devient_moniteur_et_n_est_plus_adherent_actif()
    {
        var julie = await AdherentAsync("Julie", "Lemoine", "06 12 34 56 78");
        DejaSorti(julie);

        var r = await _ctx.Adherents.PasserMoniteurAsync(julie.Id);

        Assert.True(r.Reussi, r.Message);
        var moniteur = _ctx.Db.Moniteurs.Single(m => m.Id == r.Id);
        Assert.Equal(GenreResponsable.Moniteur, moniteur.Genre);
        Assert.Equal("Julie Lemoine", moniteur.NomComplet);
        Assert.Equal("06 12 34 56 78", moniteur.Telephone);
        Assert.True(moniteur.Actif);
        Assert.False(_ctx.Db.Adherents.Single(a => a.Id == julie.Id).Actif);
        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.AdherentDevenuMoniteur);
    }

    [Fact]
    public async Task Un_moniteur_devient_adherent_et_n_est_plus_responsable_propose()
    {
        var r = await _ctx.Adherents.PasserAdherentAsync(_ctx.Pascal.Id);

        Assert.True(r.Reussi, r.Message);
        var adherent = _ctx.Db.Adherents.Single(a => a.Id == r.Id);
        Assert.Equal(_ctx.Pascal.NomComplet, adherent.NomComplet);
        Assert.True(adherent.Actif);
        Assert.False(_ctx.Db.Moniteurs.Single(m => m.Id == _ctx.Pascal.Id).Actif);
        Assert.DoesNotContain(await _ctx.Moniteurs.ListerActifsAsync(GenreResponsable.Moniteur), m => m.Id == _ctx.Pascal.Id);
        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.MoniteurDevenuAdherent);
    }

    [Fact]
    public async Task Un_adherent_sans_aucune_sortie_est_supprime()
    {
        var julie = await AdherentAsync("Julie", "Lemoine", "06 12 34 56 78");
        _ctx.Db.Journal.Add(new EntreeJournal { Id = _ctx.Db.ProchainId(), Action = ActionsJournal.AdherentModifie, AdherentId = julie.Id });

        var r = await _ctx.Adherents.PasserMoniteurAsync(julie.Id);

        Assert.True(r.Reussi, r.Message);
        Assert.Contains("supprimée", r.Message);
        Assert.DoesNotContain(_ctx.Db.Adherents, a => a.Id == julie.Id);
        Assert.DoesNotContain(_ctx.Db.Journal, j => j.AdherentId == julie.Id);
        var moniteur = _ctx.Db.Moniteurs.Single(m => m.Id == r.Id);
        Assert.Equal(("Julie Lemoine", "06 12 34 56 78", true), (moniteur.NomComplet, moniteur.Telephone, moniteur.Actif));
    }

    [Fact]
    public async Task L_aller_retour_reprend_les_memes_fiches_sans_doublon()
    {
        var julie = await AdherentAsync("Julie", "Lemoine");
        DejaSorti(julie);
        var moniteurId = (await _ctx.Adherents.PasserMoniteurAsync(julie.Id)).Id!.Value;

        var retour = await _ctx.Adherents.PasserAdherentAsync(moniteurId);
        Assert.True(retour.Reussi, retour.Message);
        Assert.Equal(julie.Id, retour.Id);
        Assert.True(_ctx.Db.Adherents.Single(a => a.Id == julie.Id).Actif);
        Assert.Single(_ctx.Db.Adherents, a => a.Nom == "Lemoine");

        var encore = await _ctx.Adherents.PasserMoniteurAsync(julie.Id);
        Assert.Equal(moniteurId, encore.Id);
        Assert.True(_ctx.Db.Moniteurs.Single(m => m.Id == moniteurId).Actif);
        Assert.Single(_ctx.Db.Moniteurs, m => m.Nom == "Lemoine");
    }

    [Fact]
    public async Task Une_classe_ne_devient_pas_adherent()
    {
        var classe = _ctx.AjouterClasses("Niveau 1")[0];

        var r = await _ctx.Adherents.PasserAdherentAsync(classe.Id);

        Assert.False(r.Reussi);
        Assert.True(classe.Actif);
    }

    [Fact]
    public async Task Le_passage_demande_le_mode_gestion()
    {
        var julie = await AdherentAsync("Julie", "Lemoine");
        Assert.True(_ctx.Gestion.DefinirCode(null, "1234", "1234").Reussi);
        _ctx.Gestion.Verrouiller();

        Assert.False((await _ctx.Adherents.PasserMoniteurAsync(julie.Id)).Reussi);
        Assert.False((await _ctx.Adherents.PasserAdherentAsync(_ctx.Pascal.Id)).Reussi);
        Assert.True(_ctx.Db.Adherents.Single(a => a.Id == julie.Id).Actif);
        Assert.True(_ctx.Pascal.Actif);
    }
}
