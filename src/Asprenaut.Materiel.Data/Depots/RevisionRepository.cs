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
            INSERT INTO controle (equipement_id, type, date_controle, resultat, inspecteur_id, notes, saisi_le)
            VALUES (@EquipementId, @Type, @DateControle, @Resultat, @InspecteurId, @Notes, @SaisiLe)
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
        }, cancellationToken: ct));
        return controle.Id;
    }

    public async Task<IReadOnlyList<DernierControle>> ListerDerniersControlesAsync(CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<DernierControle>(new CommandDefinition(
            "SELECT equipement_id, type, MAX(date_controle) AS date_controle FROM controle GROUP BY equipement_id, type;",
            cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task<IReadOnlyList<Controle>> ListerControlesAsync(long equipementId, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<Controle>(new CommandDefinition(
            """
            SELECT id, equipement_id, type, date_controle, resultat, inspecteur_id, notes, saisi_le
            FROM controle WHERE equipement_id = @equipementId ORDER BY date_controle DESC, id DESC;
            """,
            new { equipementId }, cancellationToken: ct));
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

    public async Task<SeanceTiv?> ProchaineSeanceAsync(DateOnly aPartirDu, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.QueryFirstOrDefaultAsync<SeanceTiv>(new CommandDefinition(
            "SELECT id, date_seance, heure, lieu FROM seance_tiv WHERE date_seance >= @debut ORDER BY date_seance, id LIMIT 1;",
            new { debut = FormatIso.Ecrire(aPartirDu) }, cancellationToken: ct));
    }

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
