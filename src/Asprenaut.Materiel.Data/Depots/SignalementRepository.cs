using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Data.Connexion;
using Dapper;

namespace Asprenaut.Materiel.Data.Depots;

/// <summary>Problèmes signalés (table signalement, migration 013).</summary>
public sealed class SignalementRepository(FabriqueConnexion fabrique) : ISignalementRepository
{
    private const string Colonnes = "id, equipement_id, sortie_id, cause, gravite, precision, signale_le, mis_hors_service, traite_le";

    public async Task<long> AjouterAsync(Signalement signalement, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        signalement.Id = await cx.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO signalement (equipement_id, sortie_id, cause, gravite, precision, signale_le, mis_hors_service, traite_le)
            VALUES (@EquipementId, @SortieId, @Cause, @Gravite, @Precision, @SignaleLe, @MisHorsService, @TraiteLe)
            RETURNING id;
            """,
            Parametres(signalement), cancellationToken: ct));
        return signalement.Id;
    }

    public async Task MettreAJourAsync(Signalement signalement, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        await cx.ExecuteAsync(new CommandDefinition(
            """
            UPDATE signalement SET sortie_id = @SortieId, cause = @Cause, gravite = @Gravite, precision = @Precision,
                mis_hors_service = @MisHorsService, traite_le = @TraiteLe
            WHERE id = @Id;
            """,
            Parametres(signalement), cancellationToken: ct));
    }

    public async Task<Signalement?> ObtenirOuvertAsync(long equipementId, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.QuerySingleOrDefaultAsync<Signalement>(new CommandDefinition(
            $"SELECT {Colonnes} FROM signalement WHERE equipement_id = @equipementId AND traite_le IS NULL;",
            new { equipementId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Signalement>> ListerOuvertsAsync(CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<Signalement>(new CommandDefinition(
            $"SELECT {Colonnes} FROM signalement WHERE traite_le IS NULL ORDER BY signale_le DESC, id DESC;", cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task<IReadOnlyList<Signalement>> ListerAsync(long equipementId, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<Signalement>(new CommandDefinition(
            $"SELECT {Colonnes} FROM signalement WHERE equipement_id = @equipementId ORDER BY signale_le DESC, id DESC;",
            new { equipementId }, cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task<int> TraiterAsync(long equipementId, DateTime traiteLe, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.ExecuteAsync(new CommandDefinition(
            "UPDATE signalement SET traite_le = @traiteLe WHERE equipement_id = @equipementId AND traite_le IS NULL;",
            new { equipementId, traiteLe = FormatIso.Ecrire(traiteLe) }, cancellationToken: ct));
    }

    // Piège Dapper : enum et DateTime passés en texte (les TypeHandler ne servent qu'en lecture).
    private static object Parametres(Signalement s) => new
    {
        s.Id,
        s.EquipementId,
        s.SortieId,
        s.Cause,
        Gravite = s.Gravite.ToString(),
        s.Precision,
        SignaleLe = FormatIso.Ecrire(s.SignaleLe),
        MisHorsService = s.MisHorsService ? 1 : 0,
        TraiteLe = FormatIso.Ecrire(s.TraiteLe),
    };
}
