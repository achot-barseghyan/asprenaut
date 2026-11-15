using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Data.Depots;
using Dapper;

namespace Asprenaut.Materiel.Data.Tests;

/// <summary>Onglet « Clôturées » de l'écran Sorties : filtres, tri et totaux en SQL.</summary>
public class SortiesClotureesDepotsTests : IAsyncLifetime
{
    private readonly BaseTemporaire _base = new();

    public Task InitializeAsync() => _base.InitializeAsync();

    public Task DisposeAsync() => _base.DisposeAsync();

    private static CritereSorties Tout(TriSorties tri = TriSorties.Date, bool descendant = true) =>
        new(null, null, null, null, BilanSorties.Tous, tri, descendant);

    private async Task<(long Piscine, long Mer, long Pascal, long Classe)> PreparerAsync()
    {
        await using var cx = await _base.Fabrique.OuvrirAsync();
        await cx.ExecuteAsync("INSERT INTO moniteur (prenom, nom) VALUES ('Pascal', 'Martin');");
        await cx.ExecuteAsync("INSERT INTO moniteur (prenom, nom, genre) VALUES ('', 'Niveau 2 bis', 'Classe');");
        var pascal = await cx.ExecuteScalarAsync<long>("SELECT id FROM moniteur WHERE prenom = 'Pascal';");
        var classe = await cx.ExecuteScalarAsync<long>("SELECT id FROM moniteur WHERE nom = 'Niveau 2 bis';");
        var piscine = await cx.ExecuteScalarAsync<long>("SELECT id FROM type_sortie WHERE nom = 'Piscine';");
        var mer = await cx.ExecuteScalarAsync<long>("SELECT id FROM type_sortie WHERE nom = 'Mer';");
        for (var i = 1; i <= 4; i++)
        {
            await cx.ExecuteAsync($"INSERT INTO equipement (code, libelle, categorie_id, cree_le) VALUES ('BLOC-03{i}', 'Bloc', 1, '2026-01-01T00:00:00.000Z');");
        }

        // Cours (piscine, Pascal, 1 équipement), Sortie mer (Marseille, classe, 3 équipements dont 1 manquant),
        // Vieille sortie (2025, piscine, Pascal, 0 équipement) et une sortie en cours (jamais listée).
        await cx.ExecuteAsync("""
            INSERT INTO sortie (date_sortie, type, type_sortie_id, intitule, lieu, moniteur_id, statut, cloturee_le) VALUES
                ('2026-10-30', 'Piscine', @piscine, 'Cours du jeudi', 'Piscine Chalon', @pascal, 'Cloturee', '2026-10-30T20:00:00.000Z'),
                ('2026-11-02', 'Mer', @mer, 'Week-end', 'Marseille', @classe, 'Cloturee', '2026-11-03T18:00:00.000Z'),
                ('2025-06-01', 'Piscine', @piscine, 'Vieille sortie', '', @pascal, 'Cloturee', '2025-06-01T18:00:00.000Z'),
                ('2026-11-05', 'Piscine', @piscine, 'En cours', '', @pascal, 'EnCours', NULL);
            INSERT INTO sortie_equipement (sortie_id, equipement_id, statut, scanne_depart_le) VALUES
                ((SELECT id FROM sortie WHERE intitule = 'Cours du jeudi'), 1, 'Rendu', '2026-10-30T08:00:00.000Z'),
                ((SELECT id FROM sortie WHERE intitule = 'Week-end'), 2, 'Rendu', '2026-11-02T08:00:00.000Z'),
                ((SELECT id FROM sortie WHERE intitule = 'Week-end'), 3, 'Rendu', '2026-11-02T08:00:00.000Z'),
                ((SELECT id FROM sortie WHERE intitule = 'Week-end'), 4, 'Manquant', '2026-11-02T08:00:00.000Z');
            """, new { piscine, mer, pascal, classe });
        return (piscine, mer, pascal, classe);
    }

