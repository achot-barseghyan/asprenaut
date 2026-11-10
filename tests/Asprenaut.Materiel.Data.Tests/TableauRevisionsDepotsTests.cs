using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Data.Demo;
using Asprenaut.Materiel.Data.Depots;
using Dapper;

namespace Asprenaut.Materiel.Data.Tests;

public class TableauRevisionsDepotsTests : IAsyncLifetime
{
    private readonly BaseTemporaire _base = new();

    public Task InitializeAsync() => _base.InitializeAsync();

    public Task DisposeAsync() => _base.DisposeAsync();

    private sealed class HorlogeFixe : IHorloge
    {
        public DateTime MaintenantUtc => new(2026, 11, 6, 18, 30, 0, DateTimeKind.Utc);

        public DateOnly AujourdHui => DateOnly.FromDateTime(MaintenantUtc);
    }

    [Fact]
    public async Task La_seance_les_planifies_et_l_intervention_sont_enregistres()
    {
        var horloge = new HorlogeFixe();
        Assert.True(await DonneesDemo.ChargerSiVideAsync(_base.Fabrique, horloge.AujourdHui));
        var equipements = new EquipementRepository(_base.Fabrique);
        var depot = new RevisionRepository(_base.Fabrique);
        var revisions = new ServiceRevisions(equipements, new CategorieRepository(_base.Fabrique), new SortieRepository(_base.Fabrique), depot,
            new MoniteurRepository(_base.Fabrique), new JournalRepository(_base.Fabrique), horloge, new Reglages());
        var bloc = (await equipements.ObtenirParCodeAsync("BLOC-033"))!; // TIV dépassée dans la démo.
        var pascal = (await new MoniteurRepository(_base.Fabrique).ListerAsync(true)).Single(m => m.Prenom == "Pascal");

        // La démo a une séance TIV dans deux semaines.
        Assert.True((await revisions.AjouterASeanceAsync(bloc.Id)).Reussi);
        Assert.True((await revisions.AjouterASeanceAsync(bloc.Id)).Reussi); // Sans doublon.
        var seance = (await depot.ProchaineSeanceAsync(horloge.AujourdHui))!;
        Assert.Equal([bloc.Id], await depot.ListerPlanifiesAsync(seance.Id));
        Assert.Equal(EtapeRevision.Planifie, (await revisions.TableauAsync()).Cartes.Single(c => c.Code == "BLOC-033").Etape);

        var r = await revisions.EnregistrerInterventionAsync(new Intervention(
            bloc.Id, TypeControle.Tiv, ResultatControle.Conforme, horloge.AujourdHui, "TIV", "Pascal Durand", pascal.Id));
        Assert.True(r.Reussi, r.Message);

        Assert.Empty(await depot.ListerPlanifiesAsync(seance.Id));
        await using var cx = await _base.Fabrique.OuvrirAsync();
        Assert.Equal(("TIV", "Pascal Durand"), await cx.QuerySingleAsync<(string, string)>(
            "SELECT nature, intervenant FROM controle WHERE equipement_id = @id AND date_controle = '2026-11-06';", new { id = bloc.Id }));
        var relu = (await depot.ListerControlesAsync(bloc.Id)).First();
        Assert.Equal(("TIV", "Pascal Durand"), (relu.Nature, relu.Intervenant));
        Assert.Equal(EtapeRevision.Fait, (await revisions.TableauAsync()).Cartes.Single(c => c.Code == "BLOC-033").Etape);

        // Supprimer la séance supprime ses planifiés (ON DELETE CASCADE).
        await depot.AjouterASeanceAsync(seance.Id, bloc.Id);
        await cx.ExecuteAsync("DELETE FROM seance_tiv WHERE id = @id;", new { id = seance.Id });
        Assert.Equal(0L, await cx.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM seance_equipement;"));
    }
}
