using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Data.Connexion;
using Dapper;

namespace Asprenaut.Materiel.Data.Depots;

/// <summary>Adhérents du club (table adherent, migration 014) et le matériel qu'ils ont pris (sortie_equipement.adherent_id).</summary>
public sealed class AdherentRepository(FabriqueConnexion fabrique) : IAdherentRepository
{
    // Formation en cours : le nom vient de la classe liée (migration 021) ; adherent.niveau seulement s'il n'y en a pas.
    private const string Colonnes = "a.id, a.nom, a.prenom, a.classe_id, COALESCE(c.nom, a.niveau) AS niveau, a.licence, a.telephone, a.actif, a.nitrox, a.caci, a.caci_expiration";

    private const string Source = "adherent a LEFT JOIN moniteur c ON c.id = a.classe_id";

    public async Task<IReadOnlyList<Adherent>> ListerAsync(bool actifsSeulement, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<Adherent>(new CommandDefinition(
            $"SELECT {Colonnes} FROM {Source} WHERE (@actifsSeulement = 0 OR a.actif = 1) ORDER BY a.nom, a.prenom;",
            new { actifsSeulement }, cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task<Adherent?> ObtenirAsync(long id, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.QuerySingleOrDefaultAsync<Adherent>(new CommandDefinition(
            $"SELECT {Colonnes} FROM {Source} WHERE a.id = @id;", new { id }, cancellationToken: ct));
    }

    public async Task<long> AjouterAsync(Adherent adherent, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        adherent.Id = await cx.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO adherent (nom, prenom, classe_id, niveau, licence, telephone, actif, nitrox, caci, caci_expiration)
            VALUES (@Nom, @Prenom, @ClasseId, @Niveau, @Licence, @Telephone, @Actif, @Nitrox, @Caci, @CaciExpiration) RETURNING id;
            """,
            Parametres(adherent), cancellationToken: ct));
        return adherent.Id;
    }

    public async Task MettreAJourAsync(Adherent adherent, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        await cx.ExecuteAsync(new CommandDefinition(
            """
            UPDATE adherent SET nom = @Nom, prenom = @Prenom, classe_id = @ClasseId, niveau = @Niveau, licence = @Licence, telephone = @Telephone, actif = @Actif, nitrox = @Nitrox, caci = @Caci, caci_expiration = @CaciExpiration
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

    public async Task<bool> SupprimerSiJamaisSortiAsync(long adherentId, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        await using var tx = await cx.BeginTransactionAsync(ct);
        var dejaSorti = await cx.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM sortie_equipement WHERE adherent_id = @adherentId);",
            new { adherentId }, tx, cancellationToken: ct));
        if (dejaSorti != 0)
        {
            return false;
        }

        await cx.ExecuteAsync(new CommandDefinition(
            "UPDATE journal SET adherent_id = NULL WHERE adherent_id = @adherentId;", new { adherentId }, tx, cancellationToken: ct));
        var supprimes = await cx.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM adherent WHERE id = @adherentId
              AND NOT EXISTS (SELECT 1 FROM sortie_equipement WHERE adherent_id = @adherentId);
            """,
            new { adherentId }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return supprimes > 0;
    }

    private static object Parametres(Adherent a) => new
    {
        a.Id,
        a.Nom,
        a.Prenom,
        a.ClasseId,
        a.Niveau,
        a.Licence,
        a.Telephone,
        Actif = a.Actif ? 1 : 0,
        Nitrox = a.Nitrox ? 1 : 0,
        Caci = a.Caci ? 1 : 0,
        CaciExpiration = FormatIso.Ecrire(a.CaciExpiration),
    };
}
