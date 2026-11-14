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

    // À renouveler : expiré, ou valable encore 30 jours au plus (le dernier jour compte encore).
    [Fact]
    public async Task Le_filtre_a_renouveler_prend_les_caci_expires_et_ceux_qui_finissent_bientot()
    {
        var julie = await AdherentAsync("Julie", true, Aujourdhui.AddDays(-1));
        var thomas = await AdherentAsync("Thomas", true, Aujourdhui.AddDays(ServiceAdherents.JoursAvantFinCaci));
        await AdherentAsync("Hugo", true, Aujourdhui.AddDays(ServiceAdherents.JoursAvantFinCaci + 1));
        await AdherentAsync("Léa", false, null);

        var liste = await _ctx.Adherents.ListerAsync(null, FiltreAdherents.CaciARenouveler);
        Assert.Equal(2, liste.CaciARenouveler);
        Assert.Equal(["Julie", "Thomas"], liste.Fiches.Select(f => f.Adherent.Prenom).Order());
        Assert.Equal((false, true), (liste.Fiches.Single(f => f.Adherent.Id == thomas.Id).CaciExpire, liste.Fiches.Single(f => f.Adherent.Id == thomas.Id).CaciBientot));
        Assert.False(liste.Fiches.Single(f => f.Adherent.Id == julie.Id).CaciBientot); // Expiré, pas « bientôt ».
        Assert.True((await _ctx.Adherents.ObtenirFicheAsync(thomas.Id))!.CaciBientot);
    }

    [Fact]
    public async Task Le_journal_note_le_renouvellement_et_le_retrait_du_caci()
    {
        var julie = await AdherentAsync("Julie", true, Aujourdhui.AddDays(-1));
        Assert.EndsWith($" · CACI jusqu'au {Libelles.Date(Aujourdhui.AddDays(-1))}", _ctx.Db.Journal.Last().Details, StringComparison.Ordinal);

        var fin = Aujourdhui.AddYears(1);
        Assert.True((await _ctx.Adherents.EnregistrerAsync(Copie(julie, true, fin))).Reussi);
        Assert.Equal($"Adhérent modifié : Julie Martin · CACI jusqu'au {Libelles.Date(fin)}", _ctx.Db.Journal.Last().Details);

        Assert.True((await _ctx.Adherents.EnregistrerAsync(Copie(julie, true, fin))).Reussi);
        Assert.Equal("Adhérent modifié : Julie Martin", _ctx.Db.Journal.Last().Details); // Inchangé : rien de plus.

        Assert.True((await _ctx.Adherents.EnregistrerAsync(Copie(julie, false, fin))).Reussi);
        Assert.Equal("Adhérent modifié : Julie Martin · CACI retiré", _ctx.Db.Journal.Last().Details);
    }

    // Comme le formulaire : un nouvel objet, pas celui de la base.
    private static Adherent Copie(Adherent a, bool caci, DateOnly? fin) =>
        new() { Id = a.Id, Nom = a.Nom, Prenom = a.Prenom, Actif = a.Actif, Caci = caci, CaciExpiration = fin };

    // Import : colonne « CACI valable jusqu'au » (texte ou date Excel) ; vide = inchangé ; illisible = signalé et inchangé.
    [Fact]
    public async Task L_import_lit_la_date_du_caci()
    {
        var julie = await AdherentAsync("Julie", true, new DateOnly(2026, 1, 31));
        julie.Licence = "L1";
        await _ctx.Adherents.EnregistrerAsync(julie);

        var r = await _ctx.Adherents.ImporterAsync([
            ["Nom", "Prénom", "Licence", "CACI valable jusqu’au"],
            ["Martin", "Julie", "L1", ""],
            ["Petit", "Hugo", "", "15/09/2027"],
            ["Moreau", "Léo", "", "46645"],
            ["Durand", "Zoé", "", "bientôt"],
        ]);

        Assert.True(r.Reussi, r.Message);
        Assert.Contains("1 date de CACI illisible laissée de côté (ligne 5)", r.Message, StringComparison.Ordinal);
        Assert.Equal((true, new DateOnly(2026, 1, 31)), (julie.Caci, julie.CaciExpiration)); // Vide : inchangé.
        Assert.Equal((true, new DateOnly(2027, 9, 15)), Caci("Petit"));
        Assert.Equal((true, new DateOnly(2027, 9, 15)), Caci("Moreau")); // Nombre de jours d'Excel.
        Assert.Equal((false, (DateOnly?)null), Caci("Durand"));

        Assert.True((await _ctx.Adherents.ImporterAsync([["Nom", "Prénom", "Licence", "CACI"], ["Martin", "Julie", "L1", "2028-03-01"]])).Reussi);
        Assert.Equal(new DateOnly(2028, 3, 1), _ctx.Db.Adherents.Single(a => a.Licence == "L1").CaciExpiration);
    }

    private (bool, DateOnly?) Caci(string nom) =>
        _ctx.Db.Adherents.Single(a => a.Nom == nom) is { } a ? (a.Caci, a.CaciExpiration) : default;
}
