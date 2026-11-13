using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Data.Connexion;
using Dapper;

namespace Asprenaut.Materiel.Data.Depots;

public sealed class SortieRepository(FabriqueConnexion fabrique) : ISortieRepository
{
    private const string ColonnesSortie =
        "id, date_sortie, type, type_sortie_id AS type_id, utilisations, intitule, moniteur_id, lieu, notes, statut, depart_valide_le, cloturee_le, nominatif, un_par_categorie";

    private const string ColonnesAffectation =
        "id, sortie_id, equipement_id, scanne_depart_le, scanne_retour_le, statut, adherent_id";

    private const string SelectResume = """
        SELECT s.id, s.date_sortie, s.type, s.type_sortie_id AS type_id, s.utilisations, ts.nom AS type_nom, s.intitule, s.lieu, s.statut, s.moniteur_id, s.depart_valide_le, s.cloturee_le, s.nominatif, s.un_par_categorie,
               TRIM(m.prenom || ' ' || m.nom) AS moniteur_nom, m.genre AS moniteur_genre,
               COUNT(se.id) AS nb_total,
               COALESCE(SUM(se.statut = 'Rendu'), 0) AS nb_rendus,
               COALESCE(SUM(se.statut = 'Manquant'), 0) AS nb_manquants
        FROM sortie s
        JOIN moniteur m ON m.id = s.moniteur_id
        LEFT JOIN type_sortie ts ON ts.id = s.type_sortie_id
        LEFT JOIN sortie_equipement se ON se.sortie_id = s.id
        """;

    public async Task<Sortie?> ObtenirAsync(long id, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.QuerySingleOrDefaultAsync<Sortie>(new CommandDefinition(
            $"SELECT {ColonnesSortie} FROM sortie WHERE id = @id;", new { id }, cancellationToken: ct));
    }

