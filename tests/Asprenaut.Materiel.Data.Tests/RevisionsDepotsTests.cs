using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Data.Demo;
using Asprenaut.Materiel.Data.Depots;
using Dapper;

namespace Asprenaut.Materiel.Data.Tests;

/// <summary>Moniteurs (nouvelles colonnes) et révisions sur SQLite : formats stockés et relecture.</summary>
public class RevisionsDepotsTests : IAsyncLifetime
{
    private readonly BaseTemporaire _base = new();

    public Task InitializeAsync() => _base.InitializeAsync();

    public Task DisposeAsync() => _base.DisposeAsync();

    [Fact]
    public async Task Moniteur_telephone_et_habilitation_TIV()
    {
        var depot = new MoniteurRepository(_base.Fabrique);
        var id = await depot.AjouterAsync(new Moniteur { Prenom = "Marc", Nom = "Tiv", Telephone = "06 00 00 00 00", InspecteurTiv = true });

        var lu = await depot.ObtenirAsync(id);
        Assert.Equal(("06 00 00 00 00", true), (lu!.Telephone, lu.InspecteurTiv));

        lu.InspecteurTiv = false;
        lu.Telephone = "";
        await depot.MettreAJourAsync(lu);
        Assert.False((await depot.ObtenirAsync(id))!.InspecteurTiv);
    }

    [Fact]
    public async Task Controles_atelier_et_seances_stockes_en_texte_et_relus()
    {
        var equipements = new EquipementRepository(_base.Fabrique);
        var depot = new RevisionRepository(_base.Fabrique);
        var bloc = new Equipement { Code = "BLOC-031", Libelle = "Bloc", CategorieId = 1, DateRequalification = new DateOnly(2030, 3, 1), CreeLe = DateTime.UtcNow };
        await equipements.AjouterAsync(bloc);

        await depot.AjouterControleAsync(new Controle { EquipementId = bloc.Id, Type = TypeControle.Tiv, DateControle = new DateOnly(2025, 11, 2), Resultat = ResultatControle.Conforme, SaisiLe = DateTime.UtcNow });
        await depot.AjouterControleAsync(new Controle { EquipementId = bloc.Id, Type = TypeControle.Tiv, DateControle = new DateOnly(2026, 11, 2), Resultat = ResultatControle.ASurveiller, SaisiLe = DateTime.UtcNow });
        var passage = new PassageAtelier { EquipementId = bloc.Id, Prestataire = "Organisme", Motif = "Requalification", EnvoyeLe = new DateOnly(2026, 11, 2), RetourPrevu = new DateOnly(2026, 11, 20) };
        await depot.AjouterPassageAtelierAsync(passage);
        await depot.AjouterSeanceAsync(new SeanceTiv { DateSeance = new DateOnly(2026, 11, 21), Heure = "9:00", Lieu = "Club" });

        var dernier = Assert.Single(await depot.ListerDerniersControlesAsync());
        Assert.Equal((TypeControle.Tiv, new DateOnly(2026, 11, 2)), (dernier.Type, dernier.DateControle));
        Assert.Equal(ResultatControle.ASurveiller, (await depot.ListerControlesAsync(bloc.Id))[0].Resultat);
        Assert.Equal(new DateOnly(2030, 3, 1), (await equipements.ObtenirAsync(bloc.Id))!.DateRequalification);

        var atelier = Assert.Single(await depot.ListerAtelierEnCoursAsync());
        Assert.Equal(("BLOC-031", new DateOnly(2026, 11, 20)), (atelier.Code, atelier.RetourPrevu));
        passage.RevenuLe = new DateOnly(2026, 11, 18);
        await depot.MettreAJourPassageAsync(passage);
        Assert.Empty(await depot.ListerAtelierEnCoursAsync());
        Assert.Null(await depot.ObtenirPassageEnCoursAsync(bloc.Id));

        Assert.Equal("9:00", (await depot.ProchaineSeanceAsync(new DateOnly(2026, 11, 8)))?.Heure);
        Assert.Null(await depot.ProchaineSeanceAsync(new DateOnly(2026, 11, 22)));

        // Formats bruts : dates seules "yyyy-MM-dd", énumérations par leur nom.
        await using var cx = await _base.Fabrique.OuvrirAsync();
        Assert.Equal("2030-03-01", await cx.ExecuteScalarAsync<string>("SELECT date_requalification FROM equipement;"));
        Assert.Equal(
            ["2025-11-02|Tiv|Conforme", "2026-11-02|Tiv|ASurveiller"],
            await cx.QueryAsync<string>("SELECT date_controle || '|' || type || '|' || resultat FROM controle ORDER BY id;"));
        Assert.Equal("2026-11-02|2026-11-20|2026-11-18", await cx.ExecuteScalarAsync<string>("SELECT envoye_le || '|' || retour_prevu || '|' || revenu_le FROM atelier;"));
    }

    [Fact]
    public async Task Scenario_revisions_et_moniteurs_sur_les_donnees_de_demo()
    {
        var horloge = new Horloge(new DateTime(2026, 11, 8, 10, 0, 0, DateTimeKind.Utc));
        Assert.True(await DonneesDemo.ChargerSiVideAsync(_base.Fabrique, horloge.AujourdHui));
        var equipements = new EquipementRepository(_base.Fabrique);
        var sorties = new SortieRepository(_base.Fabrique);
        var moniteurs = new MoniteurRepository(_base.Fabrique);
        var journal = new JournalRepository(_base.Fabrique);
        var revisions = new ServiceRevisions(equipements, new CategorieRepository(_base.Fabrique), sorties, new RevisionRepository(_base.Fabrique), moniteurs, journal, horloge, new Reglages());
        var serviceMoniteurs = new ServiceMoniteurs(moniteurs, sorties, journal, horloge);

        var echeances = await revisions.ListerEcheancesAsync();
        Assert.Contains(echeances, l => l is { Code: "BLOC-033", Type: TypeControle.Tiv, Etat: EtatEcheance.EnRetard });
        Assert.Contains(echeances, l => l is { Code: "BLOC-032", Type: TypeControle.Requalification });
        Assert.DoesNotContain(echeances, l => l.Code == "DET-003"); // Réformé.

        var pascal = (await serviceMoniteurs.ListerInspecteursTivAsync()).Single();
        var bloc = (await equipements.ObtenirParCodeAsync("BLOC-033"))!;
        var r = await revisions.EnregistrerControleAsync(bloc.Id, TypeControle.Tiv, ResultatControle.Conforme, horloge.AujourdHui, pascal.Id);
        Assert.True(r.Reussi, r.Message);
        Assert.Equal(new DateOnly(2027, 11, 8), (await equipements.ObtenirAsync(bloc.Id))!.DateProchaineRevision);

        var seance = await revisions.ProchaineSeanceAsync();
        Assert.Equal(["Pascal Durand"], seance!.Inspecteurs);

        var fiches = await serviceMoniteurs.ListerFichesAsync();
        Assert.Equal(2, fiches.Count);
        Assert.All(fiches, f => Assert.Equal(0, f.NbDehors));
    }

    private sealed class Horloge(DateTime maintenant) : IHorloge
    {
        public DateTime MaintenantUtc => maintenant;

        public DateOnly AujourdHui => DateOnly.FromDateTime(maintenant);
    }
}
