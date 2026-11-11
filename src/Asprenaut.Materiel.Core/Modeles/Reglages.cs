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

    // --- Règles des sorties ---

    /// <summary>Une sortie en cours dont du matériel est encore dehors après ce nombre de jours est signalée.</summary>
    public int JoursAvantAlerteRetour { get; set; } = 1;

    /// <summary>Une révision qui tombe dans ce nombre de jours est signalée dans l'inventaire.</summary>
    public int JoursAvantRevision { get; set; } = 30;

    /// <summary>Un équipement à la révision dépassée peut partir (avec un avertissement).</summary>
    public bool DepartSiRevisionDepassee { get; set; } = true;

    /// <summary>Demander une confirmation avant de clôturer s'il reste du matériel dehors.</summary>
    public bool ConfirmerCloture { get; set; } = true;

    // --- Révisions (délais de la maquette « Révisions ») ---

    /// <summary>TIV d'un bloc : au plus tous les 12 mois.</summary>
    public int MoisEntreTiv { get; set; } = 12;

    /// <summary>Requalification d'un bloc dont la TIV annuelle est faite : tous les 6 ans.</summary>
    public int AnneesEntreRequalifications { get; set; } = 6;

    /// <summary>Révision des équipements dont la catégorie n'a pas de délai propre (délai du fabricant par défaut).</summary>
    public int MoisEntreRevisions { get; set; } = 12;

    /// <summary>
    /// Délai de révision propre à une catégorie, en mois : détendeurs tous les 2 ans ; 0 = pas de date obligatoire (gilets).
    /// </summary>
    public Dictionary<string, int> MoisEntreRevisionsParCategorie { get; set; } = new()
    {
        ["Détendeurs"] = 24,
        ["Gilets"] = 0,
    };

    /// <summary>Contrôle « à surveiller » : contrôle anticipé dans ce nombre de mois.</summary>
    public int MoisSiASurveiller { get; set; } = 6;

    /// <summary>Échéancier : une échéance dans ce nombre de jours est « à planifier ».</summary>
    public int JoursAPlanifier { get; set; } = 90;

    // --- Compteur d'utilisation (voir ServiceCompteur) ---

    /// <summary>Catégories qui ont un seuil de sorties réglable, dans l'ordre de l'écran Paramètres.</summary>
    public static readonly string[] CategoriesCompteur = ["Détendeurs", "Gilets", "Ordinateurs", "Blocs"];

    /// <summary>Révision après ce nombre de sorties depuis le dernier contrôle, par catégorie ; 0 = désactivé.</summary>
    public Dictionary<string, int> SeuilsSorties { get; set; } = new()
    {
        ["Détendeurs"] = 100,
        ["Gilets"] = 100,
        ["Ordinateurs"] = 0,
        ["Blocs"] = 0,
    };

    /// <summary>Seuils propres à un modèle ou à un équipement (notice du fabricant) : ils remplacent celui de la catégorie.</summary>
    public List<SeuilSpecifique> SeuilsSpecifiques { get; set; } = [];

    /// <summary>À 90 % du seuil, l'équipement passe « à planifier » dans Révisions.</summary>
    public bool PrevenirAvantSeuil { get; set; } = true;

    /// <summary>Seuil atteint : refusé au départ tant que la révision n'est pas enregistrée (sinon, avertissement).</summary>
    public bool BloquerAuSeuil { get; set; }

    // --- Données ---

    /// <summary>Dossier des sauvegardes ; null = "Sauvegardes" à côté de la base.</summary>
    public string? DossierSauvegardes { get; set; }

    // --- Sécurité · mode gestion (voir ServiceGestion) ---

    public static readonly int[] MinutesDeverrouillagePossibles = [5, 15, 30, 60];

    /// <summary>Code gestionnaire haché (PBKDF2, jamais le code lui-même) ; null = aucun code, rien n'est verrouillé.</summary>
    public string? CodeGestionnaire { get; set; }

    /// <summary>Sans activité pendant ce nombre de minutes, l'application repasse en consultation.</summary>
    public int MinutesDeverrouillage { get; set; } = 15;

    /// <summary>Au prochain lancement, l'application démarre en consultation (sinon elle reprend le mode gestion laissé ouvert).</summary>
    public bool VerrouillerALaFermeture { get; set; } = true;

    /// <summary>Clôturer une sortie en laissant du matériel manquant demande le code.</summary>
    public bool CodePourCloturerAvecManquants { get; set; }

    /// <summary>Mode gestion ouvert à la fermeture (utilisé seulement si <see cref="VerrouillerALaFermeture"/> est coupé).</summary>
    public bool DeverrouilleAuDemarrage { get; set; }

    // --- Affichage ---

    /// <summary>Thème de l'interface : sombre (par défaut) ou clair.</summary>
    public ModeAffichage Affichage { get; set; } = ModeAffichage.Sombre;

    // --- Club ---

    public string NomClub { get; set; } = "Asprenaut";

    public string ResponsableMateriel { get; set; } = "";

    /// <summary>Numéro d'affiliation FFESSM (ex. « 14 71-0025 »), repris dans l'identification marquée sur le matériel.</summary>
    public string NumeroClub { get; set; } = "";

    // --- Manuels ---

    /// <summary>
    /// Notices fournies avec l'appli déjà installées (nom de leur fichier) : chacune une seule fois, un manuel retiré ne revient
    /// pas, une notice ajoutée dans une nouvelle version de l'appli s'installe au lancement suivant.
    /// </summary>
    public List<string> ManuelsFournis { get; set; } = [];
}
