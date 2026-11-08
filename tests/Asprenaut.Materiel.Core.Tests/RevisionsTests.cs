using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Révisions : échéancier, enregistrement d'un contrôle, atelier, séances TIV.</summary>
public class RevisionsTests
{
    // Contexte.Maintenant = 6 novembre 2026.
    private static readonly DateOnly Aujourdhui = new(2026, 11, 6);
    private readonly Contexte _ctx = new();
    private readonly Moniteur _inspecteur;

    public RevisionsTests()
    {
        _inspecteur = new Moniteur { Id = _ctx.Db.ProchainId(), Prenom = "Marc", Nom = "Tiv", InspecteurTiv = true, Actif = true };
        _ctx.Db.Moniteurs.Add(_inspecteur);
    }

    // --- Échéancier ---

    [Theory]
    [InlineData(-1, EtatEcheance.EnRetard)]
    [InlineData(0, EtatEcheance.CeMois)]
    [InlineData(24, EtatEcheance.CeMois)]   // 30 novembre
    [InlineData(25, EtatEcheance.Bientot)]  // 1er décembre
    [InlineData(90, EtatEcheance.Bientot)]
    [InlineData(91, EtatEcheance.AJour)]
    public void Etat_d_une_echeance(int jours, EtatEcheance attendu) =>
        Assert.Equal(attendu, _ctx.Revisions.EtatDe(Aujourdhui.AddDays(jours)));

    [Fact]
    public async Task Echeancier_bloc_a_TIV_et_requalification_le_reste_une_revision()
    {
        _ctx.Equipement("BLOC-031", revision: Aujourdhui.AddDays(10), requalification: Aujourdhui.AddDays(400));
        _ctx.Equipement("DET-001", revision: Aujourdhui.AddDays(-5), categorie: "Détendeurs");
        _ctx.Equipement("BLOC-099", EtatEquipement.Reforme, revision: Aujourdhui.AddDays(-100));

        var lignes = await _ctx.Revisions.ListerEcheancesAsync();

        Assert.Equal(
            [("DET-001", TypeControle.Revision, "Révision fabricant", EtatEcheance.EnRetard),
             ("BLOC-031", TypeControle.Tiv, "TIV annuelle", EtatEcheance.CeMois),
             ("BLOC-031", TypeControle.Requalification, "Requalification", EtatEcheance.AJour)],
            lignes.Select(l => (l.Code, l.Type, l.Controle, l.Etat)).ToArray());
    }

    // --- Règle 9 : la requalification dépassée compte comme une révision dépassée ---

    [Fact]
    public async Task Requalification_depassee_au_depart_donne_un_avertissement()
    {
        _ctx.Equipement("BLOC-031", revision: Aujourdhui.AddDays(100), requalification: Aujourdhui.AddDays(-3));
        var sortie = await _ctx.SortieAsync();

        var r = await _ctx.Scan.ScannerDepartAsync(sortie, "BLOC-031");

        Assert.Equal(NiveauResultat.Avertissement, r.Niveau);
        Assert.Contains("03/11/2026", r.Message);
    }

    // --- Enregistrer un contrôle ---

