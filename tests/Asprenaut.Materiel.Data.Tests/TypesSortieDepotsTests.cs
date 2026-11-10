using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Data.Connexion;
using Asprenaut.Materiel.Data.Depots;
using Asprenaut.Materiel.Data.Migrations;
using Dapper;

namespace Asprenaut.Materiel.Data.Tests;

public class TypesSortieDepotsTests : IAsyncLifetime
{
    private readonly BaseTemporaire _base = new();

    public Task InitializeAsync() => _base.InitializeAsync();

    public Task DisposeAsync() => _base.DisposeAsync();

    private sealed class HorlogeFixe : IHorloge
    {
        public DateTime MaintenantUtc => new(2026, 11, 6, 18, 30, 0, DateTimeKind.Utc);

        public DateOnly AujourdHui => DateOnly.FromDateTime(MaintenantUtc);
    }

    // Règle : la migration 004 crée les six types d'origine et y relie les sorties existantes d'après leur famille.
    [Fact]
    public async Task La_migration_relie_les_sorties_existantes_a_leur_type()
    {
        var fabrique = new FabriqueConnexion(Path.Combine(_base.Dossier, "ancienne.db"));
        var migrations = new GestionnaireMigrations(fabrique);
        Assert.Equal(3, await migrations.AppliquerAsync(jusqua: 3));
        await using (var cx = await fabrique.OuvrirAsync())
        {
            await cx.ExecuteAsync("INSERT INTO moniteur (nom, prenom) VALUES ('Durand', 'Pascal');");
            await cx.ExecuteAsync("""
                INSERT INTO sortie (date_sortie, type, intitule, moniteur_id) VALUES
                    ('2026-06-14', 'Mer', 'Sortie mer', 1), ('2026-06-20', 'Bapteme', 'Baptêmes', 1);
                """);
        }

        Assert.Equal(GestionnaireMigrations.VersionCible, await migrations.AppliquerAsync());

        var types = await new TypeSortieRepository(fabrique).ListerAsync();
        Assert.Equal(["Piscine", "Fosse", "Mer", "Lac", "Baptême", "Autre"], types.Select(t => t.Nom));
        Assert.Equal((TypeSortie.Bapteme, 1), (types[4].Pictogramme, types[4].NbSorties));
        var resumes = await new SortieRepository(fabrique).ListerResumesAsync(cloturees: false);
        Assert.Equal(["Mer", "Baptême"], resumes.OrderBy(s => s.DateSortie).Select(s => s.LibelleType));
    }

    // Règle : la migration 006 donne mer 4 et lac 2 ; les sorties existantes prennent les utilisations de leur type.
    [Fact]
    public async Task La_migration_006_reprend_les_utilisations_et_le_compteur_les_additionne()
    {
        var fabrique = new FabriqueConnexion(Path.Combine(_base.Dossier, "avant006.db"));
        var migrations = new GestionnaireMigrations(fabrique);
        Assert.Equal(5, await migrations.AppliquerAsync(jusqua: 5));
        await using (var cx = await fabrique.OuvrirAsync())
        {
            await cx.ExecuteAsync("INSERT INTO moniteur (nom, prenom) VALUES ('Durand', 'Pascal');");
            await cx.ExecuteAsync("""
                INSERT INTO equipement (code, libelle, categorie_id, cree_le) VALUES ('DET-001', 'Détendeur', 1, '2026-01-01T00:00:00.000Z');
                INSERT INTO sortie (date_sortie, type, type_sortie_id, intitule, moniteur_id, statut)
                    VALUES ('2026-06-14', 'Mer', (SELECT id FROM type_sortie WHERE nom = 'Mer'), 'Sortie mer', 1, 'Cloturee'),
                           ('2026-06-20', 'Piscine', (SELECT id FROM type_sortie WHERE nom = 'Piscine'), 'Piscine', 1, 'Cloturee');
                INSERT INTO sortie_equipement (sortie_id, equipement_id, scanne_depart_le, scanne_retour_le, statut)
                    SELECT id, 1, '2026-06-14T08:00:00.000Z', '2026-06-14T18:00:00.000Z', 'Rendu' FROM sortie;
                """);
        }

        await migrations.AppliquerAsync();

        var types = await new TypeSortieRepository(fabrique).ListerAsync();
        Assert.Equal([1, 1, 4, 2, 1, 1], types.Select(t => t.Utilisations));
        var sorties = await new SortieRepository(fabrique).ListerResumesAsync(cloturees: true);
        Assert.Equal([(4, "Sortie mer"), (1, "Piscine")], sorties.OrderBy(s => s.DateSortie).Select(s => (s.Utilisations, s.Intitule)));
        Assert.Equal(5, (await new EquipementRepository(fabrique).ListerCompteursAsync(1)).Single().Utilisations); // 4 + 1.
    }

