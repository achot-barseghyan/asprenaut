using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>
/// Révisions en 5 étapes (règle 33) : À prévoir → Réparation interne → À envoyer → Chez le prestataire → Fait,
/// prestataires, compteurs « au club » / « hors du club », archivage de la colonne « Fait ».
/// </summary>
public class EtapesRevisionsTests
{
    private readonly Contexte _ctx = new();
    private static readonly DateOnly Aujourdhui = DateOnly.FromDateTime(Contexte.Maintenant);

    private async Task<long> PrestataireAsync(string nom = "Aqua Service", TypePrestataire type = TypePrestataire.Magasin)
    {
        var r = await _ctx.Prestataires.EnregistrerAsync(null, nom, type, "03 85 00 00 00", "Chalon");
        Assert.True(r.Reussi, r.Message);
        return r.Id!.Value;
    }

    private Equipement Detendeur(string code = "A10", int jours = -3) => _ctx.Equipement(code, categorie: "Détendeurs", revision: Aujourdhui.AddDays(jours));

    private async Task<CarteRevision> CarteAsync(string code) =>
        (await _ctx.Revisions.TableauAsync()).Cartes.Single(c => c.Code == code && c.Etape != EtapeRevision.Fait);

    private async Task<Equipement> ChezPrestataireAsync(string code, DateOnly? retour = null)
    {
        var det = Detendeur(code);
        Assert.True((await _ctx.Revisions.EnvoyerChezPrestataireAsync(det.Id, await PrestataireAsync($"Magasin {code}"), "Dépôt prévu le 15/11")).Reussi);
        var parti = await _ctx.Revisions.MarquerPartiAsync(det.Id, retour);
        Assert.True(parti.Reussi, parti.Message);
        return det;
    }

    [Fact]
    public async Task Passer_a_envoyer_sans_prestataire_est_refuse()
    {
        var det = Detendeur();

        var r = await _ctx.Revisions.EnvoyerChezPrestataireAsync(det.Id, null, "");

        Assert.False(r.Reussi);
        Assert.Equal("Choisissez le magasin ou l'organisme.", r.Message);
        Assert.Equal(EtapeRevision.APrevoir, (await CarteAsync("A10")).Etape);
    }

    [Fact]
    public async Task Un_prestataire_inactif_n_est_plus_propose()
    {
        var id = await PrestataireAsync();
        Assert.True((await _ctx.Prestataires.ChangerActifAsync(id, false)).Reussi);

        Assert.Empty(await _ctx.Prestataires.ListerActifsAsync());
        Assert.False((await _ctx.Revisions.EnvoyerChezPrestataireAsync(Detendeur().Id, id, "")).Reussi);
        Assert.False((await _ctx.Prestataires.EnregistrerAsync(null, "AQUA SERVICE", TypePrestataire.Magasin, "", "")).Reussi); // Nom unique.
    }

