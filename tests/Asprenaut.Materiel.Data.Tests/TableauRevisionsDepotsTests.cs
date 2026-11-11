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
        Assert.Equal("", relu.Composant); // Équipement complet.

        // Fiche de suivi (migration 011) : le composant visé et les détails sont enregistrés et relus.
        var det = (await equipements.ObtenirParCodeAsync("A01"))!;
        det.Octopus = "R095";
        await equipements.MettreAJourAsync(det);
        r = await revisions.EnregistrerInterventionAsync(new Intervention(
            det.Id, TypeControle.Revision, ResultatControle.Conforme, horloge.AujourdHui, "Réparation", "Aquasub", Composant: "Octopus", Notes: "Flexible MP"));
        Assert.True(r.Reussi, r.Message);
        Assert.Equal(("Octopus", "Flexible MP"), await cx.QuerySingleAsync<(string, string)>(
            "SELECT composant, notes FROM controle WHERE equipement_id = @id;", new { id = det.Id }));
        var controleDet = (await depot.ListerControlesAsync(det.Id)).Single();
        Assert.Equal(("Octopus", "Flexible MP"), (controleDet.Composant, controleDet.Notes));

        // Un contrôle de composant ne compte pas comme dernier contrôle de l'équipement (échéancier, compteur, inventaire).
        Assert.DoesNotContain(await depot.ListerDerniersControlesAsync(), d => d.EquipementId == det.Id);
        Assert.Null((await equipements.ListerInventaireAsync(null, null)).Single(l => l.EquipementId == det.Id).DerniereRevision);

        // Détails d'un composant ajouté (migration 012) : écrits, remplacés (même rôle), relus ; dates au format ISO.
        await equipements.EnregistrerComposantAsync(new DetailComposant { EquipementId = det.Id, Role = "Octopus", Origine = OrigineComposant.Neuve });
        await equipements.EnregistrerComposantAsync(new DetailComposant
        {
            EquipementId = det.Id, Role = "Octopus", NumeroSerie = "", Fabrication = new DateOnly(2019, 3, 1),
            MiseEnService = new DateOnly(2026, 11, 6), Origine = OrigineComposant.Recuperee,
        });
        var detail = Assert.Single(await equipements.ListerComposantsAsync(det.Id));
        Assert.Equal((new DateOnly(2019, 3, 1), new DateOnly(2026, 11, 6), OrigineComposant.Recuperee), (detail.Fabrication, detail.MiseEnService, detail.Origine));
        Assert.Equal(("2019-03-01", "Recuperee"), await cx.QuerySingleAsync<(string, string)>(
            "SELECT fabrication, origine FROM composant WHERE equipement_id = @id;", new { id = det.Id }));
        Assert.Empty(await equipements.ListerComposantsAsync(bloc.Id));

        // Supprimer la séance supprime ses planifiés (ON DELETE CASCADE).
        await depot.AjouterASeanceAsync(seance.Id, bloc.Id);
        await cx.ExecuteAsync("DELETE FROM seance_tiv WHERE id = @id;", new { id = seance.Id });
        Assert.Equal(0L, await cx.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM seance_equipement;"));
    }
}
