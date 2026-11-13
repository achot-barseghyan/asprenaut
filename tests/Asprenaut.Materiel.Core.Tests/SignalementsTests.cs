using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Problèmes signalés au retour d'une sortie ou depuis l'inventaire (règle 28).</summary>
public class SignalementsTests
{
    private readonly Contexte _ctx = new();
    private static readonly DateOnly Aujourdhui = DateOnly.FromDateTime(Contexte.Maintenant);

    private Equipement Detendeur(string code = "A07") => _ctx.Equipement(code, categorie: "Détendeurs");

    private Task<Resultats.ResultatOperation> SignalerAsync(Equipement e, GraviteSignalement gravite = GraviteSignalement.ASurveiller, long? sortie = null, string cause = "Fuite / bulles", string? precision = null) =>
        _ctx.Signalements.SignalerAsync(new NouveauSignalement(e.Id, sortie, cause, gravite, precision));

    [Fact]
    public void Les_choix_dependent_de_la_categorie_et_finissent_par_autre()
    {
        Assert.Equal(["Fuite / bulles", "Débit continu", "Respiration dure", "Embout abîmé", "Flexible abîmé", "Autre"], ServiceSignalements.CausesPour("Détendeurs"));
        Assert.Contains("Inflateur qui colle", ServiceSignalements.CausesPour("Gilets"));
        Assert.Contains("Robinet qui fuit", ServiceSignalements.CausesPour("Blocs"));
        Assert.Contains("Voilure fendue", ServiceSignalements.CausesPour("Palmes"));
        Assert.Equal("Autre", ServiceSignalements.CausesPour("Lampes")[^1]);
    }

    // Règle 34 : tuiles réglables par catégorie dans Paramètres ; une tuile peut rendre la précision obligatoire.
    [Fact]
    public async Task Les_tuiles_se_reglent_par_categorie_et_peuvent_exiger_une_precision()
    {
        var a07 = Detendeur();
        var tuiles = new List<CauseSignalement>
        {
            new() { Libelle = "Fuite / bulles", Pictogramme = "Goutte" },
            new() { Libelle = "Manomètre HS", Pictogramme = "Ecran" },
            new() { Libelle = "Autre", Pictogramme = "Autre", PrecisionObligatoire = true },
        };

        Assert.True(_ctx.ServiceReglages.ChangerTuilesSignalement("détendeurs", tuiles).Reussi);

        Assert.Equal(["Fuite / bulles", "Manomètre HS", "Autre"], _ctx.Signalements.Tuiles("Détendeurs").Select(t => t.Libelle));
        Assert.Equal("Autre", ServiceSignalements.CausesPour("Gilets", _ctx.Reglages)[^1]); // Les autres catégories gardent celles d'origine.
        Assert.True(_ctx.Stockage.Enregistrements > 0);

        var refus = await SignalerAsync(a07, cause: "Autre", precision: "  ");
        Assert.Equal("Précisez le problème : « Autre » demande une précision.", refus.Message);
        Assert.Empty(_ctx.DepotSignalements.Signalements);
        Assert.True((await SignalerAsync(a07, cause: "Autre", precision: "Purge du 2e étage")).Reussi);
        Assert.True((await SignalerAsync(a07, cause: "Manomètre HS")).Reussi); // Précision facultative.

        Assert.True(_ctx.ServiceReglages.RetablirTuilesSignalement("Détendeurs").Reussi);
        Assert.Equal(ServiceSignalements.CausesPour("Détendeurs"), _ctx.Signalements.Tuiles("Détendeurs").Select(t => t.Libelle));
    }

