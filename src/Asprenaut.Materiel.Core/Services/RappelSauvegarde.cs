using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>État de la sauvegarde complète : à faire (rappel), message court, et date conseillée pour la suivante.</summary>
public sealed record EtatSauvegardeComplete(bool AFaire, string Message, DateTime? Derniere);

/// <summary>
/// Conseil : une sauvegarde complète par semaine, sur un stockage externe (clé USB, disque externe), pour ne rien perdre si
/// le PC du club tombe en panne ou est volé. Rappel sur l'écran Sorties dès qu'elle a 7 jours ou plus (ou s'il n'y en a jamais eu).
/// </summary>
public static class RappelSauvegarde
{
    /// <summary>Une sauvegarde complète par semaine.</summary>
    public const int JoursEntreSauvegardes = 7;

    public const string Conseil = "Conseil : une sauvegarde complète chaque semaine, sur une clé USB ou un disque externe.";

    /// <summary>État d'après la date de la dernière sauvegarde complète (heure locale).</summary>
    public static EtatSauvegardeComplete Etat(DateTime? derniere, DateTime maintenant)
    {
        if (derniere is not { } d)
        {
            return new EtatSauvegardeComplete(true, "Aucune sauvegarde complète pour l'instant.", null);
        }

        var jours = (maintenant.Date - d.Date).Days;
        var quand = jours <= 0 ? "aujourd'hui" : jours == 1 ? "hier" : $"il y a {jours} jours";
        var prochaine = d.Date.AddDays(JoursEntreSauvegardes);
        return jours >= JoursEntreSauvegardes
            ? new EtatSauvegardeComplete(true, $"Dernière sauvegarde complète {quand} : il est temps d'en refaire une.", d)
            : new EtatSauvegardeComplete(false, $"Dernière sauvegarde complète {quand}. Prochaine conseillée le {Libelles.Date(DateOnly.FromDateTime(prochaine))}.", d);
    }

    /// <summary>
    /// Vrai si le fichier est sur le même disque que la base (même lecteur, « C:\ ») : il serait perdu avec le PC.
    /// Un chemin réseau (\\serveur\…) ou un autre lecteur (clé USB, disque externe) est considéré comme externe.
    /// </summary>
    public static bool MemeDisque(string fichier, string cheminBase)
    {
        var a = Path.GetPathRoot(Path.GetFullPath(fichier));
        var b = Path.GetPathRoot(Path.GetFullPath(cheminBase));
        return !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
