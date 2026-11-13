using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Data.Depots;
using Asprenaut.Materiel.Data.Migrations;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Asprenaut.Materiel.Data.Tests;

/// <summary>Révisions en 5 étapes et prestataires (migration 016) : reprise des données, formats stockés, requêtes.</summary>
public class EtapesRevisionsDepotsTests : IAsyncLifetime
{
    private readonly BaseTemporaire _base = new();

    public Task InitializeAsync() => _base.InitializeAsync();

    public Task DisposeAsync() => _base.DisposeAsync();

    private static readonly DateOnly Aujourdhui = DateOnly.FromDateTime(DateTime.Now);

    private async Task<long> EquipementAsync(string code, long categorie = 3)
    {
        var e = new Equipement { Code = code, Libelle = code, CategorieId = categorie, CreeLe = DateTime.UtcNow };
        return await new EquipementRepository(_base.Fabrique).AjouterAsync(e);
    }

    // Règle : « Planifié » devient « Réparation interne », « En atelier » devient « Chez le prestataire », sans perte.
    [Fact]
    public async Task La_migration_016_reprend_planifie_en_interne_et_atelier_chez_le_prestataire()
    {
        var fabrique = new Connexion.FabriqueConnexion(Path.Combine(_base.Dossier, "ancienne.db"));
        var migrations = new GestionnaireMigrations(fabrique);
        Assert.Equal(15, await migrations.AppliquerAsync(jusqua: 15));
        await using (var cx = await fabrique.OuvrirAsync())
        {
            await cx.ExecuteAsync("""
                INSERT INTO equipement (id, code, libelle, categorie_id, cree_le) VALUES
                    (1, 'BLOC-031', 'Bloc', 1, '2026-01-01T00:00:00.000Z'),
                    (2, 'BLOC-032', 'Bloc', 1, '2026-01-01T00:00:00.000Z'),
                    (3, 'A09', 'Détendeur', 3, '2026-01-01T00:00:00.000Z'),
                    (4, 'A10', 'Détendeur', 3, '2026-01-01T00:00:00.000Z');
                INSERT INTO seance_tiv (id, date_seance, heure, lieu) VALUES
                    (1, date('now', 'localtime', '+10 days'), '9:00', 'Local'),
                    (2, date('now', 'localtime', '-40 days'), '9:00', 'Local');
                INSERT INTO seance_equipement (seance_id, equipement_id) VALUES (1, 1), (2, 2);
                INSERT INTO atelier (equipement_id, prestataire, motif, envoye_le, retour_prevu, revenu_le) VALUES
                    (3, 'aqua service', 'Fuite', '2026-09-01', NULL, '2026-09-20'),
                    (3, 'Aqua Service', 'Révision complète', '2026-10-28', '2026-11-15', NULL),
                    (4, 'Bureau Veritas', '', '2026-11-02', NULL, NULL);
                INSERT INTO controle (equipement_id, type, date_controle, resultat, saisi_le) VALUES
                    (2, 'Tiv', date('now', 'localtime', '-3 days'), 'Conforme', '2026-11-03T10:00:00.000Z'),
                    (2, 'Tiv', '2025-01-10', 'Conforme', '2025-01-10T10:00:00.000Z');
                """);
        }

        Assert.Equal(GestionnaireMigrations.VersionCible, await migrations.AppliquerAsync());

        await using var lecture = await fabrique.OuvrirAsync();
        Assert.Equal(["Aqua Service", "Bureau Veritas"], await lecture.QueryAsync<string>("SELECT nom FROM prestataire ORDER BY nom COLLATE NOCASE;"));
        var lignes = (await lecture.QueryAsync<(long Equipement, string Etape, string? Prestataire, string Motif, string? Envoi, string? Retour, long? Atelier, long SeanceTiv, string? Fait)>("""
            SELECT r.equipement_id, r.etape, p.nom, r.motif, r.date_envoi, r.date_retour_prevue, r.atelier_id, r.seance_tiv, r.date_fait
            FROM revision r LEFT JOIN prestataire p ON p.id = r.prestataire_id ORDER BY r.equipement_id, r.id;
            """)).ToList();
        Assert.Equal(4, lignes.Count);
        Assert.Equal((1L, "Interne", 1L), (lignes[0].Equipement, lignes[0].Etape, lignes[0].SeanceTiv));          // Séance à venir.
        Assert.Equal((2L, "Fait"), (lignes[1].Equipement, lignes[1].Etape));                                           // Séance passée : pas de reprise ; TIV récente → Fait.
        Assert.Equal((3L, "ChezPrestataire", "Aqua Service", "Révision complète", "2026-10-28", "2026-11-15"),
            (lignes[2].Equipement, lignes[2].Etape, lignes[2].Prestataire, lignes[2].Motif, lignes[2].Envoi, lignes[2].Retour));
        Assert.Equal(2L, lignes[2].Atelier);                                                                          // Le passage en cours, pas l'ancien.
        Assert.Equal((4L, "ChezPrestataire", "Bureau Veritas"), (lignes[3].Equipement, lignes[3].Etape, lignes[3].Prestataire));

        // Sans perte : passages en atelier et séances restent.
        Assert.Equal(3, await lecture.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM atelier;"));
        Assert.Equal(2, await lecture.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM seance_equipement;"));
    }

    [Fact]
    public async Task Les_etapes_sont_stockees_en_texte_et_relues_avec_le_prestataire()
    {
        var depot = new RevisionRepository(_base.Fabrique);
        var prestataires = new PrestataireRepository(_base.Fabrique);
        var det = await EquipementAsync("A10");
        var idPrestataire = await prestataires.AjouterAsync(new Prestataire { Nom = "Aqua Service", Type = TypePrestataire.Magasin, Telephone = "03", Adresse = "Chalon" });
        var suivi = new SuiviRevision
        {
            EquipementId = det, Etape = EtapeRevision.AEnvoyer, PrestataireId = idPrestataire, Motif = "Révision", NoteEnvoi = "Dépôt prévu le 15/11",
            CreeLe = new DateTime(2026, 11, 6, 18, 30, 0, DateTimeKind.Utc),
        };
        await depot.AjouterSuiviAsync(suivi);

        await using var cx = await _base.Fabrique.OuvrirAsync();
        Assert.Equal(("AEnvoyer", "2026-11-06T18:30:00.000Z", 0L), await cx.QuerySingleAsync<(string, string, long)>("SELECT etape, cree_le, seance_tiv FROM revision;"));

        // Une seule révision en cours par équipement (index partiel ux_revision_en_cours).
        await Assert.ThrowsAsync<SqliteException>(() => depot.AjouterSuiviAsync(new SuiviRevision { EquipementId = det, Etape = EtapeRevision.Interne, CreeLe = DateTime.UtcNow }));

        suivi.Etape = EtapeRevision.ChezPrestataire;
        suivi.DateEnvoi = new DateOnly(2026, 11, 13);
        suivi.DateRetourPrevue = new DateOnly(2026, 11, 20);
        suivi.SeanceTiv = true;
        await depot.MettreAJourSuiviAsync(suivi);
        Assert.Equal(("ChezPrestataire", "2026-11-13", "2026-11-20", 1L), await cx.QuerySingleAsync<(string, string, string, long)>(
            "SELECT etape, date_envoi, date_retour_prevue, seance_tiv FROM revision;"));

        var lu = (await depot.ObtenirSuiviEnCoursAsync(det))!;
        Assert.Equal((EtapeRevision.ChezPrestataire, "Aqua Service", "Dépôt prévu le 15/11", new DateOnly(2026, 11, 20), true),
            (lu.Etape, lu.Prestataire, lu.NoteEnvoi, lu.DateRetourPrevue!.Value, lu.SeanceTiv));
        Assert.Equal(lu.Id, (await depot.ObtenirSuiviAsync(lu.Id))!.Id);
        Assert.Single(await depot.ListerSuivisAsync(Aujourdhui));
    }

    // Règle : « Archiver tout » n'archive que les révisions faites et ne supprime aucune ligne ; annuler les remet.
    [Fact]
    public async Task Archiver_seulement_les_faites_sans_supprimer()
    {
        var depot = new RevisionRepository(_base.Fabrique);
        var a = await EquipementAsync("A01");
        var b = await EquipementAsync("A02");
        var fait = new SuiviRevision { EquipementId = a, Etape = EtapeRevision.Fait, DateFait = Aujourdhui, CreeLe = DateTime.UtcNow };
        var ancien = new SuiviRevision { EquipementId = a, Etape = EtapeRevision.Fait, DateFait = Aujourdhui.AddDays(-40), CreeLe = DateTime.UtcNow };
        var enCours = new SuiviRevision { EquipementId = b, Etape = EtapeRevision.Interne, CreeLe = DateTime.UtcNow };
        foreach (var s in new[] { fait, ancien, enCours })
        {
            await depot.AjouterSuiviAsync(s);
        }

        Assert.Equal([fait.Id, enCours.Id], (await depot.ListerSuivisAsync(Aujourdhui.AddDays(-30))).Select(s => s.Id)); // Plus de 30 jours : masqué.

        Assert.Equal(1, await depot.ArchiverSuivisAsync([fait.Id, enCours.Id], true, Aujourdhui));
        await using var cx = await _base.Fabrique.OuvrirAsync();
        Assert.Equal(3, await cx.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM revision;"));
        Assert.Equal((1L, Aujourdhui.ToString("yyyy-MM-dd")), await cx.QuerySingleAsync<(long, string)>("SELECT archive, date_archive FROM revision WHERE id = @id;", new { id = fait.Id }));
        Assert.Equal([enCours.Id], (await depot.ListerSuivisAsync(Aujourdhui.AddDays(-30))).Select(s => s.Id));

        Assert.Equal(1, await depot.ArchiverSuivisAsync([fait.Id], false, null));
        Assert.Equal(2, (await depot.ListerSuivisAsync(Aujourdhui.AddDays(-30))).Count);

        // Seule une révision en cours se supprime (abandon), jamais une faite.
        await depot.SupprimerSuiviAsync(fait.Id);
        await depot.SupprimerSuiviAsync(enCours.Id);
        Assert.Equal(2, await cx.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM revision;"));
        Assert.Null(await depot.ObtenirSuiviEnCoursAsync(b));
    }

    [Fact]
    public async Task Prestataires_stockes_et_nom_unique_sans_les_majuscules()
    {
        var depot = new PrestataireRepository(_base.Fabrique);
        var id = await depot.AjouterAsync(new Prestataire { Nom = "Bureau Veritas", Type = TypePrestataire.Organisme });
        await depot.AjouterAsync(new Prestataire { Nom = "Aqua Service", Actif = false });

        await using var cx = await _base.Fabrique.OuvrirAsync();
        Assert.Equal(("Organisme", 1L), await cx.QuerySingleAsync<(string, long)>("SELECT type, actif FROM prestataire WHERE id = @id;", new { id }));
        await Assert.ThrowsAsync<SqliteException>(() => depot.AjouterAsync(new Prestataire { Nom = "BUREAU VERITAS" }));

        Assert.Equal(["Bureau Veritas", "Aqua Service"], (await depot.ListerAsync()).Select(p => p.Nom)); // Actifs d'abord.
        var lu = (await depot.ObtenirAsync(id))!;
        lu.Telephone = "03 85 00 00 00";
        lu.Actif = false;
        await depot.MettreAJourAsync(lu);
        Assert.Equal(("03 85 00 00 00", false, TypePrestataire.Organisme), ((await depot.ObtenirAsync(id))!.Telephone, (await depot.ObtenirAsync(id))!.Actif, (await depot.ObtenirAsync(id))!.Type));
    }

    [Fact]
    public async Task Journal_d_un_equipement_par_actions()
    {
        var journal = new JournalRepository(_base.Fabrique);
        var det = await EquipementAsync("A10");
        await journal.AjouterAsync(new EntreeJournal { Horodatage = DateTime.UtcNow, Action = ActionsJournal.RevisionEtape, Details = "A10 : réparation au club", EquipementId = det });
        await journal.AjouterAsync(new EntreeJournal { Horodatage = DateTime.UtcNow, Action = ActionsJournal.ScanRetour, Details = "A10", EquipementId = det });
        await journal.AjouterAsync(new EntreeJournal { Horodatage = DateTime.UtcNow, Action = ActionsJournal.RevisionEtape, Details = "autre", EquipementId = null });

        var lignes = await journal.ListerPourEquipementAsync(det, [ActionsJournal.RevisionEtape, ActionsJournal.AjoutSeanceTiv]);

        Assert.Equal(["A10 : réparation au club"], lignes.Select(l => l.Details));
    }

    [Fact]
    public async Task L_export_contient_prestataires_et_revisions()
    {
        await new PrestataireRepository(_base.Fabrique).AjouterAsync(new Prestataire { Nom = "Aqua Service" });
        await new RevisionRepository(_base.Fabrique).AjouterSuiviAsync(new SuiviRevision { EquipementId = await EquipementAsync("A10"), Etape = EtapeRevision.Interne, CreeLe = DateTime.UtcNow });

        var tables = await new ExportRepository(_base.Fabrique).ListerTablesAsync();

        Assert.Equal("Aqua Service", tables.Single(t => t.Fichier == "prestataires.csv").Lignes.Single()[0]);
        Assert.Equal(["A10", "A10", "Interne"], tables.Single(t => t.Fichier == "revisions.csv").Lignes.Single()[..3].Select(v => v ?? ""));
    }

    // 018 : interventions prévues — une seule ouverte par équipement (index partiel), formats stockés, fermeture.
    [Fact]
    public async Task Les_interventions_prevues_sont_enregistrees_relues_et_fermees()
    {
        var depot = new RevisionRepository(_base.Fabrique);
        var a10 = await EquipementAsync("A10");
        var a11 = await EquipementAsync("A11");
        var le = new DateTime(2026, 11, 14, 9, 30, 0, DateTimeKind.Utc);
        var prevue = new InterventionPrevue { EquipementId = a10, Motif = "Changer le joint", Description = "Fuite légère", PrevueLe = le };
        await depot.AjouterPrevueAsync(prevue);
        await depot.AjouterPrevueAsync(new InterventionPrevue { EquipementId = a11, Motif = "Vérifier l'inflateur", PrevueLe = le.AddMinutes(1) });
        await Assert.ThrowsAsync<SqliteException>(() => depot.AjouterPrevueAsync(new InterventionPrevue { EquipementId = a10, Motif = "Doublon", PrevueLe = le }));

        prevue.Motif = "Changer les joints";
        await depot.MettreAJourPrevueAsync(prevue);
        var relue = (await depot.ObtenirPrevueOuverteAsync(a10))!;
        Assert.Equal(("Changer les joints", "Fuite légère", le), (relue.Motif, relue.Description, relue.PrevueLe));
        Assert.Equal(["A10", "A11"], (await depot.ListerPrevuesOuvertesAsync()).Select(p => p.EquipementId == a10 ? "A10" : "A11"));

        Assert.Equal(1, await depot.FermerPrevueAsync(a10, le.AddDays(1), annulee: true));
        Assert.Null(await depot.ObtenirPrevueOuverteAsync(a10));
        Assert.Equal(0, await depot.FermerPrevueAsync(a10, le.AddDays(1), annulee: false));
        await using var cx = await _base.Fabrique.OuvrirAsync();
        Assert.Equal(("2026-11-14T09:30:00.000Z", "2026-11-15T09:30:00.000Z", 1L), await cx.QuerySingleAsync<(string, string, long)>(
            "SELECT prevue_le, close_le, annulee FROM intervention_prevue WHERE equipement_id = @a10;", new { a10 }));
        await depot.AjouterPrevueAsync(new InterventionPrevue { EquipementId = a10, Motif = "De nouveau", PrevueLe = le }); // Une fois fermée, une autre.
    }
}
