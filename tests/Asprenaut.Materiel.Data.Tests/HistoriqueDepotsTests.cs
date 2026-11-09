using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Data.Connexion;
using Asprenaut.Materiel.Data.Demo;
using Asprenaut.Materiel.Data.Depots;
using Asprenaut.Materiel.Data.Migrations;
using Dapper;

namespace Asprenaut.Materiel.Data.Tests;

public class HistoriqueDepotsTests : IAsyncLifetime
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
    public async Task Le_journal_est_relie_puis_relu_avec_l_equipement_la_sortie_et_le_moniteur()
    {
        var horloge = new HorlogeFixe();
        var equipements = new EquipementRepository(_base.Fabrique);
        var sorties = new SortieRepository(_base.Fabrique);
        var journal = new JournalRepository(_base.Fabrique);
        var moniteurs = new MoniteurRepository(_base.Fabrique);
        var revisions = new RevisionRepository(_base.Fabrique);
        var reglages = new Reglages();
        var scan = new ServiceScan(equipements, sorties, journal, horloge, reglages);
        var serviceSortie = new ServiceSortie(sorties, moniteurs, journal, horloge, reglages);
        var serviceRevisions = new ServiceRevisions(equipements, new CategorieRepository(_base.Fabrique), sorties, revisions, moniteurs, journal, horloge, reglages);
        var historique = new ServiceHistorique(journal, equipements, new CategorieRepository(_base.Fabrique), sorties, revisions, moniteurs, horloge, reglages);
        Assert.True(await DonneesDemo.ChargerSiVideAsync(_base.Fabrique, horloge.AujourdHui));
        var pascal = (await moniteurs.ListerAsync(true)).Single(m => m.Prenom == "Pascal");

        var id = (await serviceSortie.CreerAsync(new NouvelleSortie(new DateOnly(2026, 11, 6), TypeSortie.Piscine, "Cours en piscine", pascal.Id))).Id!.Value;
        await scan.ScannerDepartAsync(id, "BLOC-031");
        await scan.ScannerDepartAsync(id, "GIL-001");
        await serviceSortie.ValiderDepartAsync(id);
        await scan.ScannerRetourAsync(id, "BLOC-031");
        await serviceSortie.CloturerAsync(id);
        await serviceRevisions.EnvoyerEnAtelierAsync("DET-001", "Aqua Service", "Fuite", null);

        // Stockage brut : identifiants en entiers, sortie seule pour la création.
        await using (var cx = await _base.Fabrique.OuvrirAsync())
        {
            var bloc = await cx.ExecuteScalarAsync<long>("SELECT id FROM equipement WHERE code = 'BLOC-031';");
            Assert.Equal((bloc, id), await cx.QuerySingleAsync<(long, long)>(
                "SELECT equipement_id, sortie_id FROM journal WHERE action = 'ScanDepart' AND details LIKE 'BLOC-031%';"));
            Assert.Null(await cx.ExecuteScalarAsync<long?>("SELECT equipement_id FROM journal WHERE action = 'SortieCreee';"));
        }

        var lignes = await journal.ListerAsync(null, 100);
        var depart = lignes.First(l => l.Action == ActionsJournal.ScanDepart && l.Code == "BLOC-031");
        Assert.Equal(("Bloc 12 L acier", "Blocs", "Cours en piscine"), (depart.Libelle, depart.Categorie, depart.SortieIntitule));
        Assert.Equal((new DateOnly(2026, 11, 6), TypeSortie.Piscine, "Pascal Durand"), (depart.SortieDate, depart.SortieType, depart.MoniteurNom));
        Assert.Equal((2, 1), (depart.SortieNbEquipements, depart.SortieNbManquants));
        Assert.Equal(horloge.MaintenantUtc, depart.Horodatage);
        Assert.Contains(lignes, l => l.Action == ActionsJournal.EnvoiAtelier && l.Code == "DET-001" && l.SortieId == null);
        Assert.Empty(await journal.ListerAsync(DateTime.UtcNow.AddMinutes(1), 100)); // La démo est datée de l'heure réelle.
        Assert.Equal(3, (await journal.ListerAsync(null, 3)).Count);

        // Vie d'un équipement sur SQLite : passages en sortie et en atelier.
        var gilet = await historique.ObtenirVieAsync("GIL-001");
        Assert.Equal(StatutInventaire.Manquant, gilet!.Statut);
        Assert.Contains(gilet.Etapes, e => e.Titre == "Noté manquant");
        Assert.Equal(1, gilet.SortiesSaison);
        var detendeur = await historique.ObtenirVieAsync("DET-001");
        Assert.Equal("Atelier", detendeur!.LibelleStatut);
        Assert.Contains(detendeur.Etapes, e => e.Titre == "Envoyé en atelier" && e.Detail == "Aqua Service · Fuite");
    }

    [Fact]
    public async Task La_migration_003_relie_les_entrees_deja_ecrites()
    {
        var fabrique = new FabriqueConnexion(Path.Combine(_base.Dossier, "ancienne.db"));
        var migrations = new GestionnaireMigrations(fabrique);
        Assert.Equal(2, await migrations.AppliquerAsync(jusqua: 2));
        await using (var cx = await fabrique.OuvrirAsync())
        {
            await cx.ExecuteAsync("""
                INSERT INTO moniteur (nom, prenom) VALUES ('Durand', 'Pascal');
                INSERT INTO equipement (code, libelle, categorie_id, cree_le) VALUES ('BLOC-03', 'Piège', 1, '2026-01-01T00:00:00.000Z');
                INSERT INTO equipement (code, libelle, categorie_id, cree_le) VALUES ('BLOC-031', 'Bloc 12 L', 1, '2026-01-01T00:00:00.000Z');
                INSERT INTO sortie (date_sortie, type, intitule, moniteur_id) VALUES ('2026-11-06', 'Piscine', 'Piscine', 1);
                INSERT INTO journal (horodatage, action, details) VALUES
                    ('2026-11-06T18:00:00.000Z', 'ScanDepart', 'BLOC-031 part avec la sortie 1 « Piscine »'),
                    ('2026-11-06T18:01:00.000Z', 'ScanRetour', 'BLOC-031 rendu (sortie 1)'),
                    ('2026-11-06T18:02:00.000Z', 'SortieCloturee', 'Sortie 1 clôturée, 0 manquant'),
                    ('2026-11-06T18:03:00.000Z', 'SortieCreee', 'Sortie 99 « Disparue » du 06/11/2026 (Pascal Durand)'),
                    ('2026-11-06T18:04:00.000Z', 'MoniteurCree', 'Moniteur Pascal Durand ajouté');
                """);
        }

        Assert.Equal(GestionnaireMigrations.VersionCible, await migrations.AppliquerAsync());

        await using var verif = await fabrique.OuvrirAsync();
        var liens = (await verif.QueryAsync<(string Action, long? EquipementId, long? SortieId)>(
            "SELECT action, equipement_id, sortie_id FROM journal ORDER BY id;")).ToList();
        Assert.Equal(
            [("ScanDepart", 2L, 1L), ("ScanRetour", 2L, 1L), ("SortieCloturee", null, 1L), ("SortieCreee", null, null), ("MoniteurCree", null, null)],
            liens.Select(l => (l.Action, l.EquipementId, l.SortieId)));
    }
}
