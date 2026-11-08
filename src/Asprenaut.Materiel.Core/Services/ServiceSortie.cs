using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>Données saisies sur l'écran "Nouvelle sortie".</summary>
public sealed record NouvelleSortie(
    DateOnly Date,
    TypeSortie Type,
    string Intitule,
    long? MoniteurId,
    string Lieu = "",
    string Notes = "");

/// <summary>Cycle de vie d'une sortie : création, validation du départ, clôture.</summary>
public sealed class ServiceSortie(
    ISortieRepository sorties,
    IMoniteurRepository moniteurs,
    IJournalRepository journal,
    IHorloge horloge)
{
    public Task<IReadOnlyList<SortieResume>> ListerEnCoursAsync(CancellationToken ct = default) =>
        sorties.ListerResumesAsync(cloturees: false, ct);

    public Task<IReadOnlyList<SortieResume>> ListerClotureesAsync(CancellationToken ct = default) =>
        sorties.ListerResumesAsync(cloturees: true, ct);

    public Task<SortieResume?> ObtenirResumeAsync(long sortieId, CancellationToken ct = default) =>
        sorties.ObtenirResumeAsync(sortieId, ct);

    public Task<IReadOnlyList<LigneSortie>> ListerMaterielAsync(long sortieId, CancellationToken ct = default) =>
        sorties.ListerLignesAsync(sortieId, ct);

    public async Task<ResultatOperation> CreerAsync(NouvelleSortie demande, CancellationToken ct = default)
    {
        var intitule = demande.Intitule.Trim();
        if (intitule.Length == 0)
        {
            return ResultatOperation.Echec("Indiquez un intitulé.");
        }

        if (demande.MoniteurId is not { } moniteurId)
        {
            return ResultatOperation.Echec("Choisissez le moniteur responsable.");
        }

        var moniteur = await moniteurs.ObtenirAsync(moniteurId, ct);
        if (moniteur is null || !moniteur.Actif)
        {
            return ResultatOperation.Echec("Ce moniteur n'est pas disponible.");
        }

        var sortie = new Sortie
        {
            DateSortie = demande.Date,
            Type = demande.Type,
            Intitule = intitule,
            MoniteurId = moniteurId,
            Lieu = demande.Lieu.Trim(),
            Notes = demande.Notes.Trim(),
            Statut = StatutSortie.EnPreparation,
        };
        var id = await sorties.AjouterAsync(sortie, ct);
        await JournaliserAsync(ActionsJournal.SortieCreee, $"Sortie {id} « {intitule} » du {Libelles.Date(demande.Date)} ({moniteur.NomComplet})", ct);
        return ResultatOperation.Ok("Sortie créée.", id);
    }

    /// <summary>Fin de la préparation : le matériel est parti, on passe au retour.</summary>
    public async Task<ResultatOperation> ValiderDepartAsync(long sortieId, CancellationToken ct = default)
    {
        var sortie = await sorties.ObtenirAsync(sortieId, ct);
        if (sortie is null)
        {
            return ResultatOperation.Echec("Sortie introuvable.");
        }

        if (sortie.Statut != StatutSortie.EnPreparation)
        {
            return ResultatOperation.Echec("Le départ est déjà validé.");
        }

        var affectations = await sorties.ListerAffectationsAsync(sortieId, ct);
        if (affectations.Count == 0)
        {
            return ResultatOperation.Echec("Aucun matériel scanné. Scannez au moins un équipement.");
        }

        sortie.Statut = StatutSortie.EnCours;
        sortie.DepartValideLe = horloge.MaintenantUtc;
        await sorties.MettreAJourAsync(sortie, ct);
        await JournaliserAsync(ActionsJournal.DepartValide, $"Départ validé pour la sortie {sortieId} ({Libelles.Pluriel(affectations.Count, "équipement")})", ct);
        return ResultatOperation.Ok($"Départ validé : {Libelles.Pluriel(affectations.Count, "équipement")}.", sortieId);
    }

    /// <summary>Nombre d'équipements qui deviendront manquants, et message de confirmation.</summary>
    public async Task<ApercuCloture> ApercuClotureAsync(long sortieId, CancellationToken ct = default)
    {
        var affectations = await sorties.ListerAffectationsAsync(sortieId, ct);
        var nb = affectations.Count(a => a.Statut == StatutSortieEquipement.Dehors);
        var message = nb == 0
            ? "Tout le matériel est rendu. Clôturer la sortie ?"
            : $"{Libelles.Pluriel(nb, "équipement")} pas encore {(nb > 1 ? "rendus" : "rendu")}. "
              + $"{(nb > 1 ? "Ils seront notés manquants" : "Il sera noté manquant")}. Clôturer quand même ?";
        return new ApercuCloture(nb, message);
    }

    /// <summary>Clôture : tout ce qui est encore dehors passe à "Manquant".</summary>
    public async Task<ResultatOperation> CloturerAsync(long sortieId, CancellationToken ct = default)
    {
        var sortie = await sorties.ObtenirAsync(sortieId, ct);
        if (sortie is null)
        {
            return ResultatOperation.Echec("Sortie introuvable.");
        }

        if (sortie.Statut == StatutSortie.Cloturee)
        {
            return ResultatOperation.Echec("Cette sortie est déjà clôturée.");
        }

        if (sortie.Statut == StatutSortie.EnPreparation)
        {
            return ResultatOperation.Echec("Validez d'abord le départ.");
        }

        var manquants = (await sorties.ListerAffectationsAsync(sortieId, ct))
            .Where(a => a.Statut == StatutSortieEquipement.Dehors)
            .ToList();
        foreach (var a in manquants)
        {
            a.Statut = StatutSortieEquipement.Manquant;
        }

        sortie.Statut = StatutSortie.Cloturee;
        sortie.ClotureeLe = horloge.MaintenantUtc;
        await sorties.EnregistrerClotureAsync(sortie, manquants, ct);

        if (manquants.Count > 0)
        {
            var codes = (await sorties.ListerLignesAsync(sortieId, ct)).ToDictionary(l => l.EquipementId, l => l.Code);
            foreach (var a in manquants)
            {
                var code = codes.GetValueOrDefault(a.EquipementId, $"Équipement {a.EquipementId}");
                await JournaliserAsync(ActionsJournal.ManquantDeclare, $"{code} manquant (sortie {sortieId})", ct);
            }
        }

        await JournaliserAsync(ActionsJournal.SortieCloturee, $"Sortie {sortieId} clôturée, {Libelles.Pluriel(manquants.Count, "manquant")}", ct);

        return manquants.Count == 0
            ? ResultatOperation.Ok("Sortie clôturée. Tout est rendu.", sortieId)
            : ResultatOperation.Ok($"Sortie clôturée avec {Libelles.Pluriel(manquants.Count, "manquant")}.", sortieId);
    }

    private Task JournaliserAsync(string action, string details, CancellationToken ct) =>
        journal.AjouterAsync(new EntreeJournal { Horodatage = horloge.MaintenantUtc, Action = action, Details = details }, ct);
}
