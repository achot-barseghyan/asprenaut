using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Data.Connexion;
using Dapper;

namespace Asprenaut.Materiel.Data.Depots;

/// <summary>Modèles de sortie (table modele_sortie, migration 024).</summary>
public sealed class ModeleSortieRepository(FabriqueConnexion fabrique) : IModeleSortieRepository
{
    private const string Select = """
        SELECT id, nom, jour, type_sortie_id AS TypeId, lieu, intitule, moniteur_id AS MoniteurId, utilisations,
               un_par_categorie AS UnParCategorie, notes
        FROM modele_sortie
        """;

    public async Task<IReadOnlyList<ModeleSortie>> ListerAsync(CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<ModeleSortie>(new CommandDefinition($"{Select} ORDER BY nom COLLATE NOCASE;", cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task<ModeleSortie?> ObtenirAsync(long id, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.QuerySingleOrDefaultAsync<ModeleSortie>(new CommandDefinition($"{Select} WHERE id = @id;", new { id }, cancellationToken: ct));
    }

    public async Task<long> AjouterAsync(ModeleSortie modele, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        modele.Id = await cx.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO modele_sortie (nom, jour, type_sortie_id, lieu, intitule, moniteur_id, utilisations, un_par_categorie, notes, cree_le)
            VALUES (@Nom, @Jour, @TypeId, @Lieu, @Intitule, @MoniteurId, @Utilisations, @UnParCategorie, @Notes, @CreeLe)
            RETURNING id;
            """,
            Parametres(modele), cancellationToken: ct));
        return modele.Id;
    }

    public async Task MettreAJourAsync(ModeleSortie modele, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        await cx.ExecuteAsync(new CommandDefinition(
            """
            UPDATE modele_sortie SET nom = @Nom, jour = @Jour, type_sortie_id = @TypeId, lieu = @Lieu, intitule = @Intitule,
                   moniteur_id = @MoniteurId, utilisations = @Utilisations, un_par_categorie = @UnParCategorie, notes = @Notes
            WHERE id = @Id;
            """,
            Parametres(modele), cancellationToken: ct));
    }

    public async Task<bool> SupprimerAsync(long id, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.ExecuteAsync(new CommandDefinition("DELETE FROM modele_sortie WHERE id = @id;", new { id }, cancellationToken: ct)) == 1;
    }

    // Dapper ignore les TypeHandler en paramètre : jour en entier (0 = dimanche), horodatage en texte ISO.
    private static object Parametres(ModeleSortie m) => new
    {
        m.Id,
        m.Nom,
        Jour = m.Jour is { } j ? (int?)j : null,
        m.TypeId,
        m.Lieu,
        m.Intitule,
        m.MoniteurId,
        m.Utilisations,
        UnParCategorie = m.UnParCategorie ? 1 : 0,
        m.Notes,
        CreeLe = FormatIso.Ecrire(DateTime.UtcNow),
    };
}