    [Fact]
    public async Task Les_filtres_reduisent_la_liste_des_sorties_cloturees()
    {
        var (piscine, mer, pascal, classe) = await PreparerAsync();
        var depot = new SortieRepository(_base.Fabrique);
        async Task<string[]> Intitules(CritereSorties c) => (await depot.ListerClotureesAsync(c, 0, 50)).Select(s => s.Intitule).ToArray();

        Assert.Equal(["Week-end", "Cours du jeudi", "Vieille sortie"], await Intitules(Tout())); // La plus récente d'abord.
        Assert.Equal(["Week-end"], await Intitules(Tout() with { Recherche = "marseille" })); // Lieu, sans les majuscules.
        Assert.Equal(["Week-end"], await Intitules(Tout() with { Recherche = "niveau 2" })); // Responsable.
        Assert.Equal(["Cours du jeudi", "Vieille sortie"], await Intitules(Tout() with { ResponsableId = pascal }));
        Assert.Equal(["Week-end"], await Intitules(Tout() with { TypeId = mer }));
        Assert.Equal(["Week-end"], await Intitules(Tout() with { Bilan = BilanSorties.Manquants }));
        Assert.Equal(["Cours du jeudi", "Vieille sortie"], await Intitules(Tout() with { Bilan = BilanSorties.ToutRendu }));
        Assert.Equal(["Week-end", "Cours du jeudi"], await Intitules(Tout() with { Depuis = new DateOnly(2026, 9, 1) }));
        Assert.Empty(await Intitules(Tout() with { ResponsableId = classe, TypeId = piscine }));
    }

    [Fact]
    public async Task Le_tri_et_les_totaux_des_sorties_cloturees()
    {
        var (piscine, mer, _, _) = await PreparerAsync();
        var depot = new SortieRepository(_base.Fabrique);

        Assert.Equal(["Week-end", "Cours du jeudi", "Vieille sortie"], (await depot.ListerClotureesAsync(Tout(TriSorties.Materiel), 0, 50)).Select(s => s.Intitule));
        Assert.Equal(["Cours du jeudi", "Vieille sortie", "Week-end"], (await depot.ListerClotureesAsync(Tout(TriSorties.Sortie, descendant: false), 0, 50)).Select(s => s.Intitule));
        Assert.Equal(["Vieille sortie"], (await depot.ListerClotureesAsync(Tout(TriSorties.Date, descendant: false), 0, 1)).Select(s => s.Intitule));
        Assert.Equal((3, 1), (await depot.ListerClotureesAsync(Tout(), 0, 1)).Select(s => (s.NbTotal, s.NbManquants)).Single());

        // Totaux du filtre (type compris) ; par type sans le filtre de type, pour les puces.
        var totaux = await depot.TotaliserClotureesAsync(Tout() with { TypeId = mer });
        Assert.Equal((1, 3), (totaux.NbSorties, totaux.NbEquipements));
        Assert.Equal([(piscine, "Piscine", 2), (mer, "Mer", 1)], totaux.ParType.Select(t => (t.TypeId!.Value, t.Nom, t.Nombre)));
        Assert.Equal(TypeSortie.Mer, totaux.ParType.Single(t => t.TypeId == mer).Famille);
    }

    [Fact]
    public async Task Les_lieux_recents_sans_doublon_de_la_sortie_la_plus_recente()
    {
        var (piscine, _, pascal, _) = await PreparerAsync();
        await using (var cx = await _base.Fabrique.OuvrirAsync())
        {
            // Même lieu en minuscules, plus ancien : ne crée pas de doublon et ne fait pas reculer « Piscine Chalon ».
            await cx.ExecuteAsync("""
                INSERT INTO sortie (date_sortie, type, type_sortie_id, intitule, lieu, moniteur_id, statut) VALUES
                    ('2026-09-10', 'Piscine', @piscine, 'Rentrée', ' piscine chalon ', @pascal, 'Cloturee');
                """, new { piscine, pascal });
        }

        var depot = new SortieRepository(_base.Fabrique);
        var lieux = await depot.ListerLieuxRecentsAsync(5);
        Assert.Equal(["MARSEILLE", "PISCINE CHALON"], lieux.Select(l => l.ToUpperInvariant())); // Lieux vides ignorés.
        Assert.Single(await depot.ListerLieuxRecentsAsync(1));
    }
}
