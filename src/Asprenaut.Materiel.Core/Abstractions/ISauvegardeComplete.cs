using Asprenaut.Materiel.Core.Resultats;

namespace Asprenaut.Materiel.Core.Abstractions;

/// <summary>
/// Sauvegarde complète : un seul fichier .zip avec la base, les réglages (parametres.json, sans le chemin de la base) et les
/// PDF des manuels. Sert à garder une copie de tout (panne du PC) et à installer le PC du club en une fois.
/// La sauvegarde simple (<see cref="IServiceSauvegarde"/>) ne copie que la base.
/// </summary>
public interface ISauvegardeComplete
{
    /// <summary>Extension des sauvegardes complètes (« .zip ») ; les sauvegardes simples sont des « .db ».</summary>
    const string Extension = ".zip";

    /// <summary>Nom proposé : « Asprenaut sauvegarde complète 2026-11-14.zip ».</summary>
    string NomPropose();

    /// <summary>Écrit la sauvegarde complète dans ce fichier (remplacé s'il existe). Le nombre de manuels est dans le message.</summary>
    Task<ResultatOperation> CreerAsync(string fichierZip, CancellationToken ct = default);

    /// <summary>
    /// Remplace la base, les réglages et les manuels par ceux du fichier, après avoir vérifié tout le fichier (rien n'est
    /// changé s'il est incomplet ou abîmé) et gardé une copie des données et des réglages actuels. Les réglages ne sont
    /// pris en compte qu'au prochain démarrage : l'application doit redémarrer.
    /// </summary>
    Task<ResultatOperation> RestaurerAsync(string fichierZip, CancellationToken ct = default);
}

/// <summary>Les réglages tels qu'ils sont enregistrés (parametres.json), pour la sauvegarde complète.</summary>
public interface IReglagesSauvegardables
{
    /// <summary>Réglages actuels en JSON (sans ce qui est propre au PC : chemin de la base).</summary>
    string Lire();

    /// <summary>Vrai si ce JSON est lisible comme des réglages.</summary>
    bool EstLisible(string json);

    /// <summary>
    /// Remplace les réglages enregistrés par ceux-ci (pris en compte au prochain démarrage), en gardant ce qui est propre
    /// au PC (dossier des sauvegardes) et une copie des réglages d'avant.
    /// </summary>
    void Remplacer(string json);
}