    [Fact]
    public void Les_tuiles_sont_verifiees_et_le_reglage_est_protege()
    {
        CauseSignalement T(string libelle) => new() { Libelle = libelle };
        Assert.Equal("Gardez au moins une tuile.", _ctx.ServiceReglages.ChangerTuilesSignalement("Gilets", []).Message);
        Assert.Equal("Donnez un nom à chaque tuile.", _ctx.ServiceReglages.ChangerTuilesSignalement("Gilets", [T(" ")]).Message);
        Assert.Equal("« fuite » existe déjà.", _ctx.ServiceReglages.ChangerTuilesSignalement("Gilets", [T("fuite"), T("Fuite")]).Message);
        Assert.False(_ctx.ServiceReglages.ChangerTuilesSignalement("Gilets", Enumerable.Range(1, 13).Select(i => T($"T{i}")).ToList()).Reussi);
        Assert.False(_ctx.ServiceReglages.ChangerTuilesSignalement("Gilets", [T(new string('x', 61))]).Reussi);

        Assert.True(_ctx.Gestion.DefinirCode(null, "1234", "1234").Reussi);
        _ctx.Gestion.Verrouiller();
        Assert.False(_ctx.ServiceReglages.ChangerTuilesSignalement("Gilets", [T("Fuite")]).Reussi);
        Assert.Empty(_ctx.Reglages.CausesSignalement);
    }

    [Fact]
    public async Task A_surveiller_est_libre_garde_l_equipement_en_service_et_note_la_sortie()
    {
        Assert.True(_ctx.Gestion.DefinirCode(null, "1234", "1234").Reussi);
        _ctx.Gestion.Verrouiller();
        var a07 = Detendeur();
        var sortie = await _ctx.SortieEnCoursAsync("Cours en piscine", "A07");
        await _ctx.Scan.ScannerRetourAsync(sortie, "A07");

        var r = await SignalerAsync(a07, sortie: sortie, precision: "  fuite au raccord MP ");

        Assert.True(r.Reussi, r.Message);
        Assert.Equal(EtatEquipement.EnService, a07.Etat);
        var s = Assert.Single(_ctx.DepotSignalements.Signalements);
        Assert.Equal((sortie, "Fuite / bulles", "fuite au raccord MP", true), (s.SortieId!.Value, s.Cause, s.Precision, s.Ouvert));
        var entree = _ctx.Db.Journal.Last();
        Assert.Equal((ActionsJournal.ProblemeSignale, a07.Id, sortie), (entree.Action, entree.EquipementId!.Value, entree.SortieId!.Value));
    }

    [Fact]
    public async Task Sans_cause_ou_sur_un_reforme_c_est_refuse()
    {
        Assert.False((await SignalerAsync(Detendeur(), cause: "  ")).Reussi);
        Assert.False((await SignalerAsync(_ctx.Equipement("A09", EtatEquipement.Reforme, categorie: "Détendeurs"))).Reussi);
        Assert.Empty(_ctx.DepotSignalements.Signalements);
    }

    [Fact]
    public async Task Hors_service_demande_le_code_et_bloque_les_departs()
    {
        Assert.True(_ctx.Gestion.DefinirCode(null, "1234", "1234").Reussi);
        _ctx.Gestion.Verrouiller();
        var a07 = Detendeur();

        var refus = await SignalerAsync(a07, GraviteSignalement.HorsService);
        Assert.False(refus.Reussi);
        Assert.Equal(EtatEquipement.EnService, a07.Etat);

        Assert.True(_ctx.Gestion.Deverrouiller("1234").Reussi);
        Assert.True((await SignalerAsync(a07, GraviteSignalement.HorsService)).Reussi);
        Assert.Equal(EtatEquipement.HorsService, a07.Etat);
        Assert.True(_ctx.DepotSignalements.Signalements.Single().MisHorsService);

        var depart = await _ctx.Scan.ScannerDepartAsync(await _ctx.SortieAsync("Fosse"), "A07");
        Assert.Equal(Resultats.NiveauResultat.Erreur, depart.Niveau);
    }

    [Fact]
    public async Task Hors_service_refuse_si_l_equipement_est_dehors()
    {
        var a07 = Detendeur();
        await _ctx.SortieEnCoursAsync("Mer", "A07");

        Assert.False((await SignalerAsync(a07, GraviteSignalement.HorsService)).Reussi);
        Assert.True((await SignalerAsync(a07)).Reussi); // « À surveiller » reste possible.
    }

    [Fact]
    public async Task Signaler_de_nouveau_modifie_le_signalement_ouvert_et_redescendre_remet_en_service()
    {
        var a07 = Detendeur();
        Assert.True((await SignalerAsync(a07, GraviteSignalement.HorsService)).Reussi);

        var r = await SignalerAsync(a07, GraviteSignalement.ASurveiller, cause: "Débit continu");

        Assert.True(r.Reussi, r.Message);
        var s = Assert.Single(_ctx.DepotSignalements.Signalements);
        Assert.Equal(("Débit continu", GraviteSignalement.ASurveiller, false), (s.Cause, s.Gravite, s.MisHorsService));
        Assert.Equal(EtatEquipement.EnService, a07.Etat);
    }

