using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Tableau des révisions : À prévoir → Réparation interne → À envoyer → Chez le prestataire → Fait.</summary>
public class TableauRevisionsTests
{
    private readonly Contexte _ctx = new();
    private static readonly DateOnly Aujourdhui = DateOnly.FromDateTime(Contexte.Maintenant);

    public TableauRevisionsTests()
    {
        _ctx.Pascal.InspecteurTiv = true;
    }

    private async Task<TableauRevisions> TableauAsync() => await _ctx.Revisions.TableauAsync();

    private async Task PlanifierSeanceAsync() =>
        Assert.True((await _ctx.Revisions.PlanifierSeanceAsync(Aujourdhui.AddDays(10), "9:00", "Local du club")).Reussi);

    // Règle : « À prévoir » reçoit les échéances à 90 jours ou dépassées ; un équipement à jour n'apparaît pas.
    [Fact]
    public async Task A_prevoir_montre_ce_qui_arrive_a_echeance()
    {
        _ctx.Equipement("BLOC-031", revision: Aujourdhui.AddDays(-5));
        _ctx.Equipement("BLOC-032", revision: Aujourdhui.AddDays(60));
        _ctx.Equipement("BLOC-033", revision: Aujourdhui.AddDays(400));

        var cartes = (await TableauAsync()).Cartes;

        Assert.Equal(["BLOC-031", "BLOC-032"], cartes.Where(c => c.Etape == EtapeRevision.APrevoir).Select(c => c.Code));
        var retard = cartes.First();
        Assert.Equal(("En retard", "TIV", "TIV annuelle", false), (retard.Badge, retard.Famille, retard.Controle, retard.Bloque));
        Assert.StartsWith("Échue en", retard.Detail, StringComparison.Ordinal);
    }

    // Règle : « Bloqué pour les sorties » suit les réglages (départ refusé si révision dépassée, blocage au seuil).
    [Fact]
    public async Task Bloque_seulement_si_les_reglages_le_disent()
    {
        _ctx.Equipement("BLOC-031", revision: Aujourdhui.AddDays(-5));
        _ctx.Reglages.DepartSiRevisionDepassee = false;

        Assert.True((await TableauAsync()).Cartes.Single().Bloque);
    }

    [Fact]
    public async Task La_seance_TIV_fait_passer_le_bloc_en_reparation_interne()
    {
        var bloc = _ctx.Equipement("BLOC-031", revision: Aujourdhui.AddDays(20));
        var det = _ctx.Equipement("DET-001", categorie: "Détendeurs", revision: Aujourdhui.AddDays(20));

        Assert.Equal("Aucune séance TIV prévue : planifiez-en une d'abord.", (await _ctx.Revisions.AjouterASeanceAsync(bloc.Id)).Message);
        await PlanifierSeanceAsync();
        Assert.Equal("Seuls les blocs passent en séance TIV.", (await _ctx.Revisions.AjouterASeanceAsync(det.Id)).Message);
        Assert.True((await _ctx.Revisions.AjouterASeanceAsync(bloc.Id)).Reussi);

        var tableau = await TableauAsync();
        Assert.NotNull(tableau.Seance);
        var interne = tableau.Cartes.Single(c => c.Code == "BLOC-031");
        Assert.Equal((EtapeRevision.Interne, true, $"Séance TIV du {Aujourdhui.AddDays(10).ToString("dd/MM", Libelles.Culture)}"), (interne.Etape, interne.SeanceTiv, interne.Detail));
        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.AjoutSeanceTiv && j.EquipementId == bloc.Id);

