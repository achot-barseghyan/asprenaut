using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.App.Apparence;

/// <summary>
/// Pictogramme et couleur choisis pour chaque type de sortie (nom → type), retenus à chaque lecture des types (démarrage,
/// Paramètres, Nouvelle sortie…) : les écrans qui ne connaissent d'un type que son nom et sa famille (résumés de sorties,
/// utilisations, fiche d'un membre) dessinent ainsi le bon pictogramme et la bonne couleur.
/// </summary>
public static class TypesSortieRetenus
{
    private static Dictionary<string, TypeDeSortie> _types = new(StringComparer.CurrentCultureIgnoreCase);

    public static void Retenir(IEnumerable<TypeDeSortie> types) =>
        _types = types.GroupBy(t => t.Nom.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.CurrentCultureIgnoreCase);

    public static TypeDeSortie? Trouver(string? nom) =>
        nom is not null && _types.TryGetValue(nom.Trim(), out var type) ? type : null;
}
