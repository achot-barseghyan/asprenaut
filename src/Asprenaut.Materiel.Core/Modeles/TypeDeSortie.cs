namespace Asprenaut.Materiel.Core.Modeles;

/// <summary>
/// Type de sortie du club (table type_sortie), géré dans Paramètres : « Piscine », « Sortie bateau »...
/// Son <see cref="Pictogramme"/> reprend une des familles de <see cref="TypeSortie"/> (regroupement), déduite du pictogramme
/// choisi (<see cref="Icone"/>, 28 au choix) ; <see cref="Couleur"/> : une des 7 couleurs.
/// Masqué (<see cref="Actif"/> faux) : plus proposé pour une nouvelle sortie, mais gardé par les sorties qui l'ont.
/// </summary>
public sealed class TypeDeSortie
{
    public const int LongueurMaxNom = 40;

    /// <summary>Bornes des utilisations comptées par sortie (type ou sortie).</summary>
    public const int UtilisationsMin = 1;
    public const int UtilisationsMax = 10;

    /// <summary>Utilisations proposées pour un nouveau type d'après sa famille : mer 4, lac 2, sinon 1.</summary>
    public static int UtilisationsParDefaut(TypeSortie famille) => famille switch
    {
        TypeSortie.Mer => 4,
        TypeSortie.Lac => 2,
        _ => 1,
    };

    /// <summary>
    /// Pictogrammes au choix (maquette « Ajouter un type de sortie ») : clé enregistrée (type_sortie.icone), nom affiché et mots
    /// reconnus par la recherche et dans le nom tapé. Les tracés sont dans l'application.
    /// </summary>
    public static IReadOnlyList<(string Cle, string Nom, string MotsCles)> Icones { get; } =
    [
        ("piscine", "Piscine", "bassin"),
        ("fosse", "Fosse", "profondeur"),
        ("mer", "Mer", "vagues ocean"),
        ("lac", "Lac", "montagne"),
        ("bapteme", "Baptême", "etoile decouverte"),
        ("bateau", "Bateau", "bateau semi rigide zodiac"),
        ("carriere", "Carrière", "rochers graviere"),
        ("riviere", "Rivière", "courant fleuve"),
        ("nuit", "Nuit", "nuit lune"),
        ("epave", "Épave", "ancre wreck"),
        ("bio", "Bio", "poisson faune"),
        ("glace", "Glace", "sous glace froid flocon"),
        ("apnee", "Apnée", "respiration poumon"),
        ("nitrox", "Nitrox", "bloc bouteille gaz"),
        ("formation", "Formation", "cours ecole niveau"),
        ("examen", "Examen", "brevet validation"),
        ("secours", "Secourisme", "rifap secours croix"),
        ("photo", "Photo", "video camera"),
        ("voyage", "Voyage", "etranger monde globe"),
        ("weekend", "Week-end", "camping tente sejour"),
        ("ete", "Été", "soleil saison"),
        ("groupe", "Club", "groupe club"),
        ("event", "Événement", "fete forum drapeau"),
        ("entretien", "Entretien", "atelier tiv revision"),
        ("nettoyage", "Nettoyage", "ramassage dechets environnement"),
        ("compet", "Compétition", "trophee coupe"),
        ("enfant", "Enfants", "jeunes pack ado"),
        ("autre", "Autre", "points divers"),
    ];

    /// <summary>Couleurs au choix (clés, type_sortie.couleur), traduites en couleurs du thème par l'application.</summary>
    public static IReadOnlyList<(string Cle, string Nom)> Couleurs { get; } =
    [
        ("cyan", "Turquoise"), ("bleu", "Bleu"), ("vert", "Vert"), ("violet", "Violet"), ("jaune", "Jaune"), ("corail", "Corail"), ("rose", "Rose"),
    ];

    public long Id { get; set; }

    /// <summary>Utilisations ajoutées au compteur de chaque équipement rentré d'une sortie de ce type (valeur par défaut de la sortie).</summary>
    public int Utilisations { get; set; } = 1;
    public string Nom { get; set; } = "";
    public TypeSortie Pictogramme { get; set; } = TypeSortie.Autre;

    /// <summary>Clé du pictogramme choisi (<see cref="Icones"/>, migration 035), ou null : d'après la famille.</summary>
    public string? Icone { get; set; }

