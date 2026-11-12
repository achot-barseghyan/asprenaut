using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>
/// Types de sortie du club (Paramètres) : ajouter, renommer, changer le pictogramme, masquer, supprimer.
/// Nom obligatoire et unique (sans tenir compte des majuscules), 40 caractères au plus. Un type utilisé par une sortie
/// ne se supprime pas : on le masque (il n'est plus proposé, les sorties le gardent). Il reste toujours un type proposé.
/// Action protégée (Paramètres).
/// </summary>
public sealed class ServiceTypesSortie(ITypeSortieRepository types, ServiceGestion? gestion = null)
{
    private ResultatOperation Autorisation() => gestion?.Exiger(ActionProtegee.Parametres) ?? ResultatOperation.Ok("");

    /// <summary>Tous les types (masqués compris) avec leur nombre de sorties, pour Paramètres.</summary>
    public Task<IReadOnlyList<TypeDeSortie>> ListerAsync(CancellationToken ct = default) => types.ListerAsync(ct);

    /// <summary>Types proposés pour une sortie : les actifs, plus celui de la sortie modifiée s'il a été masqué depuis.</summary>
    public async Task<IReadOnlyList<TypeDeSortie>> ListerProposesAsync(long? typeActuel = null, CancellationToken ct = default) =>
        (await types.ListerAsync(ct)).Where(t => t.Actif || t.Id == typeActuel).ToList();

    public async Task<ResultatOperation> CreerAsync(string nom, TypeSortie pictogramme, CancellationToken ct = default)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        var tous = await types.ListerAsync(ct);
        if (Valider(nom, pictogramme, tous, null) is { } erreur)
        {
            return erreur;
        }

