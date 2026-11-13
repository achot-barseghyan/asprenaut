using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Nitrox et adhérents désactivés au départ d'une sortie nominative (règle 31).</summary>
public class NitroxTests
{
    private readonly Contexte _ctx = new();

    public NitroxTests()
    {
        _ctx.Db.Categories.Add(new Categorie { Id = _ctx.Db.ProchainId(), Nom = "Masques" });
    }

    private async Task<Adherent> AdherentAsync(string prenom, string nom, bool nitrox = false)
    {
        var r = await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = prenom, Nom = nom, Nitrox = nitrox });
        Assert.True(r.Reussi, r.Message);
        return _ctx.Db.Adherents.Single(x => x.Id == r.Id);
    }

    private async Task<long> SortieNominativeAsync()
    {
        var mer = _ctx.Db.TypesSortie.Single(t => t.Nom == "Mer");
        mer.Nominatif = true;
        return (await _ctx.Sorties.CreerAsync(new NouvelleSortie(_ctx.Horloge.AujourdHui, TypeSortie.Mer, "Marseille", _ctx.Pascal.Id, TypeId: mer.Id))).Id!.Value;
    }

    [Fact]
    public async Task Un_bloc_nitrox_ne_part_qu_avec_un_adherent_certifie_nitrox()
    {
        var bloc = _ctx.Equipement("BLOC-040");
        bloc.Nitrox = true;
        var julie = await AdherentAsync("Julie", "Martin");
        var thomas = await AdherentAsync("Thomas", "Bernard", nitrox: true);
        var sortie = await SortieNominativeAsync();
        await _ctx.Scan.ScannerDepartAsync(sortie, "BLOC-040");

        var refus = await _ctx.Sorties.AttribuerAdherentAsync(sortie, bloc.Id, julie.Id);
        Assert.False(refus.Reussi);
        Assert.Equal("BLOC-040 est un équipement nitrox : Julie Martin n'a pas la certification nitrox.", refus.Message);
        Assert.Null(_ctx.Db.Affectations.Single().AdherentId);
        Assert.Equal(NiveauResultat.Erreur, (await _ctx.Scan.ScannerDepartAsync(sortie, "BLOC-041")).Niveau); // Toujours en attente.

        Assert.True((await _ctx.Sorties.AttribuerAdherentAsync(sortie, bloc.Id, thomas.Id)).Reussi);
    }

    [Fact]
    public async Task Un_bloc_ordinaire_part_avec_un_adherent_sans_certification()
    {
        var bloc = _ctx.Equipement("BLOC-031");
        var julie = await AdherentAsync("Julie", "Martin");
        var sortie = await SortieNominativeAsync();
        await _ctx.Scan.ScannerDepartAsync(sortie, "BLOC-031");

        Assert.True((await _ctx.Sorties.AttribuerAdherentAsync(sortie, bloc.Id, julie.Id)).Reussi);
    }

    [Fact]
    public async Task Un_adherent_desactive_ne_peut_plus_sortir_de_materiel_et_reste_dans_la_liste()
    {
        var bloc = _ctx.Equipement("BLOC-031");
        var julie = await AdherentAsync("Julie", "Martin");
        var sortie = await SortieNominativeAsync();
        await _ctx.Scan.ScannerDepartAsync(sortie, "BLOC-031");

        var r = await _ctx.Adherents.ChangerActifAsync(julie.Id, false);
        Assert.Equal("Julie Martin est désactivé : il ne peut plus sortir de matériel.", r.Message);

        Assert.Equal("Julie Martin est désactivé : il ne peut plus sortir de matériel.", (await _ctx.Sorties.AttribuerAdherentAsync(sortie, bloc.Id, julie.Id)).Message);
        Assert.Empty(await _ctx.Adherents.ListerPourDepartAsync());
        Assert.Contains((await _ctx.Adherents.ListerAsync(null, FiltreAdherents.Tous)).Fiches, f => f.Adherent.Id == julie.Id); // Pour la réactiver.

        Assert.True((await _ctx.Adherents.ChangerActifAsync(julie.Id, true)).Reussi);
        Assert.True((await _ctx.Sorties.AttribuerAdherentAsync(sortie, bloc.Id, julie.Id)).Reussi);
    }

    [Fact]
    public async Task Desactiver_un_adherent_demande_le_code_gestionnaire()
    {
        var julie = await AdherentAsync("Julie", "Martin");
        Assert.True(_ctx.Gestion.DefinirCode(null, "1234", "1234").Reussi);
        _ctx.Gestion.Verrouiller();

        Assert.False((await _ctx.Adherents.ChangerActifAsync(julie.Id, false)).Reussi);
        Assert.True(julie.Actif);
    }

    [Fact]
    public async Task Seuls_les_blocs_et_les_detendeurs_peuvent_etre_nitrox()
    {
        Equipement Nouveau(string code, string categorie) => new()
        {
            Code = code, Libelle = code, CategorieId = _ctx.Db.Categories.Single(c => c.Nom == categorie).Id, Nitrox = true,
        };

        var bloc = await _ctx.Inventaire.CreerAsync(Nouveau("BLOC-050", "Blocs"));
        var det = await _ctx.Inventaire.CreerAsync(Nouveau("A50", "Détendeurs"));
        var masque = await _ctx.Inventaire.CreerAsync(Nouveau("MASQ-050", "Masques"));

        Assert.True(_ctx.Db.Equipements.Single(e => e.Id == bloc.Id).Nitrox);
        Assert.True(_ctx.Db.Equipements.Single(e => e.Id == det.Id).Nitrox);
        Assert.False(_ctx.Db.Equipements.Single(e => e.Id == masque.Id).Nitrox);

        // Modification : le journal le dit.
        var copie = _ctx.Db.Equipements.Single(e => e.Id == bloc.Id);
        var modifie = new Equipement { Id = copie.Id, Code = copie.Code, Libelle = copie.Libelle, CategorieId = copie.CategorieId, Etat = copie.Etat, Nitrox = false };
        Assert.True((await _ctx.Inventaire.ModifierAsync(modifie)).Reussi);
        Assert.Contains("plus nitrox", _ctx.Db.Journal.Last().Details);
        Assert.False((await _ctx.Inventaire.ListerAsync("BLOC-050", null)).Single().Nitrox);
    }

    [Fact]
    public async Task L_import_lit_la_colonne_nitrox_et_la_laisse_si_elle_est_absente()
    {
        var julie = await AdherentAsync("Julie", "Martin", nitrox: true);
        julie.Licence = "L1";
        await _ctx.Adherents.EnregistrerAsync(julie);

        await _ctx.Adherents.ImporterAsync([["Nom", "Prénom", "Licence"], ["Martin", "Julie", "L1"]]);
        Assert.True(_ctx.Db.Adherents.Single(a => a.Licence == "L1").Nitrox); // Pas de colonne : inchangé.

        await _ctx.Adherents.ImporterAsync([["Nom", "Prénom", "Licence", "Nitrox"], ["Martin", "Julie", "L1", "non"], ["Dubois", "Léa", "L2", "Oui"]]);
        Assert.False(_ctx.Db.Adherents.Single(a => a.Licence == "L1").Nitrox);
        Assert.True(_ctx.Db.Adherents.Single(a => a.Licence == "L2").Nitrox);
    }

    // Modèle Excel : ses en-têtes sont ceux de l'import ; une case Nitrox laissée vide ne retire pas la certification.
    [Fact]
    public async Task Le_modele_excel_se_reimporte_et_une_case_nitrox_vide_ne_change_rien()
    {
        var lea = await AdherentAsync("Léa", "Dubois", nitrox: true);
        lea.Licence = "L2";
        await _ctx.Adherents.EnregistrerAsync(lea);
        var entete = Core.Services.ServiceAdherents.ColonnesModele(_ctx.Adherents.Niveaux).Select(c => c.EnTete).ToList();
        Assert.Equal(["Nom", "Prénom", "Niveau", "Licence", "Téléphone", "Nitrox"], entete);

        var r = await _ctx.Adherents.ImporterAsync([entete, ["Dubois", "Léa", "Niveau 3", "L2", "", ""], ["Petit", "Hugo", "", "", "06 11 22 33 44", "Oui"]]);

        Assert.True(r.Reussi, r.Message);
        var relue = _ctx.Db.Adherents.Single(a => a.Licence == "L2");
        Assert.Equal(("Niveau 3", true), (relue.Niveau, relue.Nitrox)); // Mis à jour par la licence, nitrox gardé.
        Assert.True(_ctx.Db.Adherents.Single(a => a.Nom == "Petit").Nitrox);
        Assert.Equal("Adhérents à remplir - Asprenaut.xlsx", Core.Services.ServiceAdherents.NomFichierModele(" Asprenaut "));
    }
}