        Assert.True((await _ctx.Revisions.RetirerDeSeanceAsync(bloc.Id)).Reussi);
        Assert.Equal(EtapeRevision.APrevoir, (await TableauAsync()).Cartes.Single(c => c.Code == "BLOC-031").Etape);
    }

    // Règle : l'intervention enregistrée sort le bloc de la séance et le montre dans « Fait », avec sa nature et son intervenant.
    [Fact]
    public async Task L_intervention_passe_l_equipement_dans_fait()
    {
        var bloc = _ctx.Equipement("BLOC-031", revision: Aujourdhui.AddDays(20));
        await PlanifierSeanceAsync();
        await _ctx.Revisions.AjouterASeanceAsync(bloc.Id);

        var r = await _ctx.Revisions.EnregistrerInterventionAsync(new Intervention(
            bloc.Id, TypeControle.Tiv, ResultatControle.Conforme, Aujourdhui, "TIV", "Pascal Martin", _ctx.Pascal.Id));

        Assert.True(r.Reussi, r.Message);
        var fait = (await TableauAsync()).Cartes.Single(c => c.Code == "BLOC-031");
        Assert.Equal((EtapeRevision.Fait, "Conforme", "TIV"), (fait.Etape, fait.Badge, fait.Controle));
        Assert.Equal($"Le {Aujourdhui.ToString("dd/MM", Libelles.Culture)} · Pascal Martin · prochaine {Aujourdhui.AddMonths(12).ToString("MM/yyyy", Libelles.Culture)}", fait.Detail);
        Assert.Empty(_ctx.DepotRevisions.Planifies);
        Assert.Equal(("TIV", "Pascal Martin"), (_ctx.DepotRevisions.Controles.Single().Nature, _ctx.DepotRevisions.Controles.Single().Intervenant));
    }

    // Règle : retour d'atelier = intervention ; l'équipement revient en service, sauf refusé.
    [Fact]
    public async Task Le_retour_d_atelier_enregistre_l_intervention_et_remet_en_service()
    {
        var det = _ctx.Equipement("DET-001", categorie: "Détendeurs", revision: Aujourdhui.AddDays(-3));
        var gilet = _ctx.Equipement("DET-002", categorie: "Détendeurs", revision: Aujourdhui.AddDays(-3));
        await _ctx.Revisions.EnvoyerEnAtelierAsync("DET-001", "Aqua Service", "Révision", null);
        await _ctx.Revisions.EnvoyerEnAtelierAsync("DET-002", "Aqua Service", "", null);

        var tableau = await TableauAsync();
        var enAtelier = tableau.Cartes.Single(c => c.Code == "DET-001");
        Assert.Equal((EtapeRevision.ChezPrestataire, "En retard", "Révision"), (enAtelier.Etape, enAtelier.Badge, enAtelier.Controle));
        Assert.Equal(("Aqua Service", Aujourdhui, 0), (enAtelier.Prestataire, enAtelier.DateEnvoi!.Value, enAtelier.JoursDehors!.Value));
        Assert.DoesNotContain(tableau.Cartes, c => c.Etape == EtapeRevision.APrevoir);

        var r = await _ctx.Revisions.EnregistrerInterventionAsync(new Intervention(
            det.Id, TypeControle.Revision, ResultatControle.Conforme, Aujourdhui, "Révision atelier", "Aqua Service", null, enAtelier.PassageAtelierId));
        Assert.True(r.Reussi, r.Message);
        Assert.Equal((EtatEquipement.EnService, Aujourdhui.AddMonths(24)), (det.Etat, det.DateProchaineRevision!.Value));
        Assert.All(_ctx.DepotRevisions.Passages.Where(p => p.EquipementId == det.Id), p => Assert.NotNull(p.RevenuLe));

        var passage2 = (await TableauAsync()).Cartes.Single(c => c.Code == "DET-002").PassageAtelierId;
        await _ctx.Revisions.EnregistrerInterventionAsync(new Intervention(
            gilet.Id, TypeControle.Revision, ResultatControle.Refuse, Aujourdhui, "Réparation", "Aqua Service", null, passage2));
        Assert.Equal(EtatEquipement.HorsService, gilet.Etat);
        Assert.Equal("Hors service", (await TableauAsync()).Cartes.Single(c => c.Code == "DET-002").Badge);
    }

    [Fact]
    public async Task Une_carte_d_utilisation_montre_ce_qui_reste()
    {
        _ctx.Equipement("DET-001", categorie: "Détendeurs");
        _ctx.Reglages.SeuilsSorties["Détendeurs"] = 10;
        for (var i = 0; i < 9; i++)
        {
            var sortie = await _ctx.SortieEnCoursAsync($"S{i}", "DET-001");
            await _ctx.Scan.ScannerRetourAsync(sortie, "DET-001");
        }

        var carte = (await TableauAsync()).Cartes.Single();

        Assert.Equal(("Utilisation", "Révision · 10 utilisations", "1 utilisation restante", "À planifier"), (carte.Famille, carte.Controle, carte.Detail, carte.Badge));
        Assert.Equal(new UsageEquipement(9, 10, EtatCompteur.Proche), carte.Usage);
        Assert.Equal(["Utilisation"], (await TableauAsync()).Familles);
    }

    [Fact]
    public void Les_natures_dependent_du_controle()
    {
        Assert.Equal(["TIV"], Core.Services.ServiceRevisions.NaturesPour(TypeControle.Tiv, "Blocs"));
        Assert.Equal(["Révision fabricant", "Contrôle visuel", "Révision atelier", "Réparation", "Changement de pièce"],
            Core.Services.ServiceRevisions.NaturesPour(TypeControle.Revision, "Détendeurs"));
    }
}
