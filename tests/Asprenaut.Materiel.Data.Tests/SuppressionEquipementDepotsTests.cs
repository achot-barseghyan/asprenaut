using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Data.Depots;
using Dapper;

namespace Asprenaut.Materiel.Data.Tests;

/// <summary>Suppression d'un équipement jamais utilisé (corbeille de l'inventaire).</summary>
public class SuppressionEquipementDepotsTests : IAsyncLifetime
{
    private readonly BaseTemporaire _base = new();

    public Task InitializeAsync() => _base.InitializeAsync();

    public Task DisposeAsync() => _base.DisposeAsync();

    private sealed class HorlogeFixe : IHorloge
    {
        public DateTime MaintenantUtc => new(2026, 11, 6, 18, 30, 0, DateTimeKind.Utc);

        public DateOnly AujourdHui => DateOnly.FromDateTime(MaintenantUtc);
    }

    private ServiceInventaire Inventaire(EquipementRepository depot) =>
        new(depot, new CategorieRepository(_base.Fabrique), new JournalRepository(_base.Fabrique), new HorlogeFixe(), new Reglages(),
            null, new SortieRepository(_base.Fabrique), new RevisionRepository(_base.Fabrique));

    private async Task<long> CreerAsync(ServiceInventaire inventaire, string code, DateOnly? derniereRevision = null)
    {
        var detendeurs = (await new CategorieRepository(_base.Fabrique).ListerAsync()).Single(c => c.Nom == "Détendeurs").Id;
        var r = await inventaire.CreerAsync(new Equipement { Code = code, Libelle = "Détendeur adulte", CategorieId = detendeurs }, derniereRevision);
        Assert.True(r.Reussi, r.Message);
        return r.Id!.Value;
    }

    [Fact]
    public async Task Un_equipement_jamais_utilise_est_supprime_avec_ses_controles_composants_et_signalements()
    {
        var depot = new EquipementRepository(_base.Fabrique);
        var inventaire = Inventaire(depot);
        var id = await CreerAsync(inventaire, "A05", new DateOnly(2026, 3, 1)); // Révision saisie : un contrôle.
        await depot.EnregistrerComposantAsync(new DetailComposant { EquipementId = id, Role = "Octopus", Origine = OrigineComposant.Neuve });
        await using (var cx = await _base.Fabrique.OuvrirAsync())
        {
            await cx.ExecuteAsync(
                "INSERT INTO signalement (equipement_id, cause, gravite, signale_le) VALUES (@id, 'Fuite', 'ASurveiller', '2026-11-06T18:30:00.000Z');",
                new { id });
        }

        Assert.True((await depot.ListerInventaireAsync(null, null)).Single(l => l.Code == "A05").JamaisUtilise);

        var r = await inventaire.SupprimerAsync(id);

        Assert.True(r.Reussi, r.Message);
        Assert.Null(await depot.ObtenirAsync(id));
        await using var lecture = await _base.Fabrique.OuvrirAsync();
        Assert.Equal(0, await lecture.ExecuteScalarAsync<long>(
            "SELECT (SELECT COUNT(*) FROM controle) + (SELECT COUNT(*) FROM composant) + (SELECT COUNT(*) FROM signalement);"));
        // Le journal garde la création et la suppression, sans lien vers l'équipement disparu.
        Assert.Equal(0, await lecture.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM journal WHERE equipement_id = @id;", new { id }));
        Assert.Equal(1, await lecture.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM journal WHERE action = 'EquipementSupprime';"));
    }

    [Fact]
    public async Task Un_equipement_deja_sorti_ou_au_compteur_repris_n_est_pas_supprime()
    {
        var depot = new EquipementRepository(_base.Fabrique);
        var inventaire = Inventaire(depot);
        var sorti = await CreerAsync(inventaire, "A06");
        var repris = await CreerAsync(inventaire, "A07");
        await using (var cx = await _base.Fabrique.OuvrirAsync())
        {
            await cx.ExecuteAsync("""
                INSERT INTO moniteur (nom, prenom) VALUES ('Martin', 'Pascal');
                INSERT INTO sortie (date_sortie, type, intitule, moniteur_id) VALUES ('2026-11-01', 'Piscine', 'Piscine', last_insert_rowid());
                INSERT INTO sortie_equipement (sortie_id, equipement_id, scanne_depart_le, scanne_retour_le, statut)
                VALUES (last_insert_rowid(), @sorti, '2026-11-01T18:00:00.000Z', '2026-11-01T21:00:00.000Z', 'Rendu');
                """, new { sorti });
        }

        await depot.EnregistrerRepriseAsync(new RepriseUtilisations(repris, 12, new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc)));

        var lignes = await depot.ListerInventaireAsync(null, null);
        Assert.False(lignes.Single(l => l.Code == "A06").JamaisUtilise);
        Assert.False(lignes.Single(l => l.Code == "A07").JamaisUtilise);
        Assert.False((await inventaire.SupprimerAsync(sorti)).Reussi);
        Assert.False((await inventaire.SupprimerAsync(repris)).Reussi);
        Assert.NotNull(await depot.ObtenirAsync(sorti));
        Assert.NotNull(await depot.ObtenirAsync(repris));
    }
}
