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
    Reglages reglages,
    ServiceCompteur? compteur = null,
    ServiceGestion? gestion = null)
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

        // Règle : sortie nominative → pas de nouveau scan tant que le précédent n'a pas d'adhérent.
        if (sortie.Nominatif && ServiceSortie.AAttribuer(true, await sorties.ListerLignesAsync(sortieId, ct)) is { } enAttente)
        {
            return ResultatScan.Erreur($"Choisissez d'abord l'adhérent qui prend {enAttente.Code}. Ce scan n'a pas été pris en compte.");
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

        // Règle : un équipement manquant scanné au départ est là : il est noté retrouvé (rendu) dans la sortie où il manquait,
        // même si le départ est ensuite refusé. Le message le dit, avant celui du départ.
        var retrouve = await RetrouverManquantsAsync(equipement, sortie, ct);
        ResultatScan Avec(ResultatScan r) => retrouve is null ? r : r with { Message = $"{retrouve} {r.Message}" };

        // Règle : un équipement hors service ou réformé ne part pas.
        if (equipement.Etat == EtatEquipement.HorsService)
        {
            return Avec(ResultatScan.Erreur($"{equipement.Designation} est hors service. Il ne peut pas partir.", equipement));
        }

        if (equipement.Etat == EtatEquipement.Reforme)
        {
            return Avec(ResultatScan.Erreur($"{equipement.Designation} est réformé. Il ne peut pas partir.", equipement));
        }

        // Règle : révision dépassée → refusée si le club l'a décidé dans les réglages (sinon, part avec un avertissement, plus bas).
        if (!reglages.DepartSiRevisionDepassee && equipement.RevisionDepassee(horloge.AujourdHui))
        {
            return Avec(ResultatScan.Erreur(
                $"{equipement.Designation} : révision dépassée depuis le {Libelles.Date(equipement.EcheanceDepassee(horloge.AujourdHui)!.Value)}. Il ne peut pas partir.",
                equipement));
        }

        // Règle : seuil de sorties atteint (compteur d'utilisation) → refusé si le club bloque au seuil, sinon avertissement (plus bas).
        var usage = compteur is null ? null : await compteur.UsageAsync(equipement, ct);
        var seuilAtteint = usage?.Etat == EtatCompteur.Atteint;
        if (seuilAtteint && reglages.BloquerAuSeuil)
        {
            return Avec(ResultatScan.Erreur(
                $"{equipement.Designation} : {usage!.Utilisations} utilisations depuis sa dernière révision (seuil {usage.Seuil}). Révision à faire avant de repartir.",
                equipement));
        }

        // Règle : un équipement n'est dehors que dans une seule sortie à la fois.
        var dehors = await sorties.ObtenirAffectationDehorsAsync(equipement.Id, ct);
        if (dehors is not null)
        {
            var autre = await sorties.ObtenirAsync(dehors.SortieId, ct);
            var nomAutre = autre is null
                ? $"n° {dehors.SortieId}"
                : $"« {autre.Intitule} » du {Libelles.Date(autre.DateSortie)}";
            return Avec(ResultatScan.Erreur($"{equipement.Code} est déjà dehors dans la sortie {nomAutre}.", equipement)
                with { AutreSortieId = dehors.SortieId });
        }

        await sorties.AjouterAffectationAsync(new SortieEquipement
        {
            SortieId = sortieId,
            EquipementId = equipement.Id,
            ScanneDepartLe = horloge.MaintenantUtc,
            Statut = StatutSortieEquipement.Dehors,
        }, ct);
        await JournaliserAsync(ActionsJournal.ScanDepart, $"{equipement.Code} part avec la sortie {sortie.Id} « {sortie.Intitule} »", ct, equipement.Id, sortie.Id);

        // Règle : révision dépassée → part quand même, avec un avertissement visible.
        if (equipement.RevisionDepassee(horloge.AujourdHui))
        {
            return Avec(ResultatScan.Avertissement(
                $"{equipement.Designation} ajouté. Attention : révision dépassée depuis le {Libelles.Date(equipement.EcheanceDepassee(horloge.AujourdHui)!.Value)}.",
                equipement));
        }

        if (seuilAtteint)
        {
            return Avec(ResultatScan.Avertissement(
                $"{equipement.Designation} ajouté. Attention : révision à faire ({usage!.Utilisations} utilisations, seuil {usage.Seuil}).",
                equipement));
        }

        return Avec(ResultatScan.Succes($"{equipement.Designation} ajouté.", equipement));
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

        // Règle : dans une sortie clôturée, rendre un manquant demande le mode gestion.
        if (sortie.Statut == StatutSortie.Cloturee && gestion?.Exiger(ActionProtegee.RendreManquant) is { Reussi: false } refus)
        {
            return ResultatScan.Erreur($"Sortie clôturée : {refus.Message}");
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
                    await JournaliserAsync(ActionsJournal.ScanRetour, $"{equipement.Code} rendu (sortie {sortieId})", ct, equipement.Id, sortieId);
                    return ResultatScan.Succes($"{equipement.Designation} rendu.", equipement);

                default: // Manquant dans cette sortie (déjà clôturée) → retrouvé.
                    await MarquerRenduAsync(affectation, ct);
                    await JournaliserAsync(ActionsJournal.ManquantRetrouve, $"{equipement.Code} retrouvé et rendu (sortie {sortieId})", ct, equipement.Id, sortieId);
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
            await JournaliserAsync(ActionsJournal.ManquantRetrouve, $"{equipement.Code} retrouvé et rendu (sortie {manquant.SortieId})", ct, equipement.Id, manquant.SortieId);
            return ResultatScan.Succes($"{equipement.Designation} était manquant. Il est maintenant rendu.", equipement);
        }

        return ResultatScan.Avertissement($"{equipement.Code} ne fait pas partie de cette sortie. Rien n'a été modifié.", equipement);
    }

    /// <summary>
    /// Retire un équipement scanné par erreur, pendant la préparation. Sortie nominative : un équipement qui attend
    /// encore son adhérent peut aussi être retiré après la validation (le scan est simplement annulé).
    /// </summary>
    public async Task<ResultatOperation> RetirerDuDepartAsync(long sortieId, long equipementId, CancellationToken ct = default)
    {
        var sortie = await sorties.ObtenirAsync(sortieId, ct);
        if (sortie is null)
        {
            return ResultatOperation.Echec("Sortie introuvable.");
        }

        var affectation = await sorties.ObtenirAffectationAsync(sortieId, equipementId, ct);
        var scanEnAttente = sortie.Nominatif && sortie.Statut == StatutSortie.EnCours
            && affectation is { Statut: StatutSortieEquipement.Dehors, AdherentId: null };
        if (sortie.Statut != StatutSortie.EnPreparation && !scanEnAttente)
        {
            return ResultatOperation.Echec("Le départ est validé. On ne peut plus retirer de matériel.");
        }

        if (affectation is null)
        {
            return ResultatOperation.Echec("Cet équipement n'est pas dans la sortie.");
        }

        await sorties.SupprimerAffectationAsync(affectation.Id, ct);
        var equipement = await equipements.ObtenirAsync(equipementId, ct);
        var code = equipement?.Code ?? equipementId.ToString(Libelles.Culture);
        await JournaliserAsync(ActionsJournal.RetraitDepart, $"{code} retiré de la sortie {sortieId}", ct, equipementId, sortieId);
        return ResultatOperation.Ok($"{code} retiré.");
    }

    /// <summary>
    /// Sortie clôturée, bouton « Marquer rendu » : le responsable note qu'un manquant est revenu sans le scanner
    /// (étiquette abîmée, rendu en main propre…). Mode gestion seulement.
    /// </summary>
    public async Task<ResultatOperation> RendreManquantAsync(long sortieId, long equipementId, CancellationToken ct = default)
    {
        if (gestion?.Exiger(ActionProtegee.RendreManquant) is { Reussi: false } refus)
        {
            return refus;
        }

        var affectation = await sorties.ObtenirAffectationAsync(sortieId, equipementId, ct);
        if (affectation is not { Statut: StatutSortieEquipement.Manquant })
        {
            return ResultatOperation.Echec("Cet équipement n'est pas manquant dans cette sortie.");
        }

        await MarquerRenduAsync(affectation, ct);
        var equipement = await equipements.ObtenirAsync(equipementId, ct);
        var code = equipement?.Code ?? equipementId.ToString(Libelles.Culture);
        await JournaliserAsync(ActionsJournal.ManquantRetrouve, $"{code} retrouvé et marqué rendu (sortie {sortieId})", ct, equipementId, sortieId);
        return ResultatOperation.Ok($"{code} est noté rendu.", equipementId);
    }

    /// <summary>
    /// Un équipement scanné au départ n'est pas perdu : chaque sortie où il était « Manquant » le note rendu (retrouvé),
    /// avec l'entrée « Retrouvé » du journal. Retourne le message à afficher, ou null s'il n'était manquant nulle part.
    /// </summary>
    private async Task<string?> RetrouverManquantsAsync(Equipement equipement, Sortie depart, CancellationToken ct)
    {
        var sortiesRetrouvees = new List<string>();
        while (await sorties.ObtenirDerniereAffectationManquanteAsync(equipement.Id, ct) is { } manquant)
        {
            await MarquerRenduAsync(manquant, ct);
            var ancienne = await sorties.ObtenirAsync(manquant.SortieId, ct);
            var nom = ancienne is null ? $"n° {manquant.SortieId}" : $"« {ancienne.Intitule} » du {ancienne.DateSortie.ToString("dd/MM", Libelles.Culture)}";
            sortiesRetrouvees.Add(nom);
            await JournaliserAsync(ActionsJournal.ManquantRetrouve,
                $"{equipement.Code} retrouvé au départ de la sortie {depart.Id} (était manquant dans la sortie {manquant.SortieId})", ct, equipement.Id, manquant.SortieId);
        }

        return sortiesRetrouvees.Count == 0
            ? null
            : $"{equipement.Code} était manquant (sortie {string.Join(", ", sortiesRetrouvees)}) : noté retrouvé.";
    }

    private Task MarquerRenduAsync(SortieEquipement affectation, CancellationToken ct)
    {
        affectation.Statut = StatutSortieEquipement.Rendu;
        affectation.ScanneRetourLe = horloge.MaintenantUtc;
        return sorties.MettreAJourAffectationAsync(affectation, ct);
    }

    private Task JournaliserAsync(string action, string details, CancellationToken ct, long? equipementId = null, long? sortieId = null) =>
        journal.AjouterAsync(new EntreeJournal { Horodatage = horloge.MaintenantUtc, Action = action, Details = details, EquipementId = equipementId, SortieId = sortieId }, ct);
}
