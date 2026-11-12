namespace Asprenaut.Materiel.Core.Modeles;

/// <summary>
/// Type de sortie du club (table type_sortie), géré dans Paramètres : « Piscine », « Sortie bateau »...
/// Son <see cref="Pictogramme"/> reprend une des familles de <see cref="TypeSortie"/> (icône, regroupement).
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

    public long Id { get; set; }

    /// <summary>Utilisations ajoutées au compteur de chaque équipement rentré d'une sortie de ce type (valeur par défaut de la sortie).</summary>
    public int Utilisations { get; set; } = 1;
    public string Nom { get; set; } = "";
    public TypeSortie Pictogramme { get; set; } = TypeSortie.Autre;
    public bool Actif { get; set; } = true;

    /// <summary>Nom de l'adhérent demandé à chaque scan au départ (sorties créées ensuite ; Mer et Lac d'origine).</summary>
    public bool Nominatif { get; set; }
    public int Ordre { get; set; }

    /// <summary>Nombre de sorties de ce type (lecture seulement).</summary>
    public int NbSorties { get; set; }

    public override string ToString() => Nom;
}