        var type = new TypeDeSortie
        {
            Nom = nom.Trim(),
            Pictogramme = pictogramme,
            Actif = true,
            Ordre = tous.Count == 0 ? 1 : tous.Max(t => t.Ordre) + 1,
            Utilisations = TypeDeSortie.UtilisationsParDefaut(pictogramme),
        };
        var id = await types.AjouterAsync(type, ct);
        return ResultatOperation.Ok($"Type « {type.Nom} » ajouté.", id);
    }

    /// <summary>Renomme, change le pictogramme ou masque / réaffiche un type.</summary>
    public async Task<ResultatOperation> ModifierAsync(long id, string nom, TypeSortie pictogramme, bool actif, CancellationToken ct = default)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        var tous = await types.ListerAsync(ct);
        if (tous.FirstOrDefault(t => t.Id == id) is not { } type)
        {
            return ResultatOperation.Echec("Ce type de sortie n'existe plus.");
        }

        if (Valider(nom, pictogramme, tous, id) is { } erreur)
        {
            return erreur;
        }

        if (!actif && type.Actif && !tous.Any(t => t.Actif && t.Id != id))
        {
            return ResultatOperation.Echec("Gardez au moins un type proposé pour les nouvelles sorties.");
        }

        type.Nom = nom.Trim();
        type.Pictogramme = pictogramme;
        type.Actif = actif;
        await types.MettreAJourAsync(type, ct);
        return ResultatOperation.Ok($"Type « {type.Nom} » enregistré.", id);
    }

    /// <summary>
    /// Utilisations comptées par défaut pour une sortie de ce type (1 à 10). Ne change pas les sorties déjà créées :
    /// chacune garde les siennes. Retourne la valeur retenue.
    /// </summary>
    public async Task<ResultatOperation> ChangerUtilisationsAsync(long id, int utilisations, CancellationToken ct = default)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        if (await types.ObtenirAsync(id, ct) is not { } type)
        {
            return ResultatOperation.Echec("Ce type de sortie n'existe plus.");
        }

        type.Utilisations = Math.Clamp(utilisations, TypeDeSortie.UtilisationsMin, TypeDeSortie.UtilisationsMax);
        await types.MettreAJourAsync(type, ct);
        return ResultatOperation.Ok("Enregistré.", type.Utilisations);
    }

    /// <summary>
    /// Case « Nom de l'adhérent à chaque scan » : les sorties de ce type créées ensuite sont nominatives (ou ne le sont plus).
    /// Les sorties existantes gardent leur réglage.
    /// </summary>
    public async Task<ResultatOperation> ChangerNominatifAsync(long id, bool nominatif, CancellationToken ct = default)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        if (await types.ObtenirAsync(id, ct) is not { } type)
        {
            return ResultatOperation.Echec("Ce type de sortie n'existe plus.");
        }

        type.Nominatif = nominatif;
        await types.MettreAJourAsync(type, ct);
        return ResultatOperation.Ok(nominatif
            ? $"« {type.Nom} » : le nom de l'adhérent sera demandé à chaque scan (nouvelles sorties)."
            : $"« {type.Nom} » : plus de nom demandé au départ (nouvelles sorties).");
    }

    /// <summary>Bouton œil : masque un type proposé, ou réaffiche un type masqué.</summary>
    public async Task<ResultatOperation> BasculerMasqueAsync(long id, CancellationToken ct = default)
    {
        if (await types.ObtenirAsync(id, ct) is not { } type)
        {
            return ResultatOperation.Echec("Ce type de sortie n'existe plus.");
        }

        var etaitPropose = type.Actif;
        var resultat = await ModifierAsync(id, type.Nom, type.Pictogramme, !etaitPropose, ct);
        return resultat.Reussi
            ? ResultatOperation.Ok(etaitPropose ? $"« {type.Nom} » masqué." : $"« {type.Nom} » de nouveau proposé.", id)
            : resultat;
    }

    /// <summary>Supprime un type qu'aucune sortie n'utilise ; sinon, explique qu'il faut le masquer.</summary>
    public async Task<ResultatOperation> SupprimerAsync(long id, CancellationToken ct = default)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        var tous = await types.ListerAsync(ct);
        if (tous.FirstOrDefault(t => t.Id == id) is not { } type)
        {
            return ResultatOperation.Echec("Ce type de sortie n'existe plus.");
        }

        if (type.NbSorties > 0)
        {
            return ResultatOperation.Echec(
                $"« {type.Nom} » est utilisé par {Libelles.Pluriel(type.NbSorties, "sortie")} : il ne peut pas être supprimé. Vous pouvez le masquer (bouton œil).");
        }

        if (type.Actif && !tous.Any(t => t.Actif && t.Id != id))
        {
            return ResultatOperation.Echec("Gardez au moins un type proposé pour les nouvelles sorties.");
        }

        // La requête re-vérifie : une sortie créée entre-temps avec ce type empêche la suppression.
        return await types.SupprimerAsync(id, ct)
            ? ResultatOperation.Ok($"Type « {type.Nom} » supprimé.")
            : ResultatOperation.Echec($"« {type.Nom} » vient d'être utilisé par une sortie : il ne peut pas être supprimé.");
    }

    private static ResultatOperation? Valider(string? nom, TypeSortie pictogramme, IReadOnlyList<TypeDeSortie> tous, long? id)
    {
        var n = (nom ?? "").Trim();
        if (n.Length == 0)
        {
            return ResultatOperation.Echec("Indiquez le nom du type de sortie.");
        }

        if (n.Length > TypeDeSortie.LongueurMaxNom)
        {
            return ResultatOperation.Echec($"Nom trop long ({TypeDeSortie.LongueurMaxNom} caractères au plus).");
        }

        if (!Enum.IsDefined(pictogramme))
        {
            return ResultatOperation.Echec("Choisissez un pictogramme.");
        }

        return tous.Any(t => t.Id != id && string.Equals(t.Nom.Trim(), n, StringComparison.CurrentCultureIgnoreCase))
            ? ResultatOperation.Echec($"Le type « {n} » existe déjà.")
            : null;
    }
}