    [Fact]
    public async Task Les_utilisations_du_type_et_de_la_sortie_sont_enregistrees()
    {
        var depot = new TypeSortieRepository(_base.Fabrique);
        var service = new ServiceTypesSortie(depot);
        var bateau = (await service.CreerAsync("Sortie bateau", TypeSortie.Mer)).Id!.Value;
        Assert.Equal(4, (await depot.ObtenirAsync(bateau))!.Utilisations);
        Assert.True((await service.ChangerUtilisationsAsync(bateau, 3)).Reussi);

        var moniteurs = new MoniteurRepository(_base.Fabrique);
        var pascal = await moniteurs.AjouterAsync(new Moniteur { Nom = "Durand", Prenom = "Pascal" });
        var sorties = new SortieRepository(_base.Fabrique);
        var serviceSortie = new ServiceSortie(sorties, moniteurs, new JournalRepository(_base.Fabrique), new HorlogeFixe(), new Reglages(), null, depot);
        var defaut = (await serviceSortie.CreerAsync(new NouvelleSortie(new DateOnly(2026, 11, 7), TypeSortie.Autre, "Bateau", pascal, TypeId: bateau))).Id!.Value;
        var double_ = (await serviceSortie.CreerAsync(new NouvelleSortie(new DateOnly(2026, 11, 8), TypeSortie.Autre, "Deux plongées", pascal, TypeId: bateau, Utilisations: 6))).Id!.Value;

        await using var cx = await _base.Fabrique.OuvrirAsync();
        Assert.Equal((3L, 6L), (await cx.ExecuteScalarAsync<long>("SELECT utilisations FROM sortie WHERE id = @defaut;", new { defaut }),
            await cx.ExecuteScalarAsync<long>("SELECT utilisations FROM sortie WHERE id = @double_;", new { double_ })));
        Assert.Equal(6, (await sorties.ObtenirAsync(double_))!.Utilisations);
        Assert.Equal(3L, await cx.ExecuteScalarAsync<long>("SELECT utilisations FROM type_sortie WHERE id = @bateau;", new { bateau }));
    }

    [Fact]
    public async Task Les_types_s_ajoutent_se_modifient_et_ne_se_suppriment_que_s_ils_sont_inutilises()
    {
        var depot = new TypeSortieRepository(_base.Fabrique);
        var service = new ServiceTypesSortie(depot);
        var bateau = (await service.CreerAsync("Sortie bateau", TypeSortie.Mer)).Id!.Value;
        Assert.True((await service.ModifierAsync(bateau, "Bateau", TypeSortie.Mer, actif: false)).Reussi);

        await using (var cx = await _base.Fabrique.OuvrirAsync())
        {
            Assert.Equal(("Bateau", "Mer", 0L, 7L), await cx.QuerySingleAsync<(string, string, long, long)>(
                "SELECT nom, pictogramme, actif, ordre FROM type_sortie WHERE id = @bateau;", new { bateau }));
        }

        // Une sortie de ce type : SQLite garde le lien, le résumé lit le nom, la suppression est refusée.
        var moniteurs = new MoniteurRepository(_base.Fabrique);
        var pascal = await moniteurs.AjouterAsync(new Moniteur { Nom = "Durand", Prenom = "Pascal" });
        var sorties = new SortieRepository(_base.Fabrique);
        var serviceSortie = new ServiceSortie(sorties, moniteurs, new JournalRepository(_base.Fabrique), new HorlogeFixe(), new Reglages(), null, depot);
        Assert.True((await service.ModifierAsync(bateau, "Bateau", TypeSortie.Mer, actif: true)).Reussi);
        var id = (await serviceSortie.CreerAsync(new NouvelleSortie(new DateOnly(2026, 11, 7), TypeSortie.Autre, "Île Verte", pascal, TypeId: bateau))).Id!.Value;

        var resume = (await sorties.ObtenirResumeAsync(id))!;
        Assert.Equal((bateau, "Bateau", TypeSortie.Mer), (resume.TypeId, resume.TypeNom, resume.Type));
        Assert.Equal(bateau, (await sorties.ObtenirAsync(id))!.TypeId);
        Assert.False(await depot.SupprimerAsync(bateau));
        Assert.Equal(1, (await depot.ObtenirAsync(bateau))!.NbSorties);

        var nuit = (await service.CreerAsync("Plongée de nuit", TypeSortie.Autre)).Id!.Value;
        Assert.True(await depot.SupprimerAsync(nuit));
        Assert.Null(await depot.ObtenirAsync(nuit));

        // Historique et export : le nom du type du club.
        Assert.Equal("Bateau", (await new JournalRepository(_base.Fabrique).ListerAsync(null, 10)).First(l => l.SortieId == id).SortieTypeNom);
        var csv = (await new ServiceExport(new ExportRepository(_base.Fabrique), new HorlogeFixe()).ExporterAsync()).Single(f => f.Nom == "sorties.csv").Contenu;
        Assert.Contains(";07/11/2026;Bateau;Île Verte;", csv, StringComparison.Ordinal);
    }
}
