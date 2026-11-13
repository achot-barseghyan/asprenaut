using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Data.Connexion;
using Dapper;

namespace Asprenaut.Materiel.Data.Depots;

/// <summary>Contrôles (table controle), passages en atelier (atelier), séances TIV (seance_tiv).</summary>
public sealed class RevisionRepository(FabriqueConnexion fabrique) : IRevisionRepository
{
    private const string ColonnesAtelier = "id, equipement_id, prestataire, motif, envoye_le, retour_prevu, revenu_le";

    public async Task<long> AjouterControleAsync(Controle controle, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO controle (equipement_id, type, date_controle, resultat, inspecteur_id, notes, saisi_le, nature, intervenant, composant)
            VALUES (@EquipementId, @Type, @DateControle, @Resultat, @InspecteurId, @Notes, @SaisiLe, @Nature, @Intervenant, @Composant)
            RETURNING id;
            """;
        await using var cx = await fabrique.OuvrirAsync(ct);
        controle.Id = await cx.ExecuteScalarAsync<long>(new CommandDefinition(sql, new
        {
            controle.EquipementId,
            Type = controle.Type.ToString(),
            DateControle = FormatIso.Ecrire(controle.DateControle),
            Resultat = controle.Resultat.ToString(),
            controle.InspecteurId,
            controle.Notes,
            SaisiLe = FormatIso.Ecrire(controle.SaisiLe),
            controle.Nature,
            controle.Intervenant,
            controle.Composant,
        }, cancellationToken: ct));
        return controle.Id;
    }

    public async Task<IReadOnlyList<DernierControle>> ListerDerniersControlesAsync(CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<DernierControle>(new CommandDefinition(
            "SELECT equipement_id, type, MAX(date_controle) AS date_controle FROM controle WHERE composant = '' GROUP BY equipement_id, type;",
            cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task<IReadOnlyList<Controle>> ListerControlesAsync(long equipementId, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<Controle>(new CommandDefinition(
            """
            SELECT id, equipement_id, type, date_controle, resultat, inspecteur_id, notes, saisi_le, nature, intervenant, composant
            FROM controle WHERE equipement_id = @equipementId ORDER BY date_controle DESC, id DESC;
            """,
            new { equipementId }, cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task<IReadOnlyList<Controle>> ListerControlesPeriodeAsync(DateOnly du, DateOnly au, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<Controle>(new CommandDefinition(
            """
            SELECT id, equipement_id, type, date_controle, resultat, inspecteur_id, notes, saisi_le, nature, intervenant, composant
            FROM controle WHERE date_controle BETWEEN @du AND @au ORDER BY date_controle, id;
            """,
            new { du = FormatIso.Ecrire(du), au = FormatIso.Ecrire(au) }, cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task<long> AjouterPassageAtelierAsync(PassageAtelier passage, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO atelier (equipement_id, prestataire, motif, envoye_le, retour_prevu, revenu_le)
            VALUES (@EquipementId, @Prestataire, @Motif, @EnvoyeLe, @RetourPrevu, @RevenuLe)
            RETURNING id;
            """;
        await using var cx = await fabrique.OuvrirAsync(ct);
        passage.Id = await cx.ExecuteScalarAsync<long>(new CommandDefinition(sql, Parametres(passage), cancellationToken: ct));
        return passage.Id;
    }

    public async Task<PassageAtelier?> ObtenirPassageEnCoursAsync(long equipementId, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.QuerySingleOrDefaultAsync<PassageAtelier>(new CommandDefinition(
            $"SELECT {ColonnesAtelier} FROM atelier WHERE equipement_id = @equipementId AND revenu_le IS NULL;",
            new { equipementId }, cancellationToken: ct));
    }

