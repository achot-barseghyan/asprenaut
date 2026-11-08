namespace Asprenaut.Materiel.Core.Modeles;

/// <summary>
/// Réglages du club, modifiables dans l'écran Paramètres (enregistrés dans parametres.json).
/// Une seule instance partagée : les services lisent toujours les valeurs à jour.
/// </summary>
public sealed class Reglages
{
    public const int JoursAlerteMin = 1;
    public const int JoursAlerteMax = 14;
    public static readonly int[] JoursRevisionPossibles = [15, 30, 60];

    // --- Pistolet scanner ---

    /// <summary>Bip au succès, son grave en cas d'erreur.</summary>
    public bool SonsActifs { get; set; } = true;

    /// <summary>Le curseur revient seul dans la zone de scan.</summary>
    public bool GarderFocusScan { get; set; } = true;

    /// <summary>Même code lu deux fois en moins d'une seconde : la seconde lecture est ignorée.</summary>
    public bool IgnorerDoublesLectures { get; set; } = true;

    /// <summary>Préfixe imprimé dans les QR codes, retiré à la lecture.</summary>
    public string PrefixeEtiquette { get; set; } = Saisie.CodeScanne.Prefixe;

    // --- Règles de prêt ---

    /// <summary>Une sortie en cours dont du matériel est encore dehors après ce nombre de jours est signalée.</summary>
    public int JoursAvantAlerteRetour { get; set; } = 1;

    /// <summary>Une révision qui tombe dans ce nombre de jours est signalée dans l'inventaire.</summary>
    public int JoursAvantRevision { get; set; } = 30;

    /// <summary>Un équipement à la révision dépassée peut partir (avec un avertissement).</summary>
    public bool DepartSiRevisionDepassee { get; set; } = true;

    /// <summary>Demander une confirmation avant de clôturer s'il reste du matériel dehors.</summary>
    public bool ConfirmerCloture { get; set; } = true;

    // --- Données ---

    /// <summary>Dossier des sauvegardes ; null = "Sauvegardes" à côté de la base.</summary>
    public string? DossierSauvegardes { get; set; }

    // --- Club ---

    public string NomClub { get; set; } = "Asprenaut";

    public string ResponsableMateriel { get; set; } = "";
}
