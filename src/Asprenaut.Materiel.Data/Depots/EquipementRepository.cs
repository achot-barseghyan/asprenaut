using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Data.Connexion;
using Dapper;

namespace Asprenaut.Materiel.Data.Depots;

public sealed class CategorieRepository(FabriqueConnexion fabrique) : ICategorieRepository
{
    public async Task<IReadOnlyList<Categorie>> ListerAsync(CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<Categorie>(new CommandDefinition(
            "SELECT id, nom FROM categorie ORDER BY id;", cancellationToken: ct));
        return lignes.AsList();
    }
}

public sealed class EquipementRepository(FabriqueConnexion fabrique) : IEquipementRepository
{
    private const string Colonnes =
        "id, code, libelle, categorie_id, taille, etat, date_prochaine_revision, date_requalification, notes, cree_le";

    public async Task<Equipement?> ObtenirAsync(long id, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.QuerySingleOrDefaultAsync<Equipement>(new CommandDefinition(
            $"SELECT {Colonnes} FROM equipement WHERE id = @id;", new { id }, cancellationToken: ct));
    }

    public async Task<Equipement?> ObtenirParCodeAsync(string code, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.QuerySingleOrDefaultAsync<Equipement>(new CommandDefinition(
            $"SELECT {Colonnes} FROM equipement WHERE code = @code;", new { code }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<LigneInventaire>> ListerInventaireAsync(string? recherche, long? categorieId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT e.id AS equipement_id, e.code, e.libelle, e.categorie_id, c.nom AS categorie,
                   e.taille, e.etat, e.date_prochaine_revision, e.date_requalification,
                   s.id AS sortie_en_cours_id, s.intitule AS sortie_en_cours_intitule,
                   EXISTS (SELECT 1 FROM sortie_equipement m
                           WHERE m.equipement_id = e.id AND m.statut = 'Manquant') AS est_manquant
            FROM equipement e
            JOIN categorie c ON c.id = e.categorie_id
            LEFT JOIN sortie_equipement se ON se.equipement_id = e.id AND se.statut = 'Dehors'
            LEFT JOIN sortie s ON s.id = se.sortie_id
            WHERE (@motif IS NULL OR e.code LIKE @motif OR e.libelle LIKE @motif OR e.taille LIKE @motif)
              AND (@categorieId IS NULL OR e.categorie_id = @categorieId)
            ORDER BY e.code;
            """;
        var motif = recherche is null ? null : $"%{recherche}%";
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<LigneInventaire>(new CommandDefinition(sql, new { motif, categorieId }, cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task<long> AjouterAsync(Equipement equipement, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO equipement (code, libelle, categorie_id, taille, etat, date_prochaine_revision, date_requalification, notes, cree_le)
            VALUES (@Code, @Libelle, @CategorieId, @Taille, @Etat, @DateProchaineRevision, @DateRequalification, @Notes, @CreeLe)
            RETURNING id;
            """;
        await using var cx = await fabrique.OuvrirAsync(ct);
        equipement.Id = await cx.ExecuteScalarAsync<long>(new CommandDefinition(sql, Parametres(equipement), cancellationToken: ct));
        return equipement.Id;
    }

    public async Task MettreAJourAsync(Equipement equipement, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE equipement
            SET code = @Code, libelle = @Libelle, categorie_id = @CategorieId, taille = @Taille, etat = @Etat,
                date_prochaine_revision = @DateProchaineRevision, date_requalification = @DateRequalification, notes = @Notes
            WHERE id = @Id;
            """;
        await using var cx = await fabrique.OuvrirAsync(ct);
        await cx.ExecuteAsync(new CommandDefinition(sql, Parametres(equipement), cancellationToken: ct));
    }

    private static object Parametres(Equipement e) => new
    {
        e.Id,
        e.Code,
        e.Libelle,
        e.CategorieId,
        e.Taille,
        Etat = e.Etat.ToString(),
        DateProchaineRevision = FormatIso.Ecrire(e.DateProchaineRevision),
        DateRequalification = FormatIso.Ecrire(e.DateRequalification),
        e.Notes,
        CreeLe = FormatIso.Ecrire(e.CreeLe),
    };
}

public sealed class MoniteurRepository(FabriqueConnexion fabrique) : IMoniteurRepository
{
    public async Task<Moniteur?> ObtenirAsync(long id, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.QuerySingleOrDefaultAsync<Moniteur>(new CommandDefinition(
            "SELECT id, nom, prenom, niveau, telephone, inspecteur_tiv, actif FROM moniteur WHERE id = @id;", new { id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Moniteur>> ListerAsync(bool actifsSeulement, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<Moniteur>(new CommandDefinition(
            "SELECT id, nom, prenom, niveau, telephone, inspecteur_tiv, actif FROM moniteur WHERE (@actifsSeulement = 0 OR actif = 1) ORDER BY prenom, nom;",
            new { actifsSeulement }, cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task<long> AjouterAsync(Moniteur moniteur, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        moniteur.Id = await cx.ExecuteScalarAsync<long>(new CommandDefinition(
            "INSERT INTO moniteur (nom, prenom, niveau, telephone, inspecteur_tiv, actif) VALUES (@Nom, @Prenom, @Niveau, @Telephone, @InspecteurTiv, @Actif) RETURNING id;",
            moniteur, cancellationToken: ct));
        return moniteur.Id;
    }

    public async Task MettreAJourAsync(Moniteur moniteur, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        await cx.ExecuteAsync(new CommandDefinition(
            "UPDATE moniteur SET nom = @Nom, prenom = @Prenom, niveau = @Niveau, telephone = @Telephone, inspecteur_tiv = @InspecteurTiv, actif = @Actif WHERE id = @Id;",
            moniteur, cancellationToken: ct));
    }
}

public sealed class JournalRepository(FabriqueConnexion fabrique) : IJournalRepository
{
    public async Task AjouterAsync(EntreeJournal entree, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        entree.Id = await cx.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO journal (horodatage, action, details, equipement_id, sortie_id)
            VALUES (@Horodatage, @Action, @Details, @EquipementId, @SortieId)
            RETURNING id;
            """,
            new { Horodatage = FormatIso.Ecrire(entree.Horodatage), entree.Action, entree.Details, entree.EquipementId, entree.SortieId },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<EntreeJournal>> ListerRecentsAsync(int nombre, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<EntreeJournal>(new CommandDefinition(
            "SELECT id, horodatage, action, details, equipement_id, sortie_id FROM journal ORDER BY id DESC LIMIT @nombre;",
            new { nombre }, cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task<IReadOnlyList<LigneJournal>> ListerAsync(DateTime? depuisUtc, int nombreMax, CancellationToken ct = default)
    {
        const string sql = """
            SELECT j.id, j.horodatage, j.action, j.details, j.equipement_id, j.sortie_id,
                   e.code, e.libelle, c.nom AS categorie,
                   s.intitule AS sortie_intitule, s.date_sortie AS sortie_date, s.type AS sortie_type, s.lieu AS sortie_lieu,
                   TRIM(m.prenom || ' ' || m.nom) AS moniteur_nom,
                   (SELECT COUNT(*) FROM sortie_equipement se WHERE se.sortie_id = j.sortie_id) AS sortie_nb_equipements,
                   (SELECT COUNT(*) FROM sortie_equipement se WHERE se.sortie_id = j.sortie_id AND se.statut = 'Manquant') AS sortie_nb_manquants
            FROM journal j
            LEFT JOIN equipement e ON e.id = j.equipement_id
            LEFT JOIN categorie c ON c.id = e.categorie_id
            LEFT JOIN sortie s ON s.id = j.sortie_id
            LEFT JOIN moniteur m ON m.id = s.moniteur_id
            WHERE @depuis IS NULL OR j.horodatage >= @depuis
            ORDER BY j.horodatage DESC, j.id DESC
            LIMIT @nombreMax;
            """;
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<LigneJournal>(new CommandDefinition(
            sql, new { depuis = FormatIso.Ecrire(depuisUtc), nombreMax }, cancellationToken: ct));
        return lignes.AsList();
    }
}
