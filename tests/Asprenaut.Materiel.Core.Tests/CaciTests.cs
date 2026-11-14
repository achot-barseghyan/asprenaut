using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>CACI de l'adhérent (règle 36) : case cochée et date passée → plus de matériel ; case décochée → aucun contrôle.</summary>
public class CaciTests
{
    private readonly Contexte _ctx = new();

    private DateOnly Aujourdhui => _ctx.Horloge.AujourdHui;

    private async Task<Adherent> AdherentAsync(string prenom, bool caci, DateOnly? expiration)
    {
        var r = await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = prenom, Nom = "Martin", Caci = caci, CaciExpiration = expiration });
        Assert.True(r.Reussi, r.Message);
        return _ctx.Db.Adherents.Single(x => x.Id == r.Id);
    }

    private async Task<long> SortieNominativeAsync()
    {
        var mer = _ctx.Db.TypesSortie.Single(t => t.Nom == "Mer");
        mer.Nominatif = true;
        return (await _ctx.Sorties.CreerAsync(new NouvelleSortie(Aujourdhui, TypeSortie.Mer, "Marseille", _ctx.Pascal.Id, TypeId: mer.Id))).Id!.Value;
    }

    [Fact]
    public async Task Un_adherent_au_caci_expire_ne_prend_pas_de_materiel()
    {
        var bloc = _ctx.Equipement("BLOC-031");
        var julie = await AdherentAsync("Julie", true, Aujourdhui.AddDays(-1));
        var sortie = await SortieNominativeAsync();
        await _ctx.Scan.ScannerDepartAsync(sortie, "BLOC-031");

        var refus = await _ctx.Sorties.AttribuerAdherentAsync(sortie, bloc.Id, julie.Id);
        Assert.False(refus.Reussi);
        Assert.Equal($"Le CACI de Julie Martin a expiré le {Libelles.Date(Aujourdhui.AddDays(-1))} : pas de matériel tant qu'il n'est pas renouvelé.", refus.Message);
        Assert.Null(_ctx.Db.Affectations.Single().AdherentId);

        var lignes = await new SortieRepositoryMemoire(_ctx.Db).ListerLignesAsync(sortie);
        Assert.Equal("CACI expiré", ServiceAdherents.MotifCourt(julie, lignes.Single(), lignes, Aujourdhui));

        // Renouvelé : il repart.
        julie.CaciExpiration = Aujourdhui.AddYears(1);
        Assert.True((await _ctx.Adherents.EnregistrerAsync(julie)).Reussi);
        Assert.True((await _ctx.Sorties.AttribuerAdherentAsync(sortie, bloc.Id, julie.Id)).Reussi);
    }

    [Fact]
    public async Task Le_caci_est_valable_jusqu_au_jour_d_expiration_inclus()
    {
        var bloc = _ctx.Equipement("BLOC-031");
        var julie = await AdherentAsync("Julie", true, Aujourdhui);
        var sortie = await SortieNominativeAsync();
        await _ctx.Scan.ScannerDepartAsync(sortie, "BLOC-031");

        Assert.True((await _ctx.Sorties.AttribuerAdherentAsync(sortie, bloc.Id, julie.Id)).Reussi);
        Assert.False(julie.CaciExpire(Aujourdhui));
        Assert.True(julie.CaciExpire(Aujourdhui.AddDays(1)));
    }

    [Fact]
    public async Task Case_caci_decochee_aucun_controle_et_date_effacee()
    {
        var bloc = _ctx.Equipement("BLOC-031");
        var julie = await AdherentAsync("Julie", false, Aujourdhui.AddDays(-30));
        Assert.Null(julie.CaciExpiration);
        var sortie = await SortieNominativeAsync();
        await _ctx.Scan.ScannerDepartAsync(sortie, "BLOC-031");

        Assert.True((await _ctx.Sorties.AttribuerAdherentAsync(sortie, bloc.Id, julie.Id)).Reussi);
    }

    [Fact]
    public async Task Case_caci_cochee_demande_la_date_d_expiration()
    {
        var r = await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = "Julie", Nom = "Martin", Caci = true });
        Assert.False(r.Reussi);
        Assert.Equal("Indiquez la date d'expiration du CACI.", r.Message);
    }

    [Fact]
    public async Task La_liste_et_la_fiche_signalent_le_caci_expire()
    {
        var julie = await AdherentAsync("Julie", true, Aujourdhui.AddDays(-1));
        await AdherentAsync("Thomas", true, Aujourdhui.AddDays(10));
        await AdherentAsync("Léa", false, null);

        var liste = await _ctx.Adherents.ListerAsync(null, FiltreAdherents.Tous);
        Assert.Equal(["Julie"], liste.Fiches.Where(f => f.CaciExpire).Select(f => f.Adherent.Prenom));
        Assert.True((await _ctx.Adherents.ObtenirFicheAsync(julie.Id))!.CaciExpire);
    }
}
