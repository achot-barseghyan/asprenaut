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

/// <summary>Logo du club en mémoire (Club\logo.png).</summary>
public sealed class StockageLogoMemoire : IStockageLogo
{
    public byte[]? Png { get; private set; }

    public bool Refuser { get; set; }

    public string? Chemin() => Png is null ? null : @"C:\Club\logo.png";

    public void Enregistrer(byte[] png)
    {
        if (Refuser)
        {
            throw new IOException("Disque plein");
        }

        Png = png;
    }

    public void Supprimer() => Png = null;
}
