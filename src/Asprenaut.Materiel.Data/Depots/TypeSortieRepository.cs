using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Data.Connexion;
using Dapper;

namespace Asprenaut.Materiel.Data.Depots;

/// <summary>Types de sortie (table type_sortie, migration 004).</summary>
public sealed class TypeSortieRepository(FabriqueConnexion fabrique) : ITypeSortieRepository
{
    private const string Select = """
        SELECT t.id, t.nom, t.pictogramme, t.actif, t.ordre, t.utilisations, t.nominatif,
               (SELECT COUNT(*) FROM sortie s WHERE s.type_sortie_id = t.id) AS nb_sorties
        FROM type_sortie t
        """;

    public async Task<IReadOnlyList<TypeDeSortie>> ListerAsync(CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<TypeDeSortie>(new CommandDefinition($"{Select} ORDER BY t.ordre, t.nom;", cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task<TypeDeSortie?> ObtenirAsync(long id, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.QuerySingleOrDefaultAsync<TypeDeSortie>(new CommandDefinition($"{Select} WHERE t.id = @id;", new { id }, cancellationToken: ct));
    }

    public async Task<long> AjouterAsync(TypeDeSortie type, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        type.Id = await cx.ExecuteScalarAsync<long>(new CommandDefinition(
            "INSERT INTO type_sortie (nom, pictogramme, actif, ordre, utilisations, nominatif) VALUES (@Nom, @Pictogramme, @Actif, @Ordre, @Utilisations, @Nominatif) RETURNING id;",
            Parametres(type), cancellationToken: ct));
        return type.Id;
    }

    public async Task MettreAJourAsync(TypeDeSortie type, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        await cx.ExecuteAsync(new CommandDefinition(
            "UPDATE type_sortie SET nom = @Nom, pictogramme = @Pictogramme, actif = @Actif, ordre = @Ordre, utilisations = @Utilisations, nominatif = @Nominatif WHERE id = @Id;",
            Parametres(type), cancellationToken: ct));
    }

    public async Task<bool> SupprimerAsync(long id, CancellationToken ct = default)
    {
        // La condition est dans la requête : une sortie créée entre la vérification du service et ici empêche la suppression.
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.ExecuteAsync(new CommandDefinition(
            "DELETE FROM type_sortie WHERE id = @id AND NOT EXISTS (SELECT 1 FROM sortie WHERE type_sortie_id = @id);",
            new { id }, cancellationToken: ct)) == 1;
    }

    private static object Parametres(TypeDeSortie t) => new
    {
        t.Id,
        t.Nom,
        Pictogramme = t.Pictogramme.ToString(),
        Actif = t.Actif ? 1 : 0,
        t.Ordre,
        t.Utilisations,
        Nominatif = t.Nominatif ? 1 : 0,
    };
}
