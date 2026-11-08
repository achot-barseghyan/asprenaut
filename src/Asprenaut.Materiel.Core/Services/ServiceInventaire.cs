using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Saisie;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>Inventaire : consultation et création des équipements.</summary>
public sealed class ServiceInventaire(
    IEquipementRepository equipements,
    ICategorieRepository categories,
    IJournalRepository journal,
    IHorloge horloge,
    Reglages reglages)
{
    /// <summary>
    /// Révision dépassée, proche (dans <see cref="Reglages.JoursAvantRevision"/> jours) ou à jour.
    /// Sert au badge jaune / rouge de l'inventaire.
    /// </summary>
    public EtatRevision EtatDeRevision(DateOnly? prochaineRevision)
    {
        if (prochaineRevision is not { } date)
        {
            return EtatRevision.AJour;
        }

        var aujourdHui = horloge.AujourdHui;
        if (date < aujourdHui)
        {
            return EtatRevision.Depassee;
        }

        return date <= aujourdHui.AddDays(reglages.JoursAvantRevision) ? EtatRevision.Proche : EtatRevision.AJour;
    }

    public Task<IReadOnlyList<Categorie>> ListerCategoriesAsync(CancellationToken ct = default) =>
        categories.ListerAsync(ct);

    public Task<IReadOnlyList<LigneInventaire>> ListerAsync(string? recherche, long? categorieId, CancellationToken ct = default) =>
        equipements.ListerInventaireAsync(string.IsNullOrWhiteSpace(recherche) ? null : recherche.Trim(), categorieId, ct);

    /// <summary>Crée un équipement (par exemple après le scan d'un code inconnu).</summary>
    public async Task<ResultatOperation> CreerAsync(Equipement nouveau, CancellationToken ct = default)
    {
        var code = CodeScanne.Nettoyer(nouveau.Code, reglages.PrefixeEtiquette);
        if (code.Length == 0)
        {
            return ResultatOperation.Echec("Indiquez un code.");
        }

        if (string.IsNullOrWhiteSpace(nouveau.Libelle))
        {
            return ResultatOperation.Echec("Indiquez un nom pour l'équipement.");
        }

        if (await equipements.ObtenirParCodeAsync(code, ct) is not null)
        {
            return ResultatOperation.Echec($"Le code {code} existe déjà.");
        }

        nouveau.Code = code;
        nouveau.Libelle = nouveau.Libelle.Trim();
        nouveau.CreeLe = horloge.MaintenantUtc;
        var id = await equipements.AjouterAsync(nouveau, ct);
        await journal.AjouterAsync(new EntreeJournal
        {
            Horodatage = horloge.MaintenantUtc,
            Action = ActionsJournal.EquipementCree,
            Details = $"{code} « {nouveau.Libelle} » créé",
        }, ct);
        return ResultatOperation.Ok($"{code} créé.", id);
    }
}
