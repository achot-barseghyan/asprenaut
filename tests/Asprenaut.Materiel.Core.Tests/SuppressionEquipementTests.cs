using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Règle 43 : un équipement qui n'a jamais servi peut être supprimé ; sinon on le réforme.</summary>
public class SuppressionEquipementTests
{
    private readonly Contexte _ctx = new();

    [Fact]
    public async Task Un_equipement_jamais_utilise_est_supprime_et_note_au_journal()
    {
        var det = _ctx.Equipement("A25", categorie: "Détendeurs");

        var r = await _ctx.Inventaire.SupprimerAsync(det.Id);

        Assert.True(r.Reussi, r.Message);
        Assert.Equal("A25 · Détendeur supprimé.", r.Message);
        Assert.DoesNotContain(_ctx.Db.Equipements, e => e.Id == det.Id);
        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.EquipementSupprime && j.Details.Contains("A25") && j.EquipementId is null);
    }

    [Fact]
    public async Task Un_equipement_deja_parti_en_sortie_n_est_pas_supprime()
    {
        var bloc = _ctx.Equipement("BLOC-031");
        var sortie = await _ctx.SortieEnCoursAsync("Piscine", "BLOC-031");
        await _ctx.Scan.ScannerRetourAsync(sortie, "BLOC-031");
        Assert.False((await _ctx.Inventaire.ListerAsync(null, null)).Single().JamaisUtilise);

        var r = await _ctx.Inventaire.SupprimerAsync(bloc.Id);

        Assert.False(r.Reussi);
        Assert.Contains("déjà été utilisé", r.Message);
        Assert.Contains(_ctx.Db.Equipements, e => e.Id == bloc.Id);
    }

    [Fact]
    public async Task Un_compteur_repris_d_un_import_compte_comme_une_utilisation()
    {
        var det = _ctx.Equipement("A26", categorie: "Détendeurs");
        _ctx.Db.Reprises.Add(new RepriseUtilisations(det.Id, 40, Contexte.Maintenant));

        Assert.False((await _ctx.Inventaire.SupprimerAsync(det.Id)).Reussi);
    }

    [Fact]
    public async Task Un_equipement_en_atelier_n_est_pas_supprime()
    {
        var det = _ctx.Equipement("A27", categorie: "Détendeurs");
        await _ctx.Revisions.EnvoyerEnAtelierAsync("A27", "Aqua Service", "Fuite", null);

        var r = await _ctx.Inventaire.SupprimerAsync(det.Id);

        Assert.False(r.Reussi);
        Assert.Contains("en atelier", r.Message);
    }

    [Fact]
    public async Task Supprimer_est_une_action_protegee()
    {
        var det = _ctx.Equipement("A28", categorie: "Détendeurs");
        Assert.True(_ctx.Gestion.DefinirCode(null, "1234", "1234").Reussi);
        _ctx.Gestion.Verrouiller();

        Assert.False((await _ctx.Inventaire.SupprimerAsync(det.Id)).Reussi);
        Assert.Contains(_ctx.Db.Equipements, e => e.Id == det.Id);
    }
}