    public async Task<PassageAtelier?> ObtenirPassageAsync(long id, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.QuerySingleOrDefaultAsync<PassageAtelier>(new CommandDefinition(
            $"SELECT {ColonnesAtelier} FROM atelier WHERE id = @id;", new { id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PassageAtelier>> ListerPassagesAtelierAsync(long equipementId, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<PassageAtelier>(new CommandDefinition(
            $"SELECT {ColonnesAtelier} FROM atelier WHERE equipement_id = @equipementId ORDER BY envoye_le DESC, id DESC;",
            new { equipementId }, cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task MettreAJourPassageAsync(PassageAtelier passage, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE atelier SET prestataire = @Prestataire, motif = @Motif, envoye_le = @EnvoyeLe,
                               retour_prevu = @RetourPrevu, revenu_le = @RevenuLe
            WHERE id = @Id;
            """;
        await using var cx = await fabrique.OuvrirAsync(ct);
        await cx.ExecuteAsync(new CommandDefinition(sql, Parametres(passage), cancellationToken: ct));
    }

    public async Task<IReadOnlyList<LigneAtelier>> ListerAtelierEnCoursAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT a.id AS passage_id, a.equipement_id, e.code, e.libelle, a.prestataire, a.motif, a.envoye_le, a.retour_prevu
            FROM atelier a JOIN equipement e ON e.id = a.equipement_id
            WHERE a.revenu_le IS NULL
            ORDER BY a.envoye_le, a.id;
            """;
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<LigneAtelierSql>(new CommandDefinition(sql, cancellationToken: ct));
        return lignes.Select(l => new LigneAtelier(l.PassageId, l.EquipementId, l.Code, l.Libelle, l.Prestataire, l.Motif, l.EnvoyeLe, l.RetourPrevu)).ToList();
    }

    public async Task<long> AjouterSeanceAsync(SeanceTiv seance, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        seance.Id = await cx.ExecuteScalarAsync<long>(new CommandDefinition(
            "INSERT INTO seance_tiv (date_seance, heure, lieu) VALUES (@DateSeance, @Heure, @Lieu) RETURNING id;",
            new { DateSeance = FormatIso.Ecrire(seance.DateSeance), seance.Heure, seance.Lieu }, cancellationToken: ct));
        return seance.Id;
    }

    public async Task<IReadOnlyList<long>> ListerPlanifiesAsync(long seanceId, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<long>(new CommandDefinition(
            "SELECT equipement_id FROM seance_equipement WHERE seance_id = @seanceId ORDER BY equipement_id;", new { seanceId }, cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task AjouterASeanceAsync(long seanceId, long equipementId, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        await cx.ExecuteAsync(new CommandDefinition(
            "INSERT OR IGNORE INTO seance_equipement (seance_id, equipement_id) VALUES (@seanceId, @equipementId);",
            new { seanceId, equipementId }, cancellationToken: ct));
    }

    public async Task RetirerDesSeancesAsync(long equipementId, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        await cx.ExecuteAsync(new CommandDefinition(
            "DELETE FROM seance_equipement WHERE equipement_id = @equipementId;", new { equipementId }, cancellationToken: ct));
    }

    public async Task<SeanceTiv?> ProchaineSeanceAsync(DateOnly aPartirDu, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.QueryFirstOrDefaultAsync<SeanceTiv>(new CommandDefinition(
            "SELECT id, date_seance, heure, lieu FROM seance_tiv WHERE date_seance >= @debut ORDER BY date_seance, id LIMIT 1;",
            new { debut = FormatIso.Ecrire(aPartirDu) }, cancellationToken: ct));
    }

    // ---------------------------------------------------------------- Étapes (table revision, migration 016)

    /// <summary>Nom du prestataire : celui de la liste, sinon celui du passage en atelier (envoi sans prestataire enregistré).</summary>
    private const string SelectSuivi = """
        SELECT r.id, r.equipement_id, r.etape, r.prestataire_id,
               COALESCE(p.nom, a.prestataire, '') AS prestataire,
               r.motif, r.note_envoi, r.date_envoi, r.date_retour_prevue, r.intervenant_interne, r.seance_tiv,
               r.atelier_id, r.controle_id, r.cree_le, r.date_fait, r.archive, r.date_archive
        FROM revision r
        LEFT JOIN prestataire p ON p.id = r.prestataire_id
        LEFT JOIN atelier a ON a.id = r.atelier_id
        """;

    public async Task<IReadOnlyList<SuiviRevision>> ListerSuivisAsync(DateOnly faitDepuis, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<SuiviRevision>(new CommandDefinition(
            $"{SelectSuivi} WHERE r.etape <> 'Fait' OR (r.archive = 0 AND r.date_fait >= @depuis) ORDER BY r.id;",
            new { depuis = FormatIso.Ecrire(faitDepuis) }, cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task<SuiviRevision?> ObtenirSuiviEnCoursAsync(long equipementId, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.QuerySingleOrDefaultAsync<SuiviRevision>(new CommandDefinition(
            $"{SelectSuivi} WHERE r.equipement_id = @equipementId AND r.etape <> 'Fait';", new { equipementId }, cancellationToken: ct));
    }

    public async Task<SuiviRevision?> ObtenirSuiviAsync(long id, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.QuerySingleOrDefaultAsync<SuiviRevision>(new CommandDefinition(
            $"{SelectSuivi} WHERE r.id = @id;", new { id }, cancellationToken: ct));
    }

    public async Task<long> AjouterSuiviAsync(SuiviRevision suivi, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO revision (equipement_id, etape, prestataire_id, motif, note_envoi, date_envoi, date_retour_prevue,
                                  intervenant_interne, seance_tiv, atelier_id, controle_id, cree_le, date_fait, archive, date_archive)
            VALUES (@EquipementId, @Etape, @PrestataireId, @Motif, @NoteEnvoi, @DateEnvoi, @DateRetourPrevue,
                    @IntervenantInterne, @SeanceTiv, @AtelierId, @ControleId, @CreeLe, @DateFait, @Archive, @DateArchive)
            RETURNING id;
            """;
        await using var cx = await fabrique.OuvrirAsync(ct);
        suivi.Id = await cx.ExecuteScalarAsync<long>(new CommandDefinition(sql, Parametres(suivi), cancellationToken: ct));
        return suivi.Id;
    }

    public async Task MettreAJourSuiviAsync(SuiviRevision suivi, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE revision SET etape = @Etape, prestataire_id = @PrestataireId, motif = @Motif, note_envoi = @NoteEnvoi,
                                date_envoi = @DateEnvoi, date_retour_prevue = @DateRetourPrevue, intervenant_interne = @IntervenantInterne,
                                seance_tiv = @SeanceTiv, atelier_id = @AtelierId, controle_id = @ControleId, date_fait = @DateFait,
                                archive = @Archive, date_archive = @DateArchive
            WHERE id = @Id;
            """;
        await using var cx = await fabrique.OuvrirAsync(ct);
        await cx.ExecuteAsync(new CommandDefinition(sql, Parametres(suivi), cancellationToken: ct));
    }

    public async Task SupprimerSuiviAsync(long id, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        await cx.ExecuteAsync(new CommandDefinition("DELETE FROM revision WHERE id = @id AND etape <> 'Fait';", new { id }, cancellationToken: ct));
    }

    public async Task<int> ArchiverSuivisAsync(IReadOnlyList<long> ids, bool archive, DateOnly? le, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.ExecuteAsync(new CommandDefinition(
            "UPDATE revision SET archive = @archive, date_archive = @le WHERE id IN @ids AND etape = 'Fait';",
            new { ids, archive = archive ? 1 : 0, le = FormatIso.Ecrire(archive ? le : null) }, cancellationToken: ct));
    }

    private static object Parametres(SuiviRevision s) => new
    {
        s.Id,
        s.EquipementId,
        Etape = s.Etape.ToString(),
        s.PrestataireId,
        s.Motif,
        s.NoteEnvoi,
        DateEnvoi = FormatIso.Ecrire(s.DateEnvoi),
        DateRetourPrevue = FormatIso.Ecrire(s.DateRetourPrevue),
        s.IntervenantInterne,
        SeanceTiv = s.SeanceTiv ? 1 : 0,
        s.AtelierId,
        s.ControleId,
        CreeLe = FormatIso.Ecrire(s.CreeLe),
        DateFait = FormatIso.Ecrire(s.DateFait),
        Archive = s.Archive ? 1 : 0,
        DateArchive = FormatIso.Ecrire(s.DateArchive),
    };

    private static object Parametres(PassageAtelier p) => new
    {
        p.Id,
        p.EquipementId,
        p.Prestataire,
        p.Motif,
        EnvoyeLe = FormatIso.Ecrire(p.EnvoyeLe),
        RetourPrevu = FormatIso.Ecrire(p.RetourPrevu),
        RevenuLe = FormatIso.Ecrire(p.RevenuLe),
    };

    /// <summary>Ligne lue par Dapper (les records positionnels ne se lient pas par nom de colonne).</summary>
    private sealed class LigneAtelierSql
    {
        public long PassageId { get; set; }
        public long EquipementId { get; set; }
        public string Code { get; set; } = "";
        public string Libelle { get; set; } = "";
        public string Prestataire { get; set; } = "";
        public string Motif { get; set; } = "";
        public DateOnly EnvoyeLe { get; set; }
        public DateOnly? RetourPrevu { get; set; }
    }
}
