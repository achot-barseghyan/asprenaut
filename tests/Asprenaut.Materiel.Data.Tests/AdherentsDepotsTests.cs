using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Data.Connexion;
using Asprenaut.Materiel.Data.Demo;
using Asprenaut.Materiel.Data.Depots;
using Asprenaut.Materiel.Data.Migrations;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Asprenaut.Materiel.Data.Tests;

/// <summary>Adhérents et sorties nominatives (migration 014).</summary>
public class AdherentsDepotsTests : IAsyncLifetime
{
    private readonly BaseTemporaire _base = new();

    public Task InitializeAsync() => _base.InitializeAsync();

    public Task DisposeAsync() => _base.DisposeAsync();

    private sealed class HorlogeFixe : IHorloge
    {
        public DateTime MaintenantUtc { get; set; } = new(2026, 11, 6, 18, 30, 0, DateTimeKind.Utc);

        public DateOnly AujourdHui => DateOnly.FromDateTime(MaintenantUtc);
    }

    [Fact]
    public async Task Un_adherent_est_ajoute_modifie_et_sa_licence_est_unique()
    {
        var depot = new AdherentRepository(_base.Fabrique);
        var julie = new Adherent { Nom = "Martin", Prenom = "Julie", Niveau = "Niveau 2", Licence = "A-1", Telephone = "06" };
        await depot.AjouterAsync(julie);
        await depot.AjouterAsync(new Adherent { Nom = "Sans", Prenom = "Licence" });
        await depot.AjouterAsync(new Adherent { Nom = "Autre", Prenom = "Sans licence" }); // Plusieurs licences vides : permis.

        julie.Actif = false;
        julie.Niveau = null;
        await depot.MettreAJourAsync(julie);

        var lu = (await depot.ObtenirAsync(julie.Id))!;
        Assert.Equal(("Julie Martin", null, "A-1", false), (lu.NomComplet, lu.Niveau, lu.Licence, lu.Actif));
        Assert.Equal(2, (await depot.ListerAsync(actifsSeulement: true)).Count);
        await Assert.ThrowsAsync<SqliteException>(() => depot.AjouterAsync(new Adherent { Nom = "Bis", Prenom = "Julie", Licence = "A-1" }));

        await using var cx = await _base.Fabrique.OuvrirAsync();
        Assert.Equal(0L, await cx.ExecuteScalarAsync<long>("SELECT actif FROM adherent WHERE id = @id;", new { id = julie.Id }));
    }

    // 015 : nitrox de l'adhérent et de l'équipement, relus par les dépôts (inventaire, lignes de sortie).
    [Fact]
    public async Task Le_nitrox_est_enregistre_pour_l_adherent_et_l_equipement()
    {
        Assert.True(await DonneesDemo.ChargerSiVideAsync(_base.Fabrique, new HorlogeFixe().AujourdHui));
        var adherents = new AdherentRepository(_base.Fabrique);
        var thomas = new Adherent { Nom = "Bernard", Prenom = "Thomas", Nitrox = true };
        await adherents.AjouterAsync(thomas);
        Assert.True((await adherents.ObtenirAsync(thomas.Id))!.Nitrox);

        var equipements = new EquipementRepository(_base.Fabrique);
        var bloc = (await equipements.ObtenirParCodeAsync("BLOC-031"))!;
        bloc.Nitrox = true;
        await equipements.MettreAJourAsync(bloc);
        Assert.True((await equipements.ObtenirAsync(bloc.Id))!.Nitrox);
        Assert.True((await equipements.ListerInventaireAsync("BLOC-031", null)).Single().Nitrox);

        var horloge = new HorlogeFixe();
        var sorties = new SortieRepository(_base.Fabrique);
        var moniteurs = new MoniteurRepository(_base.Fabrique);
        var service = new ServiceSortie(sorties, moniteurs, new JournalRepository(_base.Fabrique), horloge, new Reglages());
        var pascal = (await moniteurs.ListerAsync(true)).Single(m => m.Prenom == "Pascal");
        var id = (await service.CreerAsync(new NouvelleSortie(horloge.AujourdHui, TypeSortie.Piscine, "Piscine", pascal.Id))).Id!.Value;
        await new ServiceScan(equipements, sorties, new JournalRepository(_base.Fabrique), horloge, new Reglages()).ScannerDepartAsync(id, "BLOC-031");
        Assert.True((await sorties.ListerLignesAsync(id)).Single().Nitrox);

        await using var cx = await _base.Fabrique.OuvrirAsync();
        Assert.Equal((1L, 1L), await cx.QuerySingleAsync<(long, long)>(
            "SELECT (SELECT nitrox FROM equipement WHERE code = 'BLOC-031'), (SELECT nitrox FROM adherent WHERE id = @id);", new { id = thomas.Id }));
    }

