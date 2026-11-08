using Asprenaut.Materiel.Core.Resultats;

namespace Asprenaut.Materiel.Core.Abstractions;

/// <summary>Sauvegarde de la base (copie horodatée, rotation des anciennes copies) et restauration.</summary>
public interface IServiceSauvegarde
{
    /// <summary>Dossier où sont écrites les copies.</summary>
    string Dossier { get; }

    /// <summary>Nombre de copies conservées (les plus anciennes sont supprimées).</summary>
    int NombreAConserver { get; }

    /// <summary>Crée une copie de la base. Retourne le chemin de la copie, ou null si la base n'existe pas encore.</summary>
    Task<string?> SauvegarderAsync(CancellationToken ct = default);

    /// <summary>Date (locale) de la copie la plus récente du dossier, ou null s'il n'y en a pas.</summary>
    DateTime? DerniereSauvegarde();

    /// <summary>Change le dossier des copies (créé s'il n'existe pas, refusé s'il est inaccessible).</summary>
    ResultatOperation ChangerDossier(string dossier);

    /// <summary>
    /// Remplace les données par celles d'une copie, après avoir sauvegardé les données actuelles.
    /// Refuse un fichier qui n'est pas une base Asprenaut.
    /// </summary>
    Task<ResultatOperation> RestaurerAsync(string fichier, CancellationToken ct = default);
}
