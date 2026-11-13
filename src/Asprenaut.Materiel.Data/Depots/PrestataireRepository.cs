using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Data.Connexion;
using Dapper;

namespace Asprenaut.Materiel.Data.Depots;

/// <summary>Magasins et organismes (table prestataire, migration 016).</summary>
public sealed class PrestataireRepository(FabriqueConnexion fabrique) : IPrestataireRepository
{
    private const string Select = "SELECT id, nom, type, telephone, adresse, actif FROM prestataire";

    public async Task<IReadOnlyList<Prestataire>> ListerAsync(CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        var lignes = await cx.QueryAsync<Prestataire>(new CommandDefinition(
            $"{Select} ORDER BY actif DESC, nom COLLATE NOCASE;", cancellationToken: ct));
        return lignes.AsList();
    }

    public async Task<Prestataire?> ObtenirAsync(long id, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        return await cx.QuerySingleOrDefaultAsync<Prestataire>(new CommandDefinition($"{Select} WHERE id = @id;", new { id }, cancellationToken: ct));
    }

    public async Task<long> AjouterAsync(Prestataire prestataire, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        prestataire.Id = await cx.ExecuteScalarAsync<long>(new CommandDefinition(
            "INSERT INTO prestataire (nom, type, telephone, adresse, actif) VALUES (@Nom, @Type, @Telephone, @Adresse, @Actif) RETURNING id;",
            Parametres(prestataire), cancellationToken: ct));
        return prestataire.Id;
    }

    public async Task MettreAJourAsync(Prestataire prestataire, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        await cx.ExecuteAsync(new CommandDefinition(
            "UPDATE prestataire SET nom = @Nom, type = @Type, telephone = @Telephone, adresse = @Adresse, actif = @Actif WHERE id = @Id;",
            Parametres(prestataire), cancellationToken: ct));
    }

    private static object Parametres(Prestataire p) => new
    {
        p.Id,
        p.Nom,
        Type = p.Type.ToString(),
        p.Telephone,
        p.Adresse,
        Actif = p.Actif ? 1 : 0,
    };
}
