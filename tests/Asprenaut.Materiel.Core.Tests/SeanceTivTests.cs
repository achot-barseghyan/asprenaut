using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Règle 39 : séance TIV configurable (inspecteurs choisis, modification, annulation, affichage).</summary>
public class SeanceTivTests
{
    // Contexte.Maintenant = 6 novembre 2026.
    private static readonly DateOnly Aujourdhui = new(2026, 11, 6);
    private readonly Contexte _ctx = new();
    private readonly Moniteur _marc;
    private readonly Moniteur _julie;

    public SeanceTivTests()
    {
        _marc = new Moniteur { Id = _ctx.Db.ProchainId(), Prenom = "Marc", Nom = "Tiv", InspecteurTiv = true, Actif = true };
        _julie = new Moniteur { Id = _ctx.Db.ProchainId(), Prenom = "Julie", Nom = "Bernard", InspecteurTiv = true, Actif = true };
        _ctx.Db.Moniteurs.Add(_marc);
        _ctx.Db.Moniteurs.Add(_julie);
    }

    private Task<Asprenaut.Materiel.Core.Resultats.ResultatOperation> PlanifierAsync(params long[] inspecteurs) =>
        _ctx.Revisions.PlanifierSeanceAsync(new SaisieSeance(Aujourdhui.AddDays(10), "9:00", "Local du club", inspecteurs, "Blocs vides"));

    [Fact]
    public async Task Les_inspecteurs_affiches_sont_ceux_choisis()
    {
        Assert.True((await PlanifierAsync(_julie.Id)).Reussi);

        var seance = await _ctx.Revisions.ProchaineSeanceAsync();

        Assert.NotNull(seance);
        Assert.Equal(["Julie Bernard"], seance.Inspecteurs);
        Assert.Equal("Blocs vides", seance.Seance.Note);
    }

    [Fact]
    public async Task Ancienne_planification_prend_tous_les_inspecteurs_actifs()
    {
        Assert.True((await _ctx.Revisions.PlanifierSeanceAsync(Aujourdhui.AddDays(10), "9:00", "Club")).Reussi);

        Assert.Equal(["Julie Bernard", "Marc Tiv"], (await _ctx.Revisions.ProchaineSeanceAsync())!.Inspecteurs.Order());
    }

    [Fact]
    public async Task Un_moniteur_non_inspecteur_est_refuse()
    {
        var r = await PlanifierAsync(_ctx.Pascal.Id);

        Assert.False(r.Reussi);
        Assert.Null(await _ctx.Revisions.ProchaineSeanceAsync());
    }

    [Fact]
    public async Task Modifier_change_date_heure_lieu_note_et_inspecteurs()
    {
        var id = (await PlanifierAsync(_marc.Id)).Id!.Value;

        Assert.False((await _ctx.Revisions.ModifierSeanceAsync(id, new SaisieSeance(Aujourdhui.AddDays(-1), "", "", []))).Reussi);
        var r = await _ctx.Revisions.ModifierSeanceAsync(id, new SaisieSeance(Aujourdhui.AddDays(20), " 14:00 ", "Piscine", [_julie.Id], ""));

        Assert.True(r.Reussi);
        var seance = (await _ctx.Revisions.ProchaineSeanceAsync())!;
        Assert.Equal((Aujourdhui.AddDays(20), "14:00", "Piscine", ""), (seance.Seance.DateSeance, seance.Seance.Heure, seance.Seance.Lieu, seance.Seance.Note));
        Assert.Equal(["Julie Bernard"], seance.Inspecteurs);
        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.SeanceTivModifiee);
    }

    [Fact]
    public async Task Un_inspecteur_deja_choisi_reste_accepte_s_il_ne_l_est_plus()
    {
        var id = (await PlanifierAsync(_marc.Id)).Id!.Value;
        _marc.InspecteurTiv = false;

        Assert.True((await _ctx.Revisions.ModifierSeanceAsync(id, new SaisieSeance(Aujourdhui.AddDays(12), "9:00", "Club", [_marc.Id]))).Reussi);
    }

    [Fact]
    public async Task Annuler_la_seance_renvoie_ses_blocs_dans_a_prevoir()
    {
        var bloc = _ctx.Equipement("BLOC-031", revision: Aujourdhui.AddDays(20));
        var id = (await PlanifierAsync(_marc.Id)).Id!.Value;
        Assert.True((await _ctx.Revisions.AjouterASeanceAsync(bloc.Id)).Reussi);
        Assert.Equal(EtapeRevision.Interne, (await _ctx.Revisions.TableauAsync()).Cartes.Single(c => c.Code == "BLOC-031").Etape);

        var r = await _ctx.Revisions.AnnulerSeanceAsync(id);

        Assert.True(r.Reussi);
        Assert.Null(await _ctx.Revisions.ProchaineSeanceAsync());
        Assert.Equal(EtapeRevision.APrevoir, (await _ctx.Revisions.TableauAsync()).Cartes.Single(c => c.Code == "BLOC-031").Etape);
        Assert.False((await _ctx.Revisions.AnnulerSeanceAsync(id)).Reussi); // Déjà annulée.
        Assert.False((await _ctx.Revisions.ModifierSeanceAsync(id, new SaisieSeance(Aujourdhui.AddDays(12), "", "", []))).Reussi);
    }

    [Fact]
    public async Task Planifier_modifier_et_annuler_demandent_le_mode_gestion()
    {
        var id = (await PlanifierAsync(_marc.Id)).Id!.Value;
        Assert.True(_ctx.Gestion.DefinirCode(null, "1234", "1234").Reussi);
        _ctx.Gestion.Verrouiller();

        Assert.False((await PlanifierAsync(_marc.Id)).Reussi);
        Assert.False((await _ctx.Revisions.ModifierSeanceAsync(id, new SaisieSeance(Aujourdhui.AddDays(12), "", "", []))).Reussi);
        Assert.False((await _ctx.Revisions.AnnulerSeanceAsync(id)).Reussi);
    }

    [Fact]
    public void La_seance_est_affichee_par_defaut() => Assert.True(new Reglages().AfficherSeanceTiv);
}