    [Fact]
    public async Task Redescendre_ne_remet_pas_en_service_un_equipement_arrete_par_autre_chose()
    {
        var a07 = _ctx.Equipement("A07", EtatEquipement.HorsService, categorie: "Détendeurs"); // Refusé à un contrôle.
        Assert.True((await SignalerAsync(a07, GraviteSignalement.HorsService)).Reussi);
        Assert.False(_ctx.DepotSignalements.Signalements.Single().MisHorsService);

        Assert.True((await SignalerAsync(a07, GraviteSignalement.ASurveiller)).Reussi);

        Assert.Equal(EtatEquipement.HorsService, a07.Etat);
    }

    [Fact]
    public async Task Le_signalement_arrive_en_tete_de_a_prevoir_meme_hors_service()
    {
        var a07 = Detendeur();
        Assert.True((await SignalerAsync(a07, GraviteSignalement.HorsService, precision: "flexible MP")).Reussi);

        var tableau = await _ctx.Revisions.TableauAsync();

        var carte = tableau.Cartes.First();
        Assert.Equal((EtapeRevision.APrevoir, a07.Id, "Fuite / bulles", EtatCarte.HorsService, true, true),
            (carte.Etape, carte.EquipementId, carte.Controle, carte.Etat, carte.Bloque, carte.Signalement));
        Assert.Contains("flexible MP", carte.Precision); // À part, sous la ligne de détail.
        Assert.Equal(ServiceRevisions.FamilleSignalement, tableau.Familles[0]);
    }

    [Fact]
    public async Task Une_intervention_traite_le_signalement_et_remet_en_service()
    {
        var a07 = Detendeur();
        Assert.True((await SignalerAsync(a07, GraviteSignalement.HorsService)).Reussi);

        var r = await _ctx.Revisions.EnregistrerControleAsync(a07.Id, TypeControle.Revision, ResultatControle.Conforme, Aujourdhui, null, "Réparation", "Magasin");

        Assert.True(r.Reussi, r.Message);
        Assert.False(_ctx.DepotSignalements.Signalements.Single().Ouvert);
        Assert.Equal(EtatEquipement.EnService, a07.Etat);
        Assert.DoesNotContain((await _ctx.Revisions.TableauAsync()).Cartes, c => c.Signalement);
    }

    [Fact]
    public async Task Une_intervention_refusee_traite_le_signalement_mais_laisse_hors_service()
    {
        var a07 = Detendeur();
        Assert.True((await SignalerAsync(a07, GraviteSignalement.HorsService)).Reussi);

        Assert.True((await _ctx.Revisions.EnregistrerControleAsync(a07.Id, TypeControle.Revision, ResultatControle.Refuse, Aujourdhui, null)).Reussi);

        Assert.False(_ctx.DepotSignalements.Signalements.Single().Ouvert);
        Assert.Equal(EtatEquipement.HorsService, a07.Etat);
    }

    [Fact]
    public async Task La_fiche_de_suivi_montre_le_signalement_avec_le_responsable_de_la_sortie()
    {
        var a07 = Detendeur();
        var sortie = await _ctx.SortieEnCoursAsync("Cours en piscine", "A07");
        await _ctx.Scan.ScannerRetourAsync(sortie, "A07");
        Assert.True((await SignalerAsync(a07, sortie: sortie, precision: "flexible MP")).Reussi);

        var fiche = await _ctx.Historique.ObtenirFicheSuiviAsync(a07.Id);

        var ligne = Assert.Single(fiche!.Interventions);
        Assert.Equal((TypeInterventionSuivie.Signalement, "Problème signalé : Fuite / bulles : flexible MP", "Pascal Martin", "À surveiller", ResultatControle.ASurveiller),
            (ligne.Type, ligne.Nature, ligne.Intervenant, ligne.Resultat, ligne.ResultatBrut!.Value));
        Assert.Equal(TypeEvenement.Revision, ServiceHistorique.TypeDe(ActionsJournal.ProblemeSignale));
    }
}