    [Fact]
    public async Task TIV_conforme_repousse_la_TIV_de_12_mois()
    {
        var bloc = _ctx.Equipement("BLOC-031", revision: Aujourdhui.AddDays(-2));

        var r = await _ctx.Revisions.EnregistrerControleAsync(bloc.Id, TypeControle.Tiv, ResultatControle.Conforme, Aujourdhui, _inspecteur.Id);

        Assert.True(r.Reussi, r.Message);
        Assert.Equal(new DateOnly(2027, 11, 6), bloc.DateProchaineRevision);
        var c = Assert.Single(_ctx.DepotRevisions.Controles);
        Assert.Equal((TypeControle.Tiv, ResultatControle.Conforme, _inspecteur.Id), (c.Type, c.Resultat, c.InspecteurId));
        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.ControleEnregistre);
    }

    [Fact]
    public async Task Requalification_conforme_repousse_requalification_de_6_ans_et_TIV_de_12_mois()
    {
        var bloc = _ctx.Equipement("BLOC-031", revision: Aujourdhui.AddDays(40), requalification: Aujourdhui.AddDays(-1));

        Assert.True((await _ctx.Revisions.EnregistrerControleAsync(bloc.Id, TypeControle.Requalification, ResultatControle.Conforme, Aujourdhui, null)).Reussi);

        Assert.Equal(new DateOnly(2032, 11, 6), bloc.DateRequalification);
        Assert.Equal(new DateOnly(2027, 11, 6), bloc.DateProchaineRevision);
    }

    [Fact]
    public async Task A_surveiller_avance_le_prochain_controle_a_6_mois()
    {
        var det = _ctx.Equipement("DET-001", revision: Aujourdhui, categorie: "Détendeurs");

        Assert.True((await _ctx.Revisions.EnregistrerControleAsync(det.Id, TypeControle.Revision, ResultatControle.ASurveiller, Aujourdhui, null)).Reussi);

        Assert.Equal(new DateOnly(2027, 5, 6), det.DateProchaineRevision);
        Assert.Equal(EtatEquipement.EnService, det.Etat);
    }

    [Fact]
    public async Task Refuse_passe_l_equipement_hors_service_et_il_ne_part_plus()
    {
        var bloc = _ctx.Equipement("BLOC-031", revision: Aujourdhui);

        Assert.True((await _ctx.Revisions.EnregistrerControleAsync(bloc.Id, TypeControle.Tiv, ResultatControle.Refuse, Aujourdhui, _inspecteur.Id)).Reussi);

        Assert.Equal(EtatEquipement.HorsService, bloc.Etat);
        var sortie = await _ctx.SortieAsync();
        Assert.Equal(NiveauResultat.Erreur, (await _ctx.Scan.ScannerDepartAsync(sortie, "BLOC-031")).Niveau);
    }

    [Fact]
    public async Task Controles_refuses_si_la_saisie_est_incoherente()
    {
        var bloc = _ctx.Equipement("BLOC-031", revision: Aujourdhui);
        var det = _ctx.Equipement("DET-001", revision: Aujourdhui, categorie: "Détendeurs");

        // TIV sans inspecteur habilité.
        Assert.False((await _ctx.Revisions.EnregistrerControleAsync(bloc.Id, TypeControle.Tiv, ResultatControle.Conforme, Aujourdhui, null)).Reussi);
        Assert.False((await _ctx.Revisions.EnregistrerControleAsync(bloc.Id, TypeControle.Tiv, ResultatControle.Conforme, Aujourdhui, _ctx.Pascal.Id)).Reussi);

        // Date future, type qui ne correspond pas à la catégorie.
        Assert.False((await _ctx.Revisions.EnregistrerControleAsync(bloc.Id, TypeControle.Tiv, ResultatControle.Conforme, Aujourdhui.AddDays(1), _inspecteur.Id)).Reussi);
        Assert.False((await _ctx.Revisions.EnregistrerControleAsync(det.Id, TypeControle.Tiv, ResultatControle.Conforme, Aujourdhui, _inspecteur.Id)).Reussi);
        Assert.False((await _ctx.Revisions.EnregistrerControleAsync(bloc.Id, TypeControle.Revision, ResultatControle.Conforme, Aujourdhui, null)).Reussi);

        Assert.Empty(_ctx.DepotRevisions.Controles);
    }

    [Fact]
    public async Task Refus_impossible_tant_que_l_equipement_est_dehors()
    {
        var bloc = _ctx.Equipement("BLOC-031", revision: Aujourdhui.AddDays(30));
        await _ctx.SortieEnCoursAsync("Mer", "BLOC-031");

        var r = await _ctx.Revisions.EnregistrerControleAsync(bloc.Id, TypeControle.Tiv, ResultatControle.Refuse, Aujourdhui, _inspecteur.Id);

        Assert.False(r.Reussi);
        Assert.Equal(EtatEquipement.EnService, bloc.Etat);
    }

    [Fact]
    public async Task Preparation_propose_le_controle_le_plus_urgent()
    {
        _ctx.Equipement("BLOC-031", revision: Aujourdhui.AddDays(200), requalification: Aujourdhui.AddDays(20));
        _ctx.Equipement("DET-001", revision: Aujourdhui, categorie: "Détendeurs");

        var bloc = await _ctx.Revisions.PreparerControleAsync("asp:bloc-031");
        var det = await _ctx.Revisions.PreparerControleAsync("DET-001");

        Assert.Equal(TypeControle.Requalification, bloc?.TypePropose);
        Assert.Equal([TypeControle.Tiv, TypeControle.Requalification], bloc?.TypesPossibles);
        Assert.Equal(TypeControle.Revision, det?.TypePropose);
        Assert.Null(await _ctx.Revisions.PreparerControleAsync("INCONNU-1"));
    }

    // --- Atelier ---

    [Fact]
    public async Task Atelier_hors_service_pendant_le_passage_puis_de_nouveau_en_service()
    {
        var det = _ctx.Equipement("DET-001", revision: Aujourdhui, categorie: "Détendeurs");

        var envoi = await _ctx.Revisions.EnvoyerEnAtelierAsync("det-001", "Magasin Plongée", "Révision", Aujourdhui.AddDays(9));
        Assert.True(envoi.Reussi, envoi.Message);
        Assert.Equal(EtatEquipement.HorsService, det.Etat);
        Assert.Single(await _ctx.Revisions.ListerAtelierAsync());

        Assert.False((await _ctx.Revisions.EnvoyerEnAtelierAsync("DET-001", "Autre", "", null)).Reussi); // Déjà en atelier.

        Assert.True((await _ctx.Revisions.RetourAtelierAsync(envoi.Id!.Value)).Reussi);
        Assert.Equal(EtatEquipement.EnService, det.Etat);
        Assert.Empty(await _ctx.Revisions.ListerAtelierAsync());
        Assert.False((await _ctx.Revisions.RetourAtelierAsync(envoi.Id!.Value)).Reussi);
    }

    [Fact]
    public async Task Atelier_refuse_le_materiel_dehors_ou_sans_prestataire()
    {
        _ctx.Equipement("BLOC-031", revision: Aujourdhui.AddDays(30));
        await _ctx.SortieEnCoursAsync("Mer", "BLOC-031");

        Assert.False((await _ctx.Revisions.EnvoyerEnAtelierAsync("BLOC-031", "Magasin", "", null)).Reussi);
        _ctx.Equipement("DET-001", categorie: "Détendeurs");
        Assert.False((await _ctx.Revisions.EnvoyerEnAtelierAsync("DET-001", "  ", "", null)).Reussi);
        Assert.False((await _ctx.Revisions.EnvoyerEnAtelierAsync("DET-001", "Magasin", "", Aujourdhui.AddDays(-1))).Reussi);
    }

    // --- Séance TIV ---

    [Fact]
    public async Task Prochaine_seance_avec_inspecteurs_et_blocs_a_preparer()
    {
        _ctx.Equipement("BLOC-031", revision: Aujourdhui.AddDays(-3));
        _ctx.Equipement("BLOC-032", revision: Aujourdhui.AddDays(40));   // 26 jours après la séance : à préparer.
        _ctx.Equipement("BLOC-033", revision: Aujourdhui.AddDays(200));  // trop loin.

        Assert.False((await _ctx.Revisions.PlanifierSeanceAsync(Aujourdhui.AddDays(-1), "9:00", "Club")).Reussi);
        Assert.True((await _ctx.Revisions.PlanifierSeanceAsync(Aujourdhui.AddDays(14), "9:00", "Local du club")).Reussi);

        var seance = await _ctx.Revisions.ProchaineSeanceAsync();

        Assert.NotNull(seance);
        Assert.Equal(["Marc Tiv"], seance.Inspecteurs);
        Assert.Equal(["BLOC-031", "BLOC-032"], seance.BlocsAPreparer.Select(b => b.Code));
    }
}
