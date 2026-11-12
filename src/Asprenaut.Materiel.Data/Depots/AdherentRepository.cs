using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Data.Connexion;
using Dapper;

namespace Asprenaut.Materiel.Data.Depots;

/// <summary>Adhérents du club (table adherent, migration 014) et le matériel qu'ils ont pris (sortie_equipement.adherent_id).</summary>
public sealed class AdherentRepository(FabriqueConnexion fabrique) : IAdherentRepository
{
    private const string Colonnes = "id, nom, prenom, niveau, licence, telephone, actif, nitrox";

    public async Task<IReadOnlyList<Adherent>> ListerAsync(bool actifsSeulement, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<Adherent>(new CommandDefinition(
            $"SELECT {Colonnes} FROM adherent WHERE (@actifsSeulement = 0 OR actif = 1) ORDER BY nom, prenom;",
            new { actifsSeulement }, cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task<Adherent?> ObtenirAsync(long id, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.QuerySingleOrDefaultAsync<Adherent>(new CommandDefinition(
            $"SELECT {Colonnes} FROM adherent WHERE id = @id;", new { id }, cancellationToken: ct));
    }

    public async Task<long> AjouterAsync(Adherent adherent, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        adherent.Id = await cx.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO adherent (nom, prenom, niveau, licence, telephone, actif, nitrox)
            VALUES (@Nom, @Prenom, @Niveau, @Licence, @Telephone, @Actif, @Nitrox) RETURNING id;
            """,
            Parametres(adherent), cancellationToken: ct));
        return adherent.Id;
    }

    public async Task MettreAJourAsync(Adherent adherent, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        await cx.ExecuteAsync(new CommandDefinition(
            """
            UPDATE adherent SET nom = @Nom, prenom = @Prenom, niveau = @Niveau, licence = @Licence, telephone = @Telephone, actif = @Actif, nitrox = @Nitrox
            WHERE id = @Id;
            """,
            Parametres(adherent), cancellationToken: ct));
    }

    public async Task<IReadOnlyList<SituationAdherent>> ListerSituationsAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT adherent_id,
                   SUM(statut = 'Dehors') AS nb_dehors,
                   SUM(statut = 'Manquant') AS nb_manquants
            FROM sortie_equipement
            WHERE adherent_id IS NOT NULL
            GROUP BY adherent_id
            HAVING SUM(statut IN ('Dehors', 'Manquant')) > 0;
            """;
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<SituationAdherent>(new CommandDefinition(sql, cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task<IReadOnlyList<PassageAdherent>> ListerPassagesAsync(long adherentId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT se.sortie_id, s.intitule, s.date_sortie, ts.nom AS type_nom, s.type AS type_sortie, s.statut AS statut_sortie,
                   se.equipement_id, e.code, e.libelle, c.nom AS categorie, se.statut, se.scanne_depart_le, se.scanne_retour_le
            FROM sortie_equipement se
            JOIN sortie s ON s.id = se.sortie_id
            JOIN equipement e ON e.id = se.equipement_id
            JOIN categorie c ON c.id = e.categorie_id
            LEFT JOIN type_sortie ts ON ts.id = s.type_sortie_id
            WHERE se.adherent_id = @adherentId
            ORDER BY s.date_sortie DESC, s.id DESC, se.scanne_depart_le;
            """;
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<PassageAdherent>(new CommandDefinition(sql, new { adherentId }, cancellationToken: ct));
        return lignes.AsList();
    }

    private static object Parametres(Adherent a) => new
    {
        a.Id,
        a.Nom,
        a.Prenom,
        a.Niveau,
        a.Licence,
        a.Telephone,
        Actif = a.Actif ? 1 : 0,
        Nitrox = a.Nitrox ? 1 : 0,
    };
}