    public async Task<long> AjouterAsync(Sortie sortie, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO sortie (date_sortie, type, type_sortie_id, utilisations, intitule, moniteur_id, lieu, notes, statut, depart_valide_le, cloturee_le, nominatif, un_par_categorie)
            VALUES (@DateSortie, @Type, @TypeId, @Utilisations, @Intitule, @MoniteurId, @Lieu, @Notes, @Statut, @DepartValideLe, @ClotureeLe, @Nominatif, @UnParCategorie)
            RETURNING id;
            """;
        await using var cx = await fabrique.OuvrirAsync(ct);
        sortie.Id = await cx.ExecuteScalarAsync<long>(new CommandDefinition(sql, Parametres(sortie), cancellationToken: ct));
        return sortie.Id;
    }

    public async Task MettreAJourAsync(Sortie sortie, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        await cx.ExecuteAsync(new CommandDefinition(SqlMiseAJourSortie, Parametres(sortie), cancellationToken: ct));
    }

    public async Task<bool> SupprimerAsync(long id, CancellationToken ct = default)
    {
        // La condition est dans la requête : un scan arrivé entre la vérification du service et ici empêche la suppression.
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.ExecuteAsync(new CommandDefinition(
            "DELETE FROM sortie WHERE id = @id AND NOT EXISTS (SELECT 1 FROM sortie_equipement WHERE sortie_id = @id);",
            new { id }, cancellationToken: ct));
        return lignes == 1;
    }

    public async Task<SortieResume?> ObtenirResumeAsync(long id, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.QuerySingleOrDefaultAsync<SortieResume>(new CommandDefinition(
            $"{SelectResume} WHERE s.id = @id GROUP BY s.id;", new { id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<SortieResume>> ListerResumesAsync(bool cloturees, CancellationToken ct = default)
    {
        var filtre = cloturees ? "s.statut = 'Cloturee'" : "s.statut <> 'Cloturee'";
        var ordre = cloturees ? "s.cloturee_le DESC, s.id DESC" : "s.date_sortie, s.id";
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<SortieResume>(new CommandDefinition(
            $"{SelectResume} WHERE {filtre} GROUP BY s.id ORDER BY {ordre};", cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task<IReadOnlyList<SortieResume>> ListerClotureesAsync(int decalage, int nombre, CancellationToken ct = default)
    {
        // Les sorties de la page d'abord (index ix_sortie_statut), puis leurs totaux : on ne compte pas le matériel des autres.
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<SortieResume>(new CommandDefinition(
            $"""
            {SelectResume}
            WHERE s.id IN (SELECT id FROM sortie WHERE statut = 'Cloturee' ORDER BY cloturee_le DESC, id DESC LIMIT @nombre OFFSET @decalage)
            GROUP BY s.id ORDER BY s.cloturee_le DESC, s.id DESC;
            """,
            new { decalage = Math.Max(0, decalage), nombre = Math.Max(0, nombre) }, cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task<int> CompterClotureesAsync(CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM sortie WHERE statut = 'Cloturee';", cancellationToken: ct));
    }

    public async Task<IReadOnlyList<LigneSortie>> ListerLignesAsync(long sortieId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT se.id AS ligne_id, se.sortie_id, se.equipement_id, e.code, e.libelle, c.nom AS categorie,
                   e.taille, e.date_prochaine_revision, e.date_requalification, e.nitrox, se.statut, se.scanne_depart_le, se.scanne_retour_le,
                   se.adherent_id, TRIM(ad.prenom || ' ' || ad.nom) AS adherent_nom
            FROM sortie_equipement se
            JOIN equipement e ON e.id = se.equipement_id
            JOIN categorie c ON c.id = e.categorie_id
            LEFT JOIN adherent ad ON ad.id = se.adherent_id
            WHERE se.sortie_id = @sortieId
            ORDER BY se.scanne_depart_le DESC, se.id DESC;
            """;
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<LigneSortie>(new CommandDefinition(sql, new { sortieId }, cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task<IReadOnlyList<PassageSortie>> ListerPassagesAsync(long equipementId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT se.sortie_id, s.intitule, s.date_sortie, TRIM(m.prenom || ' ' || m.nom) AS moniteur_nom, s.cloturee_le,
                   se.statut, se.scanne_depart_le, se.scanne_retour_le,
                   s.type AS type_sortie, COALESCE(ts.nom, '') AS type_nom, s.utilisations, ts.utilisations AS utilisations_type,
                   m.genre AS moniteur_genre
            FROM sortie_equipement se
            JOIN sortie s ON s.id = se.sortie_id
            JOIN moniteur m ON m.id = s.moniteur_id
            LEFT JOIN type_sortie ts ON ts.id = s.type_sortie_id
            WHERE se.equipement_id = @equipementId
            ORDER BY se.scanne_depart_le DESC, se.id DESC;
            """;
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<PassageSortie>(new CommandDefinition(sql, new { equipementId }, cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task<IReadOnlyList<SortieEquipement>> ListerAffectationsAsync(long sortieId, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<SortieEquipement>(new CommandDefinition(
            $"SELECT {ColonnesAffectation} FROM sortie_equipement WHERE sortie_id = @sortieId ORDER BY id;",
            new { sortieId }, cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task<IReadOnlyList<PassageBilan>> ListerPassagesPeriodeAsync(DateOnly du, DateOnly au, CancellationToken ct = default)
    {
        const string sql = """
            SELECT se.sortie_id, s.date_sortie, s.type AS type_sortie, s.type_sortie_id AS type_id, ts.nom AS type_nom, s.statut AS statut_sortie, s.intitule,
                   TRIM(m.prenom || ' ' || m.nom) AS moniteur_nom, s.cloturee_le,
                   se.equipement_id, e.code, e.libelle, c.nom AS categorie, e.taille, se.statut, se.scanne_retour_le
            FROM sortie_equipement se
            JOIN sortie s ON s.id = se.sortie_id
            JOIN moniteur m ON m.id = s.moniteur_id
            JOIN equipement e ON e.id = se.equipement_id
            JOIN categorie c ON c.id = e.categorie_id
            LEFT JOIN type_sortie ts ON ts.id = s.type_sortie_id
            WHERE s.date_sortie BETWEEN @du AND @au
            ORDER BY s.date_sortie, s.id, se.id;
            """;
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<PassageBilan>(new CommandDefinition(
            sql, new { du = FormatIso.Ecrire(du), au = FormatIso.Ecrire(au) }, cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task<SortieEquipement?> ObtenirAffectationAsync(long sortieId, long equipementId, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.QuerySingleOrDefaultAsync<SortieEquipement>(new CommandDefinition(
            $"SELECT {ColonnesAffectation} FROM sortie_equipement WHERE sortie_id = @sortieId AND equipement_id = @equipementId;",
            new { sortieId, equipementId }, cancellationToken: ct));
    }

    public async Task<SortieEquipement?> ObtenirAffectationDehorsAsync(long equipementId, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.QueryFirstOrDefaultAsync<SortieEquipement>(new CommandDefinition(
            $"SELECT {ColonnesAffectation} FROM sortie_equipement WHERE equipement_id = @equipementId AND statut = 'Dehors' LIMIT 1;",
            new { equipementId }, cancellationToken: ct));
    }

    public async Task<SortieEquipement?> ObtenirDerniereAffectationManquanteAsync(long equipementId, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.QueryFirstOrDefaultAsync<SortieEquipement>(new CommandDefinition(
            $"SELECT {ColonnesAffectation} FROM sortie_equipement WHERE equipement_id = @equipementId AND statut = 'Manquant' ORDER BY scanne_depart_le DESC LIMIT 1;",
            new { equipementId }, cancellationToken: ct));
    }

    public async Task<long> AjouterAffectationAsync(SortieEquipement affectation, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO sortie_equipement (sortie_id, equipement_id, scanne_depart_le, scanne_retour_le, statut, adherent_id)
            VALUES (@SortieId, @EquipementId, @ScanneDepartLe, @ScanneRetourLe, @Statut, @AdherentId)
            RETURNING id;
            """;
        await using var cx = await fabrique.OuvrirAsync(ct);
        affectation.Id = await cx.ExecuteScalarAsync<long>(new CommandDefinition(sql, Parametres(affectation), cancellationToken: ct));
        return affectation.Id;
    }

    public async Task MettreAJourAffectationAsync(SortieEquipement affectation, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        await cx.ExecuteAsync(new CommandDefinition(SqlMiseAJourAffectation, Parametres(affectation), cancellationToken: ct));
    }

    public async Task SupprimerAffectationAsync(long affectationId, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        await cx.ExecuteAsync(new CommandDefinition(
            "DELETE FROM sortie_equipement WHERE id = @affectationId;", new { affectationId }, cancellationToken: ct));
    }

    public async Task AttribuerAdherentAsync(long affectationId, long? adherentId, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        await cx.ExecuteAsync(new CommandDefinition(
            "UPDATE sortie_equipement SET adherent_id = @adherentId WHERE id = @affectationId;",
            new { affectationId, adherentId }, cancellationToken: ct));
    }

    public async Task EnregistrerClotureAsync(Sortie sortie, IReadOnlyList<SortieEquipement> affectationsModifiees, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        await using var tx = await cx.BeginTransactionAsync(ct);
        foreach (var a in affectationsModifiees)
        {
            await cx.ExecuteAsync(new CommandDefinition(SqlMiseAJourAffectation, Parametres(a), tx, cancellationToken: ct));
        }

        await cx.ExecuteAsync(new CommandDefinition(SqlMiseAJourSortie, Parametres(sortie), tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    private const string SqlMiseAJourSortie = """
        UPDATE sortie
        SET date_sortie = @DateSortie, type = @Type, type_sortie_id = @TypeId, utilisations = @Utilisations, intitule = @Intitule, moniteur_id = @MoniteurId, lieu = @Lieu,
            notes = @Notes, statut = @Statut, depart_valide_le = @DepartValideLe, cloturee_le = @ClotureeLe, nominatif = @Nominatif, un_par_categorie = @UnParCategorie
        WHERE id = @Id;
        """;

    private const string SqlMiseAJourAffectation = """
        UPDATE sortie_equipement
        SET scanne_depart_le = @ScanneDepartLe, scanne_retour_le = @ScanneRetourLe, statut = @Statut, adherent_id = @AdherentId
        WHERE id = @Id;
        """;

    private static object Parametres(Sortie s) => new
    {
        s.Id,
        s.DateSortie,
        Type = s.Type.ToString(),
        s.TypeId,
        s.Utilisations,
        s.Intitule,
        s.MoniteurId,
        s.Lieu,
        s.Notes,
        Statut = s.Statut.ToString(),
        DepartValideLe = FormatIso.Ecrire(s.DepartValideLe),
        ClotureeLe = FormatIso.Ecrire(s.ClotureeLe),
        Nominatif = s.Nominatif ? 1 : 0,
        UnParCategorie = s.UnParCategorie ? 1 : 0,
    };

    private static object Parametres(SortieEquipement a) => new
    {
        a.Id,
        a.SortieId,
        a.EquipementId,
        ScanneDepartLe = FormatIso.Ecrire(a.ScanneDepartLe),
        ScanneRetourLe = FormatIso.Ecrire(a.ScanneRetourLe),
        Statut = a.Statut.ToString(),
        a.AdherentId,
    };
}