    [Fact]
    public async Task La_migration_014_rend_mer_et_lac_nominatifs_mais_pas_les_sorties_existantes()
    {
        var fabrique = new FabriqueConnexion(Path.Combine(_base.Dossier, "ancienne.db"));
        var migrations = new GestionnaireMigrations(fabrique);
        Assert.Equal(13, await migrations.AppliquerAsync(jusqua: 13));
        await using (var cx = await fabrique.OuvrirAsync())
        {
            await cx.ExecuteAsync("""
                INSERT INTO moniteur (nom, prenom, niveau, telephone, inspecteur_tiv, actif) VALUES ('Durand', 'Pascal', 'MF1', '', 0, 1);
                INSERT INTO sortie (date_sortie, type, type_sortie_id, utilisations, intitule, moniteur_id, lieu, notes, statut)
                VALUES ('2026-11-01', 'Mer', (SELECT id FROM type_sortie WHERE nom = 'Mer'), 4, 'Marseille', 1, '', '', 'EnCours');
                """);
        }

        await migrations.AppliquerAsync();

        var types = await new TypeSortieRepository(fabrique).ListerAsync();
        Assert.Equal(["Mer", "Lac"], types.Where(t => t.Nominatif).Select(t => t.Nom));
        var sortie = (await new SortieRepository(fabrique).ListerResumesAsync(cloturees: false)).Single();
        Assert.False(sortie.Nominatif);
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task Le_materiel_d_un_adherent_et_le_manquant_gardent_son_nom()
    {
        var horloge = new HorlogeFixe();
        var equipements = new EquipementRepository(_base.Fabrique);
        var sorties = new SortieRepository(_base.Fabrique);
        var journal = new JournalRepository(_base.Fabrique);
        var moniteurs = new MoniteurRepository(_base.Fabrique);
        var adherents = new AdherentRepository(_base.Fabrique);
        var reglages = new Reglages();
        var scan = new ServiceScan(equipements, sorties, journal, horloge, reglages);
        var service = new ServiceSortie(sorties, moniteurs, journal, horloge, reglages, null, new TypeSortieRepository(_base.Fabrique), adherents);
        Assert.True(await DonneesDemo.ChargerSiVideAsync(_base.Fabrique, horloge.AujourdHui));
        var pascal = (await moniteurs.ListerAsync(true)).Single(m => m.Prenom == "Pascal");
        var julie = (await adherents.ListerAsync(true)).Single(a => a.Prenom == "Julie");
        var id = (await service.CreerAsync(new NouvelleSortie(horloge.AujourdHui, TypeSortie.Lac, "Lac des Settons", pascal.Id))).Id!.Value;

        foreach (var code in new[] { "A01", "S01" })
        {
            await scan.ScannerDepartAsync(id, code);
            var e = (await equipements.ObtenirParCodeAsync(code))!;
            Assert.True((await service.AttribuerAdherentAsync(id, e.Id, julie.Id)).Reussi);
        }

        await service.ValiderDepartAsync(id);
        await scan.ScannerRetourAsync(id, "S01");

        Assert.Equal([(julie.Id, 1, 0)], (await adherents.ListerSituationsAsync()).Select(s => (s.AdherentId, s.NbDehors, s.NbManquants)));
        Assert.True((await service.CloturerAsync(id)).Reussi);

        Assert.Equal([(julie.Id, 0, 1)], (await adherents.ListerSituationsAsync()).Select(s => (s.AdherentId, s.NbDehors, s.NbManquants)));
        var passages = await adherents.ListerPassagesAsync(julie.Id);
        Assert.Equal([("A01", StatutSortieEquipement.Manquant), ("S01", StatutSortieEquipement.Rendu)],
            passages.Select(p => (p.Code, p.Statut)).OrderBy(p => p.Code));
        Assert.All(passages, p => Assert.Equal(("Lac des Settons", "Lac", StatutSortie.Cloturee), (p.Intitule, p.TypeNom, p.StatutSortie)));

        // Journal : adhérent stocké et relu.
        var manquant = (await journal.ListerAsync(null, 100)).Single(l => l.Action == ActionsJournal.ManquantDeclare);
        Assert.Equal("Julie Martin", manquant.AdherentNom);
        await using var cx = await _base.Fabrique.OuvrirAsync();
        Assert.Equal(julie.Id, await cx.ExecuteScalarAsync<long>("SELECT adherent_id FROM journal WHERE action = 'ManquantDeclare';"));
        Assert.Equal(1L, await cx.ExecuteScalarAsync<long>("SELECT nominatif FROM sortie WHERE id = @id;", new { id }));
    }
}
