namespace Asprenaut.Materiel.Core.Abstractions;

/// <summary>Sauvegarde de la base (copie horodatée, rotation des anciennes copies).</summary>
public interface IServiceSauvegarde
{
    /// <summary>Crée une copie de la base. Retourne le chemin de la copie, ou null si la base n'existe pas encore.</summary>
    Task<string?> SauvegarderAsync(CancellationToken ct = default);
}
