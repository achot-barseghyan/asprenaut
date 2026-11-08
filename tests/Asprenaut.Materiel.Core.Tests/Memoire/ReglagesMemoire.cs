using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;

namespace Asprenaut.Materiel.Core.Tests.Memoire;

/// <summary>Stockage des réglages qui compte les enregistrements.</summary>
public sealed class StockageReglagesMemoire : IStockageReglages
{
    public int Enregistrements { get; private set; }

    public void Enregistrer(Reglages reglages) => Enregistrements++;
}

/// <summary>Service de sauvegarde factice : accepte tout dossier sauf "INTERDIT".</summary>
public sealed class SauvegardeFactice : IServiceSauvegarde
{
    public string Dossier { get; private set; } = @"C:\Sauvegardes";

    public int NombreAConserver => 20;

    public Task<string?> SauvegarderAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);

    public DateTime? DerniereSauvegarde() => null;

    public ResultatOperation ChangerDossier(string dossier)
    {
        if (dossier.Contains("INTERDIT", StringComparison.Ordinal))
        {
            return ResultatOperation.Echec("Impossible d'écrire dans ce dossier.");
        }

        Dossier = dossier;
        return ResultatOperation.Ok("Dossier des sauvegardes enregistré.");
    }

    public Task<ResultatOperation> RestaurerAsync(string fichier, CancellationToken ct = default) =>
        Task.FromResult(ResultatOperation.Ok("Sauvegarde restaurée."));
}