    /// <summary>Clé de la couleur choisie (<see cref="Couleurs"/>, migration 035), ou null : d'après la famille.</summary>
    public string? Couleur { get; set; }

    public string IconeEffective => EstIcone(Icone) ? Icone!.ToLowerInvariant() : IconeParDefaut(Pictogramme);

    public string CouleurEffective => EstCouleur(Couleur) ? Couleur!.ToLowerInvariant() : CouleurParDefaut(Pictogramme);
    public bool Actif { get; set; } = true;

    /// <summary>Nom de l'adhérent demandé à chaque scan au départ (sorties créées ensuite ; Mer et Lac d'origine).</summary>
    public bool Nominatif { get; set; }
    public int Ordre { get; set; }

    /// <summary>Nombre de sorties de ce type (lecture seulement).</summary>
    public int NbSorties { get; set; }

    public static bool EstIcone(string? cle) => Icones.Any(i => string.Equals(i.Cle, cle, StringComparison.OrdinalIgnoreCase));

    public static bool EstCouleur(string? cle) => Couleurs.Any(c => string.Equals(c.Cle, cle, StringComparison.OrdinalIgnoreCase));

    /// <summary>Nom affiché d'un pictogramme (« Bateau ») ; « Autre » si la clé est inconnue.</summary>
    public static string NomIcone(string? cle) =>
        Icones.FirstOrDefault(i => string.Equals(i.Cle, cle, StringComparison.OrdinalIgnoreCase)).Nom ?? "Autre";

    /// <summary>Pictogramme d'une famille (types d'origine et anciens types).</summary>
    public static string IconeParDefaut(TypeSortie famille) => famille switch
    {
        TypeSortie.Piscine => "piscine",
        TypeSortie.Fosse => "fosse",
        TypeSortie.Mer => "mer",
        TypeSortie.Lac => "lac",
        TypeSortie.Bapteme => "bapteme",
        _ => "autre",
    };

    /// <summary>Couleur d'une famille : piscine turquoise, baptême vert, lac bleu, mer corail, fosse violet, autre bleu.</summary>
    public static string CouleurParDefaut(TypeSortie famille) => famille switch
    {
        TypeSortie.Piscine => "cyan",
        TypeSortie.Bapteme => "vert",
        TypeSortie.Mer => "corail",
        TypeSortie.Fosse => "violet",
        _ => "bleu",
    };

    /// <summary>
    /// Famille d'un pictogramme (colonne sortie.type, utilisations proposées) : bateau, épave et bio comptent comme la mer ;
    /// carrière, rivière et glace comme le lac.
    /// </summary>
    public static TypeSortie FamilleDe(string? icone) => (icone ?? "").ToLowerInvariant() switch
    {
        "piscine" => TypeSortie.Piscine,
        "fosse" => TypeSortie.Fosse,
        "mer" or "bateau" or "epave" or "bio" => TypeSortie.Mer,
        "lac" or "carriere" or "riviere" or "glace" => TypeSortie.Lac,
        "bapteme" => TypeSortie.Bapteme,
        _ => TypeSortie.Autre,
    };

    /// <summary>Pictogrammes dont le nom ou un mot-clé contient la recherche (accents et majuscules ignorés) ; vide = tous.</summary>
    public static IReadOnlyList<(string Cle, string Nom, string MotsCles)> ChercherIcones(string? recherche)
    {
        var q = Normaliser(recherche);
        return q.Length == 0 ? Icones : Icones.Where(i => Normaliser(i.Nom + " " + i.MotsCles).Contains(q, StringComparison.Ordinal)).ToList();
    }

    /// <summary>
    /// Pictogramme deviné d'après le nom tapé (« Sortie bateau » → bateau, « Plongée de nuit » → nuit) : premier dont le nom
    /// ou un mot-clé de plus de 2 lettres apparaît dans le nom ; null si aucun.
    /// </summary>
    public static string? DevinerIcone(string? nom)
    {
        var n = Normaliser(nom);
        if (n.Length == 0)
        {
            return null;
        }

        foreach (var (cle, libelle, mots) in Icones)
        {
            if ((Normaliser(libelle) + " " + mots).Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(m => m.Length > 2 && n.Contains(m, StringComparison.Ordinal)))
            {
                return cle;
            }
        }

        return null;
    }

    private static string Normaliser(string? texte) =>
        new string((texte ?? "").Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());

    public override string ToString() => Nom;
}
