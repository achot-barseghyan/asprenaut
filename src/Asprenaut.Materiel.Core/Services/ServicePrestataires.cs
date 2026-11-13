using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>
/// Prestataires (magasins, organismes) chez qui le matériel part en révision. Gérés dans Paramètres (action protégée
/// Paramètres). Jamais supprimés : un prestataire inactif n'est plus proposé, mais les révisions passées le gardent.
/// </summary>
public sealed class ServicePrestataires(
    IPrestataireRepository depot,
    IJournalRepository journal,
    IHorloge horloge,
    ServiceGestion? gestion = null)
{
    public const int LongueurNom = 60;

    public Task<IReadOnlyList<Prestataire>> ListerAsync(CancellationToken ct = default) => depot.ListerAsync(ct);

    /// <summary>Prestataires proposés pour un envoi.</summary>
    public async Task<IReadOnlyList<Prestataire>> ListerActifsAsync(CancellationToken ct = default) =>
        (await depot.ListerAsync(ct)).Where(p => p.Actif).ToList();

    /// <summary>
    /// Ajoute (<paramref name="id"/> null) ou modifie un prestataire : nom obligatoire, unique sans tenir compte des majuscules,
    /// <see cref="LongueurNom"/> caractères au plus.
    /// </summary>
    public async Task<ResultatOperation> EnregistrerAsync(long? id, string nom, TypePrestataire type, string? telephone, string? adresse, CancellationToken ct = default)
    {
        if (gestion?.Exiger(ActionProtegee.Parametres) is { Reussi: false } refus)
        {
            return refus;
        }

        var propre = (nom ?? "").Trim();
        if (propre.Length == 0)
        {
            return ResultatOperation.Echec("Indiquez le nom du magasin ou de l'organisme.");
        }

        if (propre.Length > LongueurNom)
        {
            return ResultatOperation.Echec($"Le nom ne doit pas dépasser {LongueurNom} caractères.");
        }

        var tous = await depot.ListerAsync(ct);
        if (tous.Any(p => p.Id != id && string.Equals(p.Nom.Trim(), propre, StringComparison.OrdinalIgnoreCase)))
        {
            return ResultatOperation.Echec($"« {propre} » existe déjà.");
        }

        Prestataire prestataire;
        if (id is { } existant)
        {
            if (await depot.ObtenirAsync(existant, ct) is not { } trouve)
            {
                return ResultatOperation.Echec("Prestataire introuvable.");
            }

            prestataire = trouve;
        }
        else
        {
            prestataire = new Prestataire();
        }

        prestataire.Nom = propre;
        prestataire.Type = type;
        prestataire.Telephone = (telephone ?? "").Trim();
        prestataire.Adresse = (adresse ?? "").Trim();
        if (id is null)
        {
            await depot.AjouterAsync(prestataire, ct);
            await JournaliserAsync(ActionsJournal.PrestataireCree, $"Prestataire ajouté : {propre}", ct);
            return ResultatOperation.Ok($"{propre} ajouté.", prestataire.Id);
        }

        await depot.MettreAJourAsync(prestataire, ct);
        await JournaliserAsync(ActionsJournal.PrestataireModifie, $"Prestataire modifié : {propre}", ct);
        return ResultatOperation.Ok($"{propre} modifié.", prestataire.Id);
    }

    /// <summary>Désactive (plus proposé pour un envoi) ou réactive un prestataire.</summary>
    public async Task<ResultatOperation> ChangerActifAsync(long id, bool actif, CancellationToken ct = default)
    {
        if (gestion?.Exiger(ActionProtegee.Parametres) is { Reussi: false } refus)
        {
            return refus;
        }

        if (await depot.ObtenirAsync(id, ct) is not { } prestataire)
        {
            return ResultatOperation.Echec("Prestataire introuvable.");
        }

        prestataire.Actif = actif;
        await depot.MettreAJourAsync(prestataire, ct);
        await JournaliserAsync(ActionsJournal.PrestataireModifie, $"Prestataire {(actif ? "réactivé" : "désactivé")} : {prestataire.Nom}", ct);
        return ResultatOperation.Ok(actif ? $"{prestataire.Nom} est de nouveau proposé." : $"{prestataire.Nom} n'est plus proposé pour les envois.", id);
    }

    private Task JournaliserAsync(string action, string details, CancellationToken ct) =>
        journal.AjouterAsync(new EntreeJournal { Horodatage = horloge.MaintenantUtc, Action = action, Details = details }, ct);
}
