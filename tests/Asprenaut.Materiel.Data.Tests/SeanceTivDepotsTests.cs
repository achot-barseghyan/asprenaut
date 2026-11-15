using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Data.Depots;
using Dapper;

namespace Asprenaut.Materiel.Data.Tests;

/// <summary>Séance TIV (migration 023) : note, inspecteurs choisis, modification, annulation.</summary>
public class SeanceTivDepotsTests : IAsyncLifetime
{
    private readonly BaseTemporaire _base = new();

    public Task InitializeAsync() => _base.InitializeAsync();

    public Task DisposeAsync() => _base.DisposeAsync();

    [Fact]
    public async Task Seance_avec_note_et_inspecteurs_modifiee_puis_annulee()
    {
        var moniteurs = new MoniteurRepository(_base.Fabrique);
        var marc = await moniteurs.AjouterAsync(new Moniteur { Prenom = "Marc", Nom = "Tiv", InspecteurTiv = true });
        var julie = await moniteurs.AjouterAsync(new Moniteur { Prenom = "Julie", Nom = "Bernard", InspecteurTiv = true });
        var equipements = new EquipementRepository(_base.Fabrique);
        var bloc = await equipements.AjouterAsync(new Equipement { Code = "BLOC-031", Libelle = "Bloc", CategorieId = 1, CreeLe = DateTime.UtcNow });
        var depot = new RevisionRepository(_base.Fabrique);

        var id = await depot.AjouterSeanceAsync(new SeanceTiv { DateSeance = new DateOnly(2026, 11, 21), Heure = "9:00", Lieu = "Club", Note = "Blocs vides", InspecteurIds = [marc] });
        await depot.AjouterASeanceAsync(id, bloc);
        var lue = (await depot.ProchaineSeanceAsync(new DateOnly(2026, 11, 8)))!;
        Assert.Equal(("Blocs vides", false), (lue.Note, lue.Annulee));
        Assert.Equal([marc], lue.InspecteurIds);

        lue.DateSeance = new DateOnly(2026, 11, 28);
        lue.Note = "";
        lue.InspecteurIds = [julie];
        await depot.ModifierSeanceAsync(lue);
        var modifiee = (await depot.ObtenirSeanceAsync(id))!;
        Assert.Equal((new DateOnly(2026, 11, 28), ""), (modifiee.DateSeance, modifiee.Note));
        Assert.Equal([julie], modifiee.InspecteurIds);
        await using (var cx = await _base.Fabrique.OuvrirAsync())
        {
            Assert.Equal("2026-11-28", await cx.ExecuteScalarAsync<string>("SELECT date_seance FROM seance_tiv WHERE id = @id;", new { id }));
        }

        await depot.AnnulerSeanceAsync(id);
        Assert.Null(await depot.ProchaineSeanceAsync(new DateOnly(2026, 11, 8)));
        Assert.True((await depot.ObtenirSeanceAsync(id))!.Annulee);
        Assert.Empty(await depot.ListerPlanifiesAsync(id));
    }
}
