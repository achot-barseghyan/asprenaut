using Asprenaut.Materiel.Data.Depots;
using Asprenaut.Materiel.Data.Migrations;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Asprenaut.Materiel.Data.Tests;

public class SchemaTests : IAsyncLifetime
{
    private readonly BaseTemporaire _base = new();

    public Task InitializeAsync() => _base.InitializeAsync();

    public Task DisposeAsync() => _base.DisposeAsync();

    [Fact]
    public async Task Migration_cree_toutes_les_tables_et_enregistre_la_version()
    {
        await using var cx = await _base.Fabrique.OuvrirAsync();
        var tables = (await cx.QueryAsync<string>("SELECT name FROM sqlite_master WHERE type = 'table';")).ToHashSet();

        Assert.Superset(
            new HashSet<string> { "categorie", "equipement", "moniteur", "sortie", "sortie_equipement", "journal", "schema_version", "controle", "atelier", "seance_tiv", "type_sortie", "seance_equipement", "manuel", "composant", "signalement", "adherent", "prestataire", "revision", "intervention_prevue", "seance_inspecteur", "modele_sortie", "utilisation_reprise" },
            tables);
        Assert.Equal(GestionnaireMigrations.VersionCible, await cx.ExecuteScalarAsync<int>("SELECT MAX(version) FROM schema_version;"));
    }

    [Fact]
    public async Task Migration_est_idempotente()
    {
        var version = await new GestionnaireMigrations(_base.Fabrique).AppliquerAsync();

        await using var cx = await _base.Fabrique.OuvrirAsync();
        Assert.Equal(GestionnaireMigrations.VersionCible, version);
        Assert.Equal(GestionnaireMigrations.ScriptsDisponibles().Count, await cx.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM schema_version;"));
        Assert.Equal(8, await cx.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM categorie;"));
    }

    [Fact]
    public void Scripts_001_a_027_sont_embarques()
    {
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27], GestionnaireMigrations.ScriptsDisponibles().Select(s => s.Version));
    }

