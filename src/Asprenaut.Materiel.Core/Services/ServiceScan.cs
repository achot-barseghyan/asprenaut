using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Saisie;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>
/// Règles métier du scan au départ et au retour.
/// Chaque méthode retourne un résultat affichable ; aucune exception pour les cas métier.
/// </summary>
public sealed class ServiceScan(
    IEquipementRepository equipements,
    ISortieRepository sorties,
    IJournalRepository journal,
    IHorloge horloge,
    Reglages reglages)
{
    /// <summary>Scan d'un équipement qui part avec la sortie.</summary>
    public async Task<ResultatScan> ScannerDepartAsync(long sortieId, string codeBrut, CancellationToken ct = default)
    {
        var code = CodeScanne.Nettoyer(codeBrut, reglages.PrefixeEtiquette);
        if (code.Length == 0)
        {
            return ResultatScan.Erreur("Code vide. Scannez à nouveau.");
        }

        var sortie = await sorties.ObtenirAsync(sortieId, ct);
        if (sortie is null)
        {
            return ResultatScan.Erreur("Sortie introuvable.");
        }

        if (sortie.Statut == StatutSortie.Cloturee)
        {
            return ResultatScan.Erreur("Cette sortie est clôturée. On ne peut plus y ajouter de matériel.");
        }

        var equipement = await equipements.ObtenirParCodeAsync(code, ct);
        if (equipement is null)
        {
            return new ResultatScan(
                NiveauResultat.Erreur,
                $"Code inconnu : {code}. Voulez-vous créer cet équipement ?",
                Action: ActionProposee.CreerEquipement,
                Code: code);
        }

        // Règle : deux scans du même équipement dans la même sortie → ignoré.
        var dejaDansSortie = await sorties.ObtenirAffectationAsync(sortieId, equipement.Id, ct);
        if (dejaDansSortie is not null)
        {
            return dejaDansSortie.Statut == StatutSortieEquipement.Dehors
                ? ResultatScan.Avertissement($"{equipement.Code} déjà scanné.", equipement)
                : ResultatScan.Avertissement($"{equipement.Code} a déjà été rendu pour cette sortie.", equipement);
        }

        // Règle : un équipement hors service ou réformé ne part pas.
        if (equipement.Etat == EtatEquipement.HorsService)
        {
            return ResultatScan.Erreur($"{equipement.Designation} est hors service. Il ne peut pas partir.", equipement);
        }

        if (equipement.Etat == EtatEquipement.Reforme)
        {
            return ResultatScan.Erreur($"{equipement.Designation} est réformé. Il ne peut pas partir.", equipement);
        }

        // Règle : révision dépassée → refusée si le club l'a décidé dans les réglages (sinon, part avec un avertissement, plus bas).
        if (!reglages.DepartSiRevisionDepassee && equipement.RevisionDepassee(horloge.AujourdHui))
        {
            return ResultatScan.Erreur(
                $"{equipement.Designation} : révision dépassée depuis le {Libelles.Date(equipement.DateProchaineRevision!.Value)}. Il ne peut pas partir.",
                equipement);
        }

        // Règle : un équipement n'est dehors que dans une seule sortie à la fois.
        var dehors = await sorties.ObtenirAffectationDehorsAsync(equipement.Id, ct);
        if (dehors is not null)
        {
            var autre = await sorties.ObtenirAsync(dehors.SortieId, ct);
            var nomAutre = autre is null
                ? $"n° {dehors.SortieId}"
                : $"« {autre.Intitule} » du {Libelles.Date(autre.DateSortie)}";
            return ResultatScan.Erreur($"{equipement.Code} est déjà dehors dans la sortie {nomAutre}.", equipement)
                with { AutreSortieId = dehors.SortieId };
        }

        await sorties.AjouterAffectationAsync(new SortieEquipement
        {
            SortieId = sortieId,
            EquipementId = equipement.Id,
            ScanneDepartLe = horloge.MaintenantUtc,
            Statut = StatutSortieEquipement.Dehors,
        }, ct);
        await JournaliserAsync(ActionsJournal.ScanDepart, $"{equipement.Code} part avec la sortie {sortie.Id} « {sortie.Intitule} »", ct);

        // Règle : révision dépassée → part quand même, avec un avertissement visible.
        if (equipement.RevisionDepassee(horloge.AujourdHui))
        {
            return ResultatScan.Avertissement(
                $"{equipement.Designation} ajouté. Attention : révision dépassée depuis le {Libelles.Date(equipement.DateProchaineRevision!.Value)}.",
                equipement);
        }

        return ResultatScan.Succes($"{equipement.Designation} ajouté.", equipement);
    }

    /// <summary>Scan d'un équipement qui rentre.</summary>
    public async Task<ResultatScan> ScannerRetourAsync(long sortieId, string codeBrut, CancellationToken ct = default)
    {
        var code = CodeScanne.Nettoyer(codeBrut, reglages.PrefixeEtiquette);
        if (code.Length == 0)
        {
            return ResultatScan.Erreur("Code vide. Scannez à nouveau.");
        }

        var sortie = await sorties.ObtenirAsync(sortieId, ct);
        if (sortie is null)
        {
            return ResultatScan.Erreur("Sortie introuvable.");
        }

        if (sortie.Statut == StatutSortie.EnPreparation)
        {
            return ResultatScan.Erreur("Le départ de cette sortie n'est pas encore validé.");
        }

        var equipement = await equipements.ObtenirParCodeAsync(code, ct);
        if (equipement is null)
        {
            return new ResultatScan(NiveauResultat.Erreur, $"Code inconnu : {code}.", Code: code);
        }

        var affectation = await sorties.ObtenirAffectationAsync(sortieId, equipement.Id, ct);
        if (affectation is not null)
        {
            switch (affectation.Statut)
            {
                case StatutSortieEquipement.Rendu:
                    return ResultatScan.Avertissement($"{equipement.Code} est déjà rendu.", equipement);

                case StatutSortieEquipement.Dehors:
                    await MarquerRenduAsync(affectation, ct);
                    await JournaliserAsync(ActionsJournal.ScanRetour, $"{equipement.Code} rendu (sortie {sortieId})", ct);
                    return ResultatScan.Succes($"{equipement.Designation} rendu.", equipement);

                default: // Manquant dans cette sortie (déjà clôturée) → retrouvé.
                    await MarquerRenduAsync(affectation, ct);
                    await JournaliserAsync(ActionsJournal.ManquantRetrouve, $"{equipement.Code} retrouvé et rendu (sortie {sortieId})", ct);
                    return ResultatScan.Succes($"{equipement.Designation} était manquant. Il est maintenant rendu.", equipement);
            }
        }

        // Absent de cette sortie, mais dehors ailleurs : on propose, on ne modifie rien.
        var dehors = await sorties.ObtenirAffectationDehorsAsync(equipement.Id, ct);
        if (dehors is not null)
        {
            var autre = await sorties.ObtenirAsync(dehors.SortieId, ct);
            var nomAutre = autre is null ? $"n° {dehors.SortieId}" : $"« {autre.Intitule} » du {Libelles.Date(autre.DateSortie)}";
            return new ResultatScan(
                NiveauResultat.Avertissement,
                $"{equipement.Code} n'est pas dans cette sortie. Il est dehors dans la sortie {nomAutre}. Le rentrer dans cette sortie ?",
                equipement,
                ActionProposee.RentrerDansAutreSortie,
                equipement.Code,
                dehors.SortieId);
        }

        // Manquant d'une sortie précédente → retrouvé.
        var manquant = await sorties.ObtenirDerniereAffectationManquanteAsync(equipement.Id, ct);
        if (manquant is not null)
        {
            await MarquerRenduAsync(manquant, ct);
            await JournaliserAsync(ActionsJournal.ManquantRetrouve, $"{equipement.Code} retrouvé et rendu (sortie {manquant.SortieId})", ct);
            return ResultatScan.Succes($"{equipement.Designation} était manquant. Il est maintenant rendu.", equipement);
        }

        return ResultatScan.Avertissement($"{equipement.Code} ne fait pas partie de cette sortie. Rien n'a été modifié.", equipement);
    }

    /// <summary>Retire un équipement scanné par erreur, uniquement pendant la préparation.</summary>
    public async Task<ResultatOperation> RetirerDuDepartAsync(long sortieId, long equipementId, CancellationToken ct = default)
    {
        var sortie = await sorties.ObtenirAsync(sortieId, ct);
        if (sortie is null)
        {
            return ResultatOperation.Echec("Sortie introuvable.");
        }

        if (sortie.Statut != StatutSortie.EnPreparation)
        {
            return ResultatOperation.Echec("Le départ est validé. On ne peut plus retirer de matériel.");
        }

        var affectation = await sorties.ObtenirAffectationAsync(sortieId, equipementId, ct);
        if (affectation is null)
        {
            return ResultatOperation.Echec("Cet équipement n'est pas dans la sortie.");
        }

        await sorties.SupprimerAffectationAsync(affectation.Id, ct);
        var equipement = await equipements.ObtenirAsync(equipementId, ct);
        var code = equipement?.Code ?? equipementId.ToString(Libelles.Culture);
        await JournaliserAsync(ActionsJournal.RetraitDepart, $"{code} retiré de la sortie {sortieId}", ct);
        return ResultatOperation.Ok($"{code} retiré.");
    }

    private Task MarquerRenduAsync(SortieEquipement affectation, CancellationToken ct)
    {
        affectation.Statut = StatutSortieEquipement.Rendu;
        affectation.ScanneRetourLe = horloge.MaintenantUtc;
        return sorties.MettreAJourAffectationAsync(affectation, ct);
    }

    private Task JournaliserAsync(string action, string details, CancellationToken ct) =>
        journal.AjouterAsync(new EntreeJournal { Horodatage = horloge.MaintenantUtc, Action = action, Details = details }, ct);
}
