using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Data.Connexion;
using Dapper;

namespace Asprenaut.Materiel.Data.Depots;

/// <summary>Manuels des fabricants (table manuel, migration 009).</summary>
public sealed class ManuelRepository(FabriqueConnexion fabrique) : IManuelRepository
{
    // Marque comparée sans majuscules, espaces, tirets ni points : « Aqua Lung » = « Aqualung » = « AQUA-LUNG ».
    private const string MarqueE = "REPLACE(REPLACE(REPLACE(LOWER(TRIM(e.marque)), ' ', ''), '-', ''), '.', '')";
    private const string MarqueM = "REPLACE(REPLACE(REPLACE(LOWER(TRIM(m.marque)), ' ', ''), '-', ''), '.', '')";

    // Équipements liés : même catégorie, même marque (voir MarqueE), hors réformés.
    private const string Lies = $"""
        FROM equipement e
        WHERE e.categorie_id = m.categorie_id AND e.etat <> 'Reforme'
          AND m.marque <> '' AND {MarqueE} = {MarqueM}
        """;

    private const string Select = $"""
        SELECT m.id, m.marque, m.titre, m.categorie_id, c.nom AS categorie, m.modeles, m.edition, m.fichier, m.nb_pages,
               m.mois_revision, m.page_entretien, m.entretien, m.sommaire,
               (SELECT COUNT(*) {Lies}) AS nb_equipements
        FROM manuel m
        LEFT JOIN categorie c ON c.id = m.categorie_id
        """;

    public async Task<IReadOnlyList<Manuel>> ListerAsync(CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<Manuel>(new CommandDefinition(
            $"{Select} ORDER BY m.marque COLLATE NOCASE, m.titre COLLATE NOCASE;", cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task<Manuel?> ObtenirAsync(long id, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.QuerySingleOrDefaultAsync<Manuel>(new CommandDefinition($"{Select} WHERE m.id = @id;", new { id }, cancellationToken: ct));
    }

    public async Task<long> AjouterAsync(Manuel manuel, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        manuel.Id = await cx.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO manuel (marque, titre, categorie_id, modeles, edition, fichier, nb_pages, mois_revision, page_entretien, entretien, sommaire)
            VALUES (@Marque, @Titre, @CategorieId, @Modeles, @Edition, @Fichier, @NbPages, @MoisRevision, @PageEntretien, @Entretien, @Sommaire)
            RETURNING id;
            """, Parametres(manuel), cancellationToken: ct));
        return manuel.Id;
    }

    public async Task MettreAJourAsync(Manuel manuel, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        await cx.ExecuteAsync(new CommandDefinition("""
            UPDATE manuel SET marque = @Marque, titre = @Titre, categorie_id = @CategorieId, modeles = @Modeles, edition = @Edition,
                fichier = @Fichier, nb_pages = @NbPages, mois_revision = @MoisRevision, page_entretien = @PageEntretien,
                entretien = @Entretien, sommaire = @Sommaire
            WHERE id = @Id;
            """, Parametres(manuel), cancellationToken: ct));
    }

    public async Task SupprimerAsync(long id, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        await cx.ExecuteAsync(new CommandDefinition("DELETE FROM manuel WHERE id = @id;", new { id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<string>> ListerCodesEquipementsAsync(long manuelId, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        var codes = await cx.QueryAsync<string>(new CommandDefinition(
            $"""
            SELECT e.code
            FROM manuel m
            JOIN equipement e ON e.categorie_id = m.categorie_id AND e.etat <> 'Reforme'
                             AND m.marque <> '' AND {MarqueE} = {MarqueM}
            WHERE m.id = @manuelId
            ORDER BY e.code;
            """,
            new { manuelId }, cancellationToken: ct));
        return codes.AsList();
    }

    public async Task<IReadOnlyDictionary<long, long>> ListerManuelsDesEquipementsAsync(CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<(long EquipementId, long ManuelId)>(new CommandDefinition(
            $"""
            SELECT e.id, MIN(m.id)
            FROM equipement e
            JOIN manuel m ON m.categorie_id = e.categorie_id AND m.marque <> '' AND {MarqueE} = {MarqueM}
            GROUP BY e.id;
            """, cancellationToken: ct));
        return lignes.ToDictionary(l => l.EquipementId, l => l.ManuelId);
    }

    private static object Parametres(Manuel m) => new
    {
        m.Id,
        m.Marque,
        m.Titre,
        m.CategorieId,
        m.Modeles,
        m.Edition,
        m.Fichier,
        m.NbPages,
        m.MoisRevision,
        m.PageEntretien,
        m.Entretien,
        m.Sommaire,
    };
}