    [Fact]
    public async Task A_envoyer_reste_au_club_avec_son_prestataire_et_sa_note()
    {
        var det = Detendeur();
        var r = await _ctx.Revisions.EnvoyerChezPrestataireAsync(det.Id, await PrestataireAsync(), "Dépôt prévu le 15/11", "Révision · 200 utilisations");

        Assert.True(r.Reussi, r.Message);
        var carte = await CarteAsync("A10");
        Assert.Equal((EtapeRevision.AEnvoyer, "Aqua Service", "Dépôt prévu le 15/11", "Révision · 200 utilisations", true),
            (carte.Etape, carte.Prestataire, carte.NoteEnvoi, carte.Controle, carte.AuClub));
        Assert.Equal(EtatEquipement.EnService, det.Etat); // Encore au club.
        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.RevisionEtape && j.EquipementId == det.Id);
    }

    [Fact]
    public async Task Marquer_comme_parti_renseigne_la_date_d_envoi_du_jour()
    {
        var det = await ChezPrestataireAsync("A10", Aujourdhui.AddDays(9));

        var carte = await CarteAsync("A10");
        Assert.Equal((EtapeRevision.ChezPrestataire, Aujourdhui, Aujourdhui.AddDays(9), 0, false),
            (carte.Etape, carte.DateEnvoi!.Value, carte.DateRetourPrevue!.Value, carte.JoursDehors!.Value, carte.RetourEnRetard));
        Assert.Equal(EtatEquipement.HorsService, det.Etat); // Hors du club jusqu'à son retour.
        Assert.Single(await _ctx.Revisions.ListerAtelierAsync());
    }

    [Fact]
    public async Task Marquer_comme_parti_seulement_depuis_a_envoyer()
    {
        var det = Detendeur();

        Assert.False((await _ctx.Revisions.MarquerPartiAsync(det.Id, null)).Reussi);
        await _ctx.Revisions.EnvoyerChezPrestataireAsync(det.Id, await PrestataireAsync(), "");
        Assert.False((await _ctx.Revisions.MarquerPartiAsync(det.Id, Aujourdhui.AddDays(-1))).Reussi); // Retour prévu passé.
    }

    [Fact]
    public async Task Un_retour_est_en_retard_si_la_date_prevue_est_passee()
    {
        await ChezPrestataireAsync("A10", Aujourdhui.AddDays(2));

        Assert.False(ServiceRevisions.RetourEnRetard(EtapeRevision.ChezPrestataire, Aujourdhui, Aujourdhui));
        Assert.True(ServiceRevisions.RetourEnRetard(EtapeRevision.ChezPrestataire, Aujourdhui.AddDays(-1), Aujourdhui));
        Assert.False(ServiceRevisions.RetourEnRetard(EtapeRevision.Fait, Aujourdhui.AddDays(-1), Aujourdhui));
        Assert.False(ServiceRevisions.RetourEnRetard(EtapeRevision.ChezPrestataire, null, Aujourdhui));

        _ctx.Horloge.MaintenantUtc = _ctx.Horloge.MaintenantUtc.AddDays(9);
        var carte = await CarteAsync("A10");
        Assert.Equal((true, 9), (carte.RetourEnRetard, carte.JoursDehors!.Value));
        Assert.Equal(1, (await _ctx.Revisions.TableauAsync()).RetoursEnRetard);
    }

    [Fact]
    public async Task Le_compteur_au_club_compte_a_prevoir_interne_et_a_envoyer_pas_chez_le_prestataire()
    {
        Detendeur("A01");                                          // À prévoir.
        Assert.True((await _ctx.Revisions.ReparerAuClubAsync(Detendeur("A02").Id)).Reussi);   // Réparation interne.
        await _ctx.Revisions.EnvoyerChezPrestataireAsync(Detendeur("A03").Id, await PrestataireAsync(), ""); // À envoyer.
        await ChezPrestataireAsync("A04");                          // Chez le prestataire.
        await ChezPrestataireAsync("A05");

        var tableau = await _ctx.Revisions.TableauAsync();

        Assert.Equal((3, 1, 2), (tableau.AuClub, tableau.AEnvoyer, tableau.HorsClub));
        Assert.Equal([("Magasin A04", 1), ("Magasin A05", 1)], tableau.ParPrestataire);
        Assert.Equal([EtapeRevision.APrevoir, EtapeRevision.Interne, EtapeRevision.AEnvoyer, EtapeRevision.ChezPrestataire, EtapeRevision.ChezPrestataire],
            tableau.Cartes.OrderBy(c => c.Code).Select(c => c.Etape));
    }

    [Fact]
    public async Task Reparer_au_club_depuis_a_envoyer_et_envoyer_depuis_interne()
    {
        _ctx.Reglages.ResponsableMateriel = "Paul";
        var det = Detendeur();
        var prestataire = await PrestataireAsync();

        Assert.True((await _ctx.Revisions.ReparerAuClubAsync(det.Id)).Reussi);
        Assert.Equal((EtapeRevision.Interne, "Par Paul · Échue en 11/2026"), ((await CarteAsync("A10")).Etape, (await CarteAsync("A10")).Detail));
        Assert.False((await _ctx.Revisions.ReparerAuClubAsync(det.Id)).Reussi); // Déjà en réparation interne.

        Assert.True((await _ctx.Revisions.EnvoyerChezPrestataireAsync(det.Id, prestataire, "")).Reussi); // Le club ne peut pas réparer.
        Assert.Equal(EtapeRevision.AEnvoyer, (await CarteAsync("A10")).Etape);

        Assert.True((await _ctx.Revisions.ReparerAuClubAsync(det.Id)).Reussi); // Finalement au club.
        var carte = await CarteAsync("A10");
        Assert.Equal((EtapeRevision.Interne, null, ""), (carte.Etape, carte.Prestataire, carte.NoteEnvoi));
        Assert.Single(_ctx.DepotRevisions.Suivis); // Toujours la même révision.
    }

    [Fact]
    public async Task Un_bloc_en_seance_TIV_ne_part_pas_chez_un_prestataire()
    {
        var bloc = _ctx.Equipement("BLOC-031", revision: Aujourdhui.AddDays(20));
        Assert.True((await _ctx.Revisions.PlanifierSeanceAsync(Aujourdhui.AddDays(10), "9:00", "Local")).Reussi);
        Assert.True((await _ctx.Revisions.AjouterASeanceAsync(bloc.Id)).Reussi);

        var r = await _ctx.Revisions.EnvoyerChezPrestataireAsync(bloc.Id, await PrestataireAsync("Bureau Veritas", TypePrestataire.Organisme), "");

        Assert.False(r.Reussi);
        Assert.Equal(EtapeRevision.Interne, (await CarteAsync("BLOC-031")).Etape);
    }

    [Theory]
    [InlineData(EtapeRevision.APrevoir)]
    [InlineData(EtapeRevision.Interne)]
    [InlineData(EtapeRevision.AEnvoyer)]
    [InlineData(EtapeRevision.ChezPrestataire)]
    public async Task En_retard_il_reste_bloque_pour_les_sorties_a_toutes_les_etapes_sauf_fait(EtapeRevision etape)
    {
        _ctx.Reglages.DepartSiRevisionDepassee = false;
        var det = Detendeur(jours: -10);
        switch (etape)
        {
            case EtapeRevision.Interne:
                await _ctx.Revisions.ReparerAuClubAsync(det.Id);
                break;
            case EtapeRevision.AEnvoyer:
                await _ctx.Revisions.EnvoyerChezPrestataireAsync(det.Id, await PrestataireAsync(), "");
                break;
            case EtapeRevision.ChezPrestataire:
                await _ctx.Revisions.EnvoyerChezPrestataireAsync(det.Id, await PrestataireAsync(), "");
                await _ctx.Revisions.MarquerPartiAsync(det.Id, null);
                break;
        }

        var carte = await CarteAsync("A10");
        Assert.Equal((etape, true), (carte.Etape, carte.Bloque));
        if (etape != EtapeRevision.ChezPrestataire)
        {
            Assert.Equal(NiveauResultat.Erreur, (await _ctx.Scan.ScannerDepartAsync(await _ctx.SortieAsync(), "A10")).Niveau);
        }

        // Fait : l'intervention conforme recalcule l'échéance, il n'est plus bloqué.
        var fait = await _ctx.Revisions.EnregistrerInterventionAsync(new Intervention(det.Id, TypeControle.Revision, ResultatControle.Conforme, Aujourdhui, "Révision", "Aqua"));
        Assert.True(fait.Reussi, fait.Message);
        var cartes = (await _ctx.Revisions.TableauAsync()).Cartes.Where(c => c.Code == "A10").ToList();
        Assert.Equal((EtapeRevision.Fait, false), (Assert.Single(cartes).Etape, cartes[0].Bloque));
        Assert.Equal(EtatEquipement.EnService, det.Etat);
    }

    [Fact]
    public async Task Le_signalement_hors_service_bloque_aussi_en_reparation_interne()
    {
        var det = Detendeur(jours: 400);
        Assert.True((await _ctx.Signalements.SignalerAsync(new NouveauSignalement(det.Id, null, "Fuite", GraviteSignalement.ASurveiller))).Reussi);
        Assert.True((await _ctx.Revisions.ReparerAuClubAsync(det.Id)).Reussi);

        var carte = await CarteAsync("A10");
        Assert.Equal((EtapeRevision.Interne, true, "Fuite"), (carte.Etape, carte.Signalement, carte.Controle));
        Assert.DoesNotContain((await _ctx.Revisions.TableauAsync()).Cartes, c => c.Etape == EtapeRevision.APrevoir); // Plus en double.
    }

    [Fact]
    public async Task Le_retour_du_prestataire_conforme_remet_le_compteur_a_zero_et_recalcule_l_echeance()
    {
        _ctx.Reglages.SeuilsSorties["Détendeurs"] = 10;
        var det = _ctx.Equipement("A10", categorie: "Détendeurs");
        for (var i = 0; i < 10; i++)
        {
            var sortie = await _ctx.SortieEnCoursAsync($"S{i}", "A10");
            await _ctx.Scan.ScannerRetourAsync(sortie, "A10");
        }

        Assert.Equal(new UsageEquipement(10, 10, EtatCompteur.Atteint), (await CarteAsync("A10")).Usage);
        await _ctx.Revisions.EnvoyerChezPrestataireAsync(det.Id, await PrestataireAsync(), "");
        await _ctx.Revisions.MarquerPartiAsync(det.Id, null);

        // « Retour du prestataire » : le passage en atelier est retrouvé d'après la révision.
        var r = await _ctx.Revisions.EnregistrerInterventionAsync(new Intervention(det.Id, TypeControle.Revision, ResultatControle.Conforme, Aujourdhui, "Révision atelier", "Aqua Service"));

        Assert.True(r.Reussi, r.Message);
        Assert.Equal((EtatEquipement.EnService, Aujourdhui.AddMonths(24)), (det.Etat, det.DateProchaineRevision!.Value));
        Assert.Empty(await _ctx.Revisions.ListerAtelierAsync());
        Assert.Equal(0, (await _ctx.Compteur.ListerUsagesAsync(await _ctx.Inventaire.ListerAsync(null, null))).GetValueOrDefault(det.Id)?.Utilisations ?? 0);
        var fait = (await _ctx.Revisions.TableauAsync()).Cartes.Single(c => c.Code == "A10");
        Assert.Equal((EtapeRevision.Fait, "Conforme"), (fait.Etape, fait.Badge));
    }

    [Fact]
    public async Task Le_retour_refuse_passe_l_equipement_hors_service()
    {
        var det = await ChezPrestataireAsync("A10");

        await _ctx.Revisions.EnregistrerInterventionAsync(new Intervention(det.Id, TypeControle.Revision, ResultatControle.Refuse, Aujourdhui, "Réparation", "Aqua"));

        Assert.Equal(EtatEquipement.HorsService, det.Etat);
        var fait = (await _ctx.Revisions.TableauAsync()).Cartes.Single(c => c.Code == "A10");
        Assert.Equal((EtapeRevision.Fait, "Hors service"), (fait.Etape, fait.Badge));
        Assert.DoesNotContain((await _ctx.Revisions.TableauAsync()).Cartes, c => c.Etape == EtapeRevision.APrevoir);
    }

    [Fact]
    public async Task Archiver_tout_n_archive_que_les_revisions_faites_sans_rien_supprimer()
    {
        var fait = Detendeur("A01");
        await _ctx.Revisions.EnregistrerInterventionAsync(new Intervention(fait.Id, TypeControle.Revision, ResultatControle.Conforme, Aujourdhui, "Révision", "Club"));
        await _ctx.Revisions.ReparerAuClubAsync(Detendeur("A02").Id);
        var ids = _ctx.DepotRevisions.Suivis.Select(s => s.Id).ToList();

        var r = await _ctx.Revisions.ArchiverAsync(ids); // Même avec la révision en cours dans la liste.

        Assert.Equal(("1 intervention archivée.", 2), (r.Message, _ctx.DepotRevisions.Suivis.Count));
        var tableau = await _ctx.Revisions.TableauAsync();
        Assert.DoesNotContain(tableau.Cartes, c => c.Etape == EtapeRevision.Fait);
        Assert.Contains(tableau.Cartes, c => c.Code == "A02" && c.Etape == EtapeRevision.Interne);

        // Toujours dans la fiche EPI (et l'Historique) : le contrôle n'a pas bougé.
        var fiche = await _ctx.Historique.ObtenirFicheSuiviAsync(fait.Id);
        Assert.Contains(fiche!.Interventions, i => i.Type == TypeInterventionSuivie.Controle && i.Nature == "Révision");

        // Annuler : les cartes reviennent.
        await _ctx.Revisions.DesarchiverAsync(ids);
        Assert.Contains((await _ctx.Revisions.TableauAsync()).Cartes, c => c.Code == "A01" && c.Etape == EtapeRevision.Fait);
    }

    [Fact]
    public async Task Archiver_ne_demande_pas_le_code_gestionnaire()
    {
        var det = Detendeur("A01");
        await _ctx.Revisions.EnregistrerInterventionAsync(new Intervention(det.Id, TypeControle.Revision, ResultatControle.Conforme, Aujourdhui, "Révision", "Club"));
        Assert.True(_ctx.Gestion.DefinirCode(null, "1234", "1234").Reussi);
        _ctx.Gestion.Verrouiller();

        Assert.True((await _ctx.Revisions.ArchiverAsync([_ctx.DepotRevisions.Suivis.Single().Id])).Reussi);
        Assert.False((await _ctx.Revisions.ReparerAuClubAsync(Detendeur("A02").Id)).Reussi); // Les transitions, si.
        Assert.False((await _ctx.Prestataires.EnregistrerAsync(null, "Aqua", TypePrestataire.Magasin, "", "")).Reussi);
    }

    [Fact]
    public async Task Les_faits_de_plus_de_30_jours_quittent_la_colonne()
    {
        var det = Detendeur("A01");
        await _ctx.Revisions.EnregistrerInterventionAsync(new Intervention(det.Id, TypeControle.Revision, ResultatControle.Conforme, Aujourdhui, "Révision", "Club"));

        _ctx.Horloge.MaintenantUtc = _ctx.Horloge.MaintenantUtc.AddDays(ServiceRevisions.JoursFait + 1);

        Assert.DoesNotContain((await _ctx.Revisions.TableauAsync()).Cartes, c => c.Etape == EtapeRevision.Fait);
    }

    [Fact]
    public async Task Chaque_etape_est_notee_dans_la_fiche_EPI()
    {
        var det = await ChezPrestataireAsync("A10");

        var fiche = await _ctx.Historique.ObtenirFicheSuiviAsync(det.Id);

        var etape = Assert.Single(fiche!.Interventions, i => i.Type == TypeInterventionSuivie.Etape);
        Assert.Equal("À envoyer chez Magasin A10 (Dépôt prévu le 15/11)", etape.Nature);
        Assert.Contains(fiche.Interventions, i => i.Type == TypeInterventionSuivie.Atelier); // Le départ.
    }

    // Cartes : la précision du problème signalé suit l'équipement d'étape en étape ; « Fait » montre la description saisie.
    [Fact]
    public async Task Les_cartes_montrent_la_precision_du_signalement_et_la_description_de_l_intervention()
    {
        var det = Detendeur(jours: 400);
        Assert.True((await _ctx.Signalements.SignalerAsync(new NouveauSignalement(det.Id, null, "Fuite", GraviteSignalement.ASurveiller, "Bulles au premier étage"))).Reussi);

        var signale = await CarteAsync("A10");
        Assert.Equal((EtapeRevision.APrevoir, "Fuite", "Bulles au premier étage"), (signale.Etape, signale.Controle, signale.Precision));
        Assert.DoesNotContain("Bulles", signale.Detail, StringComparison.Ordinal); // Plus dans la ligne de détail.

        Assert.True((await _ctx.Revisions.ReparerAuClubAsync(det.Id)).Reussi);
        Assert.Equal("Bulles au premier étage", (await CarteAsync("A10")).Precision);

        var trop = await _ctx.Revisions.EnregistrerInterventionAsync(new Intervention(det.Id, TypeControle.Revision, ResultatControle.Conforme, Aujourdhui,
            "Réparation", "Paul", Notes: new string('x', ServiceRevisions.LongueurDescription + 1)));
        Assert.Equal($"Description trop longue : {ServiceRevisions.LongueurDescription} caractères au plus.", trop.Message);

        Assert.True((await _ctx.Revisions.EnregistrerInterventionAsync(new Intervention(det.Id, TypeControle.Revision, ResultatControle.Conforme, Aujourdhui,
            "Réparation", "Paul", Notes: " Joint torique du premier étage changé "))).Reussi);
        var fait = (await _ctx.Revisions.TableauAsync()).Cartes.Single(c => c.Code == "A10");
        Assert.Equal((EtapeRevision.Fait, "Joint torique du premier étage changé", ""), (fait.Etape, fait.Description, fait.Precision));
    }

    // Intervention prévue à la main : on voit un problème, il faudra intervenir. Elle entre dans « À prévoir », suit les étapes,
    // et la prochaine intervention enregistrée la ferme.
    [Fact]
    public async Task Une_intervention_prevue_entre_dans_a_prevoir_et_se_ferme_a_l_intervention()
    {
        var det = Detendeur("A10", jours: 400); // Rien de dû.
        Assert.DoesNotContain((await _ctx.Revisions.TableauAsync()).Cartes, c => c.Code == "A10");

        Assert.Equal("Indiquez ce qu'il faudra faire.", (await _ctx.Revisions.PrevoirInterventionAsync(det.Id, " ", null)).Message);
        Assert.False((await _ctx.Revisions.PrevoirInterventionAsync(det.Id, new string('x', ServiceRevisions.LongueurMotif + 1), null)).Reussi);
        var r = await _ctx.Revisions.PrevoirInterventionAsync(det.Id, " Changer le joint ", "Petite fuite au premier étage");
        Assert.Equal("A10 ajouté à « À prévoir ».", r.Message);

        var carte = await CarteAsync("A10");
        Assert.Equal((EtapeRevision.APrevoir, true, "Changer le joint", "Petite fuite au premier étage", "Prévue"),
            (carte.Etape, carte.Prevue, carte.Controle, carte.Description, carte.Badge));

        // La prévoir de nouveau la modifie (une seule ouverte).
        Assert.Equal("A10 : intervention prévue modifiée.", (await _ctx.Revisions.PrevoirInterventionAsync(det.Id, "Changer les joints", "")).Message);
        Assert.Single((await _ctx.Revisions.TableauAsync()).Cartes, c => c.Code == "A10");

        // Elle suit l'équipement : réparation au club (motif repris), puis l'intervention la ferme.
        Assert.True((await _ctx.Revisions.ReparerAuClubAsync(det.Id, carte.Controle)).Reussi);
        Assert.Equal((EtapeRevision.Interne, "Changer le joint", true), ((await CarteAsync("A10")).Etape, (await CarteAsync("A10")).Controle, (await CarteAsync("A10")).Prevue));
        Assert.False((await _ctx.Revisions.PrevoirInterventionAsync(det.Id, "Autre", null)).Reussi); // Déjà en révision.
        Assert.True((await _ctx.Revisions.EnregistrerInterventionAsync(new Intervention(det.Id, TypeControle.Revision, ResultatControle.Conforme, Aujourdhui, "Réparation", "Paul"))).Reussi);
        Assert.Null(await _ctx.DepotRevisions.ObtenirPrevueOuverteAsync(det.Id));
        Assert.DoesNotContain((await _ctx.Revisions.TableauAsync()).Cartes, c => c.Code == "A10" && c.Etape != EtapeRevision.Fait);
    }

    [Fact]
    public async Task Une_intervention_prevue_se_retire_et_rejoint_la_carte_d_une_echeance()
    {
        var det = Detendeur("A10", jours: -3); // Révision dépassée : déjà dans « À prévoir ».
        Assert.True((await _ctx.Revisions.PrevoirInterventionAsync(det.Id, "Changer le joint", "")).Reussi);

        var carte = Assert.Single((await _ctx.Revisions.TableauAsync()).Cartes, c => c.Code == "A10");
        Assert.True(carte.Prevue);
        Assert.Equal("Changer le joint", carte.Controle);
        Assert.Equal(EtatCarte.EnRetard, carte.Etat); // L'échéance garde sa pastille.

        Assert.True((await _ctx.Revisions.RetirerInterventionPrevueAsync(det.Id)).Reussi);
        Assert.False((await CarteAsync("A10")).Prevue);
        Assert.False((await _ctx.Revisions.RetirerInterventionPrevueAsync(det.Id)).Reussi);

        // Mode gestion : prévoir demande le code.
        Assert.True(_ctx.Gestion.DefinirCode(null, "1234", "1234").Reussi);
        _ctx.Gestion.Verrouiller();
        Assert.False((await _ctx.Revisions.PrevoirInterventionAsync(det.Id, "Changer le joint", "")).Reussi);
    }

    // Une carte n'affiche « Au club » (et ne compte au club) que si l'équipement n'est ni en sortie, ni manquant.
    [Fact]
    public async Task Un_equipement_en_sortie_n_est_pas_au_club()
    {
        Detendeur("A10", jours: -3);
        await _ctx.SortieEnCoursAsync("Week-end Marseille", "A10");

        var carte = await CarteAsync("A10");
        Assert.Equal(("En sortie · Week-end Marseille", false), (carte.HorsClub, carte.AuClub));
        var tableau = await _ctx.Revisions.TableauAsync();
        Assert.Equal((0, 1), (tableau.AuClub, tableau.EnSortie));

        Detendeur("A11", jours: -3);
        Assert.Equal(("", true), ((await CarteAsync("A11")).HorsClub, (await CarteAsync("A11")).AuClub));
    }
}