    // 021 : le niveau texte de l'adhérent devient sa classe (formation en cours) ; une classe manquante est créée.
    [Fact]
    public async Task La_migration_021_relie_les_adherents_a_leur_classe()
    {
        var fabrique = new Connexion.FabriqueConnexion(Path.Combine(_base.Dossier, "avant-021.db"));
        var migrations = new GestionnaireMigrations(fabrique);
        Assert.Equal(20, await migrations.AppliquerAsync(jusqua: 20));
        await using (var cx = await fabrique.OuvrirAsync())
        {
            await cx.ExecuteAsync("""
                INSERT INTO adherent (nom, prenom, niveau, actif) VALUES
                    ('Martin', 'Julie', 'niveau 2', 1), ('Petit', 'Hugo', 'Autonome', 1),
                    ('Dubois', 'Léa', 'autonome ', 1), ('Moreau', 'Paul', NULL, 1);
                """);
        }

        await migrations.AppliquerAsync();
        var depot = new AdherentRepository(fabrique);
        var adherents = (await depot.ListerAsync(actifsSeulement: false)).ToDictionary(a => a.Prenom);
        Assert.Equal("Niveau 2", adherents["Julie"].Niveau); // Nom de la classe existante (migration 008).
        Assert.Equal(("Autonome", "Autonome"), (adherents["Hugo"].Niveau, adherents["Léa"].Niveau)); // Une seule classe créée.
        Assert.Equal(adherents["Hugo"].ClasseId, adherents["Léa"].ClasseId);
        Assert.Equal((null, null), (adherents["Paul"].ClasseId, adherents["Paul"].Niveau));

        await using var cx2 = await fabrique.OuvrirAsync();
        Assert.Equal(1L, await cx2.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM moniteur WHERE genre = 'Classe' AND nom = 'Autonome';"));

        // Le nom affiché suit la classe : la renommer change la formation de ses adhérents.
        await cx2.ExecuteAsync("UPDATE moniteur SET nom = 'Niveau 2 (2026)' WHERE id = @Id;", new { Id = adherents["Julie"].ClasseId });
        Assert.Equal("Niveau 2 (2026)", (await depot.ObtenirAsync(adherents["Julie"].Id))!.Niveau);
    }

    // 027 : « NON », « sans », « - »… dans les composants d'un détendeur importé sont vidés ; les vraies marques restent.
    [Fact]
    public async Task La_migration_027_vide_les_composants_notes_non()
    {
        var fabrique = new Connexion.FabriqueConnexion(Path.Combine(_base.Dossier, "avant-027.db"));
        var migrations = new GestionnaireMigrations(fabrique);
        Assert.Equal(26, await migrations.AppliquerAsync(jusqua: 26));
        await using (var cx = await fabrique.OuvrirAsync())
        {
            await cx.ExecuteAsync("""
                INSERT INTO equipement (code, libelle, categorie_id, cree_le, manometre, octopus, numero_octopus, modele_deuxieme_etage) VALUES
                    ('A01', 'Détendeur', 3, '2026-01-01T00:00:00.000Z', 'SCUBAPRO', 'NON', ' - ', 'R 380'),
                    ('A103', 'Détendeur', 3, '2026-01-01T00:00:00.000Z', ' non ', 'R 095', '1641', 'Sans');
                """);
        }

        await migrations.AppliquerAsync();

        await using var verif = await fabrique.OuvrirAsync();
        var lignes = (await verif.QueryAsync<(string, string, string, string)>(
            "SELECT manometre, octopus, numero_octopus, modele_deuxieme_etage FROM equipement ORDER BY code;")).ToList();
        Assert.Equal([("SCUBAPRO", "", "", "R 380"), ("", "R 095", "1641", "")], lignes);
    }

    // 010 : les anciens codes DET-/GIL- deviennent ceux des médailles (A07, S12), sorties et journal compris.
    [Fact]
    public async Task La_migration_010_passe_aux_codes_des_medailles()
    {
        var fabrique = new Connexion.FabriqueConnexion(Path.Combine(_base.Dossier, "ancienne.db"));
        var migrations = new GestionnaireMigrations(fabrique);
        Assert.Equal(9, await migrations.AppliquerAsync(jusqua: 9));
        await using (var cx = await fabrique.OuvrirAsync())
        {
            await cx.ExecuteAsync("""
                INSERT INTO equipement (code, libelle, categorie_id, cree_le) VALUES
                    ('DET-001', 'Détendeur', 3, '2026-01-01T00:00:00.000Z'),
                    ('DET-002', 'Détendeur', 3, '2026-01-01T00:00:00.000Z'),
                    ('A02', 'Déjà A02', 3, '2026-01-01T00:00:00.000Z'),
                    ('DET-010', 'Détendeur', 3, '2026-01-01T00:00:00.000Z'),
                    ('GIL-012', 'Gilet', 2, '2026-01-01T00:00:00.000Z'),
                    ('BLOC-031', 'Bloc', 1, '2026-01-01T00:00:00.000Z'),
                    ('DET-X', 'Autre forme', 3, '2026-01-01T00:00:00.000Z');
                INSERT INTO moniteur (nom, prenom) VALUES ('Durand', 'Pascal');
                INSERT INTO sortie (date_sortie, type, intitule, moniteur_id) VALUES ('2026-11-06', 'Piscine', 'Piscine', 1);
                INSERT INTO sortie_equipement (sortie_id, equipement_id, scanne_depart_le) VALUES (1, 1, '2026-11-06T18:00:00.000Z');
                INSERT INTO journal (horodatage, action, details, equipement_id) VALUES
                    ('2026-11-06T18:00:00.000Z', 'ScanDepart', 'DET-010 part avec la sortie 1', 4),
                    ('2026-11-06T18:01:00.000Z', 'EquipementCree', 'GIL-012 « Gilet » créé', 5);
                """);
        }

        Assert.Equal(GestionnaireMigrations.VersionCible, await migrations.AppliquerAsync());

        await using var verif = await fabrique.OuvrirAsync();
        Assert.Equal(["A01", "DET-002", "A02", "A10", "S12", "BLOC-031", "DET-X"],
            await verif.QueryAsync<string>("SELECT code FROM equipement ORDER BY id;"));
        Assert.Equal("A01", await verif.ExecuteScalarAsync<string>(
            "SELECT e.code FROM sortie_equipement se JOIN equipement e ON e.id = se.equipement_id;"));
        Assert.Equal(["A10 part avec la sortie 1", "S12 « Gilet » créé"],
            await verif.QueryAsync<string>("SELECT details FROM journal WHERE equipement_id IS NOT NULL ORDER BY id;"));
    }

    [Fact]
    public async Task Un_equipement_n_est_en_atelier_qu_une_fois_a_la_fois()
    {
        await using var cx = await _base.Fabrique.OuvrirAsync();
        await cx.ExecuteAsync("""
            INSERT INTO equipement (code, libelle, categorie_id, cree_le) VALUES ('DET-001', 'x', 3, '2026-01-01T00:00:00.000Z');
            INSERT INTO atelier (equipement_id, prestataire, envoye_le, revenu_le) VALUES (1, 'A', '2026-10-01', '2026-10-10');
            INSERT INTO atelier (equipement_id, prestataire, envoye_le) VALUES (1, 'B', '2026-11-01');
            """);

        await Assert.ThrowsAsync<SqliteException>(() => cx.ExecuteAsync(
            "INSERT INTO atelier (equipement_id, prestataire, envoye_le) VALUES (1, 'C', '2026-11-02');"));
        await Assert.ThrowsAsync<SqliteException>(() => cx.ExecuteAsync(
            "INSERT INTO controle (equipement_id, type, date_controle, resultat, saisi_le) VALUES (1, 'Tiv', '2026-11-01', 'Bof', '2026-11-01T00:00:00.000Z');"));
    }

    [Fact]
    public async Task Mode_WAL_et_cles_etrangeres_sont_actifs()
    {
        await using var cx = await _base.Fabrique.OuvrirAsync();

        Assert.Equal("wal", await cx.ExecuteScalarAsync<string>("PRAGMA journal_mode;"));
        Assert.Equal(1, await cx.ExecuteScalarAsync<int>("PRAGMA foreign_keys;"));
    }

    [Fact]
    public async Task Cle_etrangere_refuse_une_categorie_inexistante()
    {
        await using var cx = await _base.Fabrique.OuvrirAsync();

        var ex = await Assert.ThrowsAsync<SqliteException>(() => cx.ExecuteAsync(
            "INSERT INTO equipement (code, libelle, categorie_id, cree_le) VALUES ('X-1', 'x', 999, '2026-01-01T00:00:00.000Z');"));
        Assert.Contains("FOREIGN KEY", ex.Message);
    }

    [Fact]
    public async Task Code_equipement_est_unique_sans_tenir_compte_de_la_casse()
    {
        await using var cx = await _base.Fabrique.OuvrirAsync();
        const string sql = "INSERT INTO equipement (code, libelle, categorie_id, cree_le) VALUES (@code, 'x', 1, '2026-01-01T00:00:00.000Z');";
        await cx.ExecuteAsync(sql, new { code = "BLOC-031" });

        await Assert.ThrowsAsync<SqliteException>(() => cx.ExecuteAsync(sql, new { code = "bloc-031" }));
    }

    [Fact]
    public async Task Couple_sortie_equipement_est_unique_et_un_seul_dehors_par_equipement()
    {
        await using var cx = await _base.Fabrique.OuvrirAsync();
        await cx.ExecuteAsync("""
            INSERT INTO moniteur (nom, prenom) VALUES ('Durand', 'Pascal');
            INSERT INTO equipement (code, libelle, categorie_id, cree_le) VALUES ('BLOC-031', 'x', 1, '2026-01-01T00:00:00.000Z');
            INSERT INTO sortie (date_sortie, type, intitule, moniteur_id) VALUES ('2026-11-06', 'Piscine', 'A', 1);
            INSERT INTO sortie (date_sortie, type, intitule, moniteur_id) VALUES ('2026-11-07', 'Mer', 'B', 1);
            INSERT INTO sortie_equipement (sortie_id, equipement_id, scanne_depart_le) VALUES (1, 1, '2026-11-06T18:00:00.000Z');
            """);

        // Même couple (sortie, équipement).
        await Assert.ThrowsAsync<SqliteException>(() => cx.ExecuteAsync(
            "INSERT INTO sortie_equipement (sortie_id, equipement_id, scanne_depart_le, statut) VALUES (1, 1, '2026-11-06T18:00:00.000Z', 'Rendu');"));

        // Dehors dans une deuxième sortie.
        await Assert.ThrowsAsync<SqliteException>(() => cx.ExecuteAsync(
            "INSERT INTO sortie_equipement (sortie_id, equipement_id, scanne_depart_le) VALUES (2, 1, '2026-11-07T08:00:00.000Z');"));
    }

    [Fact]
    public async Task Valeur_d_enumeration_inconnue_est_refusee()
    {
        await using var cx = await _base.Fabrique.OuvrirAsync();

        await Assert.ThrowsAsync<SqliteException>(() => cx.ExecuteAsync(
            "INSERT INTO equipement (code, libelle, categorie_id, etat, cree_le) VALUES ('X-2', 'x', 1, 'Casse', '2026-01-01T00:00:00.000Z');"));
    }
}
