using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

public class ModificationEquipementTests
{
    private readonly Contexte _ctx = new();

    private Equipement Copie(Equipement e) => new()
    {
        Id = e.Id,
        Code = e.Code,
        Libelle = e.Libelle,
        CategorieId = e.CategorieId,
        Taille = e.Taille,
        Etat = e.Etat,
        DateProchaineRevision = e.DateProchaineRevision,
        DateRequalification = e.DateRequalification,
        Notes = e.Notes,
    };

    [Fact]
    public async Task Modifier_change_l_equipement_et_le_note_au_journal()
    {
        var bloc = _ctx.Equipement("BLOC-031");
        var modifie = Copie(bloc);
        modifie.Code = " bloc-131 ";
        modifie.Libelle = " Bloc 15 L acier ";
        modifie.DateProchaineRevision = new DateOnly(2020, 1, 1); // Une date passée est un retard réel : acceptée.

        var r = await _ctx.Inventaire.ModifierAsync(modifie);

        Assert.True(r.Reussi, r.Message);
        Assert.Equal(("BLOC-131", "Bloc 15 L acier", new DateOnly(2020, 1, 1)), (bloc.Code, bloc.Libelle, bloc.DateProchaineRevision));
        var entree = _ctx.Db.Journal.Single(j => j.Action == ActionsJournal.EquipementModifie);
        Assert.Equal(bloc.Id, entree.EquipementId);
        Assert.Equal("BLOC-031 → BLOC-131 modifié (code, nom, révision)", entree.Details);
    }

    [Fact]
    public async Task Sans_changement_rien_n_est_journalise()
    {
        var bloc = _ctx.Equipement("BLOC-031");

        var r = await _ctx.Inventaire.ModifierAsync(Copie(bloc));

        Assert.True(r.Reussi);
        Assert.Equal("Rien n'a changé.", r.Message);
        Assert.DoesNotContain(_ctx.Db.Journal, j => j.Action == ActionsJournal.EquipementModifie);
    }

    [Fact]
    public async Task Modifier_refuse_un_code_deja_pris_un_nom_vide_et_garde_la_requalification_aux_blocs()
    {
        _ctx.Equipement("BLOC-031");
        var bloc = _ctx.Equipement("BLOC-032");
        var det = _ctx.Equipement("DET-001", categorie: "Détendeurs");

        var doublon = Copie(bloc);
        doublon.Code = "bloc-031";
        Assert.Equal("Le code BLOC-031 existe déjà.", (await _ctx.Inventaire.ModifierAsync(doublon)).Message);

        var sansNom = Copie(bloc);
        sansNom.Libelle = "  ";
        Assert.False((await _ctx.Inventaire.ModifierAsync(sansNom)).Reussi);

        var requalif = Copie(det);
        requalif.DateRequalification = new DateOnly(2030, 1, 1);
        requalif.Notes = "Révisé chez Aqua";
        Assert.True((await _ctx.Inventaire.ModifierAsync(requalif)).Reussi);
        Assert.Null(det.DateRequalification); // Pas de requalification pour un détendeur.
        Assert.Equal("Révisé chez Aqua", det.Notes);
    }

    // Règle : on ne met pas hors service (ni ne réforme) un équipement dehors ; en atelier, son état suit l'atelier.
    [Fact]
    public async Task L_etat_ne_change_pas_si_l_equipement_est_dehors_ou_en_atelier()
    {
        var bloc = _ctx.Equipement("BLOC-031");
        var det = _ctx.Equipement("DET-001", categorie: "Détendeurs");
        var sortie = await _ctx.SortieEnCoursAsync("Fosse", "BLOC-031");

        var reforme = Copie(bloc);
        reforme.Etat = EtatEquipement.Reforme;
        Assert.Equal("BLOC-031 est dehors : attendez son retour avant de le mettre hors service.", (await _ctx.Inventaire.ModifierAsync(reforme)).Message);

        await _ctx.Scan.ScannerRetourAsync(sortie, "BLOC-031");
        Assert.True((await _ctx.Inventaire.ModifierAsync(reforme)).Reussi);
        Assert.Equal(EtatEquipement.Reforme, bloc.Etat);

        await _ctx.Revisions.EnvoyerEnAtelierAsync("DET-001", "Aqua Service", "Fuite", null);
        var enService = Copie(det);
        enService.Etat = EtatEquipement.EnService;
        Assert.Equal("DET-001 est en atelier : enregistrez son retour dans Révisions.", (await _ctx.Inventaire.ModifierAsync(enService)).Message);
    }

    [Fact]
    public async Task Modifier_est_une_action_protegee()
    {
        var bloc = _ctx.Equipement("BLOC-031");
        Assert.True(_ctx.Gestion.DefinirCode(null, "1234", "1234").Reussi);
        _ctx.Gestion.Verrouiller();
        var modifie = Copie(bloc);
        modifie.Libelle = "Autre";

        Assert.False((await _ctx.Inventaire.ModifierAsync(modifie)).Reussi);
        Assert.Equal("Bloc 12 L", bloc.Libelle);
    }
}
