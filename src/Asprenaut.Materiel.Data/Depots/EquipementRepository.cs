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

    public async Task<long> AjouterAsync(Categorie categorie, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        categorie.Id = await cx.ExecuteScalarAsync<long>(new CommandDefinition(
            "INSERT INTO categorie (nom) VALUES (@Nom) RETURNING id;", new { categorie.Nom }, cancellationToken: ct));
        return categorie.Id;
    }

    public async Task<int> CompterUtilisationsAsync(long id, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT (SELECT COUNT(*) FROM equipement WHERE categorie_id = @id)
                 + (SELECT COUNT(*) FROM manuel WHERE categorie_id = @id);
            """, new { id }, cancellationToken: ct));
    }

    public async Task<bool> SupprimerAsync(long id, CancellationToken ct = default)
    {
        // La condition est dans la requête : un équipement ou un manuel créé entre la vérification du service et ici
        // empêche la suppression (les clés étrangères le refuseraient de toute façon).
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM categorie
            WHERE id = @id
              AND NOT EXISTS (SELECT 1 FROM equipement WHERE categorie_id = @id)
              AND NOT EXISTS (SELECT 1 FROM manuel WHERE categorie_id = @id);
            """, new { id }, cancellationToken: ct)) == 1;
    }
}

public sealed class EquipementRepository(FabriqueConnexion fabrique) : IEquipementRepository
{
    private const string Colonnes =
        "id, code, libelle, categorie_id, taille, etat, date_prochaine_revision, date_requalification, notes, cree_le, " + ColonnesFiche;

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
        const string sql = $"""
            SELECT e.id AS equipement_id, e.code, e.libelle, e.categorie_id, c.nom AS categorie,
                   e.taille, e.etat, e.date_prochaine_revision, e.date_requalification,
                   e.marque, e.modele, e.numero_serie, e.montage, e.marque_deuxieme_etage, e.modele_deuxieme_etage,
                   e.numero_deuxieme_etage, e.direct_system, e.manometre, e.octopus, e.numero_octopus,
                   (SELECT MAX(ct.date_controle) FROM controle ct WHERE ct.equipement_id = e.id AND ct.type = 'Revision') AS derniere_revision,
                   s.id AS sortie_en_cours_id, s.intitule AS sortie_en_cours_intitule,
                   EXISTS (SELECT 1 FROM sortie_equipement m
                           WHERE m.equipement_id = e.id AND m.statut = 'Manquant') AS est_manquant
            FROM equipement e
            JOIN categorie c ON c.id = e.categorie_id
            LEFT JOIN sortie_equipement se ON se.equipement_id = e.id AND se.statut = 'Dehors'
            LEFT JOIN sortie s ON s.id = se.sortie_id
            WHERE (@motif IS NULL OR e.code LIKE @motif OR e.libelle LIKE @motif OR e.taille LIKE @motif
                   OR e.marque LIKE @motif OR e.modele LIKE @motif OR e.numero_serie LIKE @motif
                   OR e.numero_deuxieme_etage LIKE @motif OR e.numero_octopus LIKE @motif)
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
        const string sql = $"""
            INSERT INTO equipement (code, libelle, categorie_id, taille, etat, date_prochaine_revision, date_requalification, notes, cree_le, {ColonnesFiche})
            VALUES (@Code, @Libelle, @CategorieId, @Taille, @Etat, @DateProchaineRevision, @DateRequalification, @Notes, @CreeLe, {ValeursFiche})
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
                date_prochaine_revision = @DateProchaineRevision, date_requalification = @DateRequalification, notes = @Notes,
                marque = @Marque, modele = @Modele, numero_serie = @NumeroSerie, montage = @Montage,
                marque_deuxieme_etage = @MarqueDeuxiemeEtage, modele_deuxieme_etage = @ModeleDeuxiemeEtage,
                numero_deuxieme_etage = @NumeroDeuxiemeEtage, direct_system = @DirectSystem, manometre = @Manometre,
                octopus = @Octopus, numero_octopus = @NumeroOctopus
            WHERE id = @Id;
            """;
        await using var cx = await fabrique.OuvrirAsync(ct);
        await cx.ExecuteAsync(new CommandDefinition(sql, Parametres(equipement), cancellationToken: ct));
    }

    public async Task<IReadOnlyList<CompteurEquipement>> ListerCompteursAsync(long? equipementId, CancellationToken ct = default)
    {
        // Chaque retour scanné ajoute les utilisations de sa sortie (1 en piscine, 4 en mer...).
        // Contrôle daté sans heure : les retours du jour du contrôle sont comptés comme faits avant lui (fin de journée).
        const string sql = """
            SELECT e.id AS equipement_id,
                   (SELECT COALESCE(SUM(s.utilisations), 0) FROM sortie_equipement se
                    JOIN sortie s ON s.id = se.sortie_id
                    WHERE se.equipement_id = e.id AND se.scanne_retour_le IS NOT NULL
                      AND se.scanne_retour_le > COALESCE((SELECT MAX(c.date_controle) FROM controle c WHERE c.equipement_id = e.id) || 'T23:59:59.999Z', '')
                   ) AS utilisations
            FROM equipement e
            WHERE @equipementId IS NULL OR e.id = @equipementId;
            """;
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<CompteurEquipement>(new CommandDefinition(sql, new { equipementId }, cancellationToken: ct));
        return lignes.AsList();
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
        e.Marque,
        e.Modele,
        e.NumeroSerie,
        Montage = e.Montage?.ToString(),
        e.MarqueDeuxiemeEtage,
        e.ModeleDeuxiemeEtage,
        e.NumeroDeuxiemeEtage,
        DirectSystem = e.DirectSystem is { } ds ? (ds ? 1 : 0) : (int?)null,
        e.Manometre,
        e.Octopus,
        e.NumeroOctopus,
    };

    /// <summary>Colonnes de la fiche matériel (migration 005), dans l'ordre des paramètres d'écriture.</summary>
    private const string ColonnesFiche =
        "marque, modele, numero_serie, montage, marque_deuxieme_etage, modele_deuxieme_etage, numero_deuxieme_etage, direct_system, manometre, octopus, numero_octopus";

    private const string ValeursFiche =
        "@Marque, @Modele, @NumeroSerie, @Montage, @MarqueDeuxiemeEtage, @ModeleDeuxiemeEtage, @NumeroDeuxiemeEtage, @DirectSystem, @Manometre, @Octopus, @NumeroOctopus";
}

public sealed class MoniteurRepository(FabriqueConnexion fabrique) : IMoniteurRepository
{
    private const string Colonnes = "id, genre, nom, prenom, niveau, telephone, abrege, inspecteur_tiv, actif";

    public async Task<Moniteur?> ObtenirAsync(long id, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.QuerySingleOrDefaultAsync<Moniteur>(new CommandDefinition(
            $"SELECT {Colonnes} FROM moniteur WHERE id = @id;", new { id }, cancellationToken: ct));
    }

    /// <summary>Moniteurs puis classes, chacun par ordre alphabétique.</summary>
    public async Task<IReadOnlyList<Moniteur>> ListerAsync(bool actifsSeulement, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<Moniteur>(new CommandDefinition(
            $"SELECT {Colonnes} FROM moniteur WHERE (@actifsSeulement = 0 OR actif = 1) ORDER BY genre = 'Classe', prenom, nom;",
            new { actifsSeulement }, cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task<long> AjouterAsync(Moniteur moniteur, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        moniteur.Id = await cx.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO moniteur (genre, nom, prenom, niveau, telephone, abrege, inspecteur_tiv, actif)
            VALUES (@Genre, @Nom, @Prenom, @Niveau, @Telephone, @Abrege, @InspecteurTiv, @Actif) RETURNING id;
            """,
            Parametres(moniteur), cancellationToken: ct));
        return moniteur.Id;
    }

    public async Task MettreAJourAsync(Moniteur moniteur, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        await cx.ExecuteAsync(new CommandDefinition(
            """
            UPDATE moniteur SET genre = @Genre, nom = @Nom, prenom = @Prenom, niveau = @Niveau, telephone = @Telephone,
                abrege = @Abrege, inspecteur_tiv = @InspecteurTiv, actif = @Actif
            WHERE id = @Id;
            """,
            Parametres(moniteur), cancellationToken: ct));
    }

    // Dapper ignore les TypeHandler des enums en paramètre : le genre est passé en texte.
    private static object Parametres(Moniteur m) => new
    {
        m.Id,
        Genre = m.Genre.ToString(),
        m.Nom,
        m.Prenom,
        m.Niveau,
        m.Telephone,
        m.Abrege,
        m.InspecteurTiv,
        m.Actif,
    };
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
                   s.intitule AS sortie_intitule, s.date_sortie AS sortie_date, s.type AS sortie_type, ts.nom AS sortie_type_nom, s.lieu AS sortie_lieu,
                   TRIM(m.prenom || ' ' || m.nom) AS moniteur_nom,
                   (SELECT COUNT(*) FROM sortie_equipement se WHERE se.sortie_id = j.sortie_id) AS sortie_nb_equipements,
                   (SELECT COUNT(*) FROM sortie_equipement se WHERE se.sortie_id = j.sortie_id AND se.statut = 'Manquant') AS sortie_nb_manquants
            FROM journal j
            LEFT JOIN equipement e ON e.id = j.equipement_id
            LEFT JOIN categorie c ON c.id = e.categorie_id
            LEFT JOIN sortie s ON s.id = j.sortie_id
            LEFT JOIN moniteur m ON m.id = s.moniteur_id
            LEFT JOIN type_sortie ts ON ts.id = s.type_sortie_id
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
