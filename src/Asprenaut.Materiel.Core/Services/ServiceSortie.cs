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
    string Notes = "",
    long? TypeId = null,
    int? Utilisations = null);

/// <summary>Cycle de vie d'une sortie : création, validation du départ, clôture.</summary>
public sealed class ServiceSortie(
    ISortieRepository sorties,
    IMoniteurRepository moniteurs,
    IJournalRepository journal,
    IHorloge horloge,
    Reglages reglages,
    ServiceGestion? gestion = null,
    ITypeSortieRepository? types = null,
    IAdherentRepository? adherents = null)
{
    /// <summary>
    /// Type de la sortie : celui choisi (<see cref="NouvelleSortie.TypeId"/>), qui doit être proposé, ou rester celui
    /// de la sortie modifiée s'il a été masqué depuis ; sans choix, le premier type proposé de la famille demandée.
    /// Retourne (famille, id du type) ou une erreur.
    /// </summary>
    private async Task<(TypeSortie Famille, long? TypeId, int UtilisationsDuType, bool Nominatif, ResultatOperation? Erreur)> TypeAsync(NouvelleSortie demande, long? typeActuel, CancellationToken ct)
    {
        if (demande.Utilisations is < TypeDeSortie.UtilisationsMin or > TypeDeSortie.UtilisationsMax)
        {
            return (demande.Type, null, 1, false, ResultatOperation.Echec(
                $"Utilisations : entre {TypeDeSortie.UtilisationsMin} et {TypeDeSortie.UtilisationsMax} par sortie."));
        }

        if (types is null)
        {
            return (demande.Type, demande.TypeId, TypeDeSortie.UtilisationsParDefaut(demande.Type), false, null);
        }

        var tous = await types.ListerAsync(ct);
        var type = demande.TypeId is { } id
            ? tous.FirstOrDefault(t => t.Id == id && (t.Actif || t.Id == typeActuel))
            : tous.FirstOrDefault(t => t.Actif && t.Pictogramme == demande.Type);
        return type is null
            ? (demande.Type, null, 1, false, ResultatOperation.Echec("Choisissez un type de sortie."))
            : (type.Pictogramme, type.Id, type.Utilisations, type.Nominatif, null);
    }

    /// <summary>
    /// Sortie en cours dont du matériel est encore dehors plus de <see cref="Reglages.JoursAvantAlerteRetour"/> jours
    /// après la validation du départ : elle est signalée en orange dans la liste.
    /// </summary>
    public bool EstEnRetard(SortieResume s) =>
        s.Statut == StatutSortie.EnCours
        && s.NbDehors > 0
        && s.DepartValideLe is { } depart
        && horloge.MaintenantUtc - depart >= TimeSpan.FromDays(reglages.JoursAvantAlerteRetour);

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
            return ResultatOperation.Echec("Choisissez le responsable : un moniteur ou une classe.");
        }

        var moniteur = await moniteurs.ObtenirAsync(moniteurId, ct);
        if (moniteur is null || !moniteur.Actif)
        {
            return ResultatOperation.Echec(moniteur?.EstClasse == true ? "Cette classe n'est pas disponible." : "Ce moniteur n'est pas disponible.");
        }

        var (famille, typeId, utilisationsDuType, nominatif, erreurType) = await TypeAsync(demande, null, ct);
        if (erreurType is not null)
        {
            return erreurType;
        }

        // Changer les utilisations du type (compteur) demande le mode gestion.
        var utilisations = demande.Utilisations ?? utilisationsDuType;
        if (utilisations != utilisationsDuType && gestion?.Exiger(ActionProtegee.CompteurSortie) is { Reussi: false } refusCompteur)
        {
            return refusCompteur;
        }

        var sortie = new Sortie
        {
            DateSortie = demande.Date,
            Type = famille,
            TypeId = typeId,
            Utilisations = utilisations,
            Intitule = intitule,
            MoniteurId = moniteurId,
            Lieu = demande.Lieu.Trim(),
            Notes = demande.Notes.Trim(),
            Statut = StatutSortie.EnPreparation,
            Nominatif = nominatif,
        };
        var id = await sorties.AjouterAsync(sortie, ct);
        await JournaliserAsync(ActionsJournal.SortieCreee, $"Sortie {id} « {intitule} » du {Libelles.Date(demande.Date)} ({moniteur.NomComplet})", ct, sortieId: id);
        return ResultatOperation.Ok("Sortie créée.", id);
    }

    public Task<Sortie?> ObtenirAsync(long sortieId, CancellationToken ct = default) =>
        sorties.ObtenirAsync(sortieId, ct);

    /// <summary>
    /// Modifie date, type, intitulé, moniteur, lieu et notes, tant que le départ n'est pas validé.
    /// Mêmes contrôles qu'à la création ; le moniteur déjà choisi reste accepté même s'il est devenu inactif.
    /// </summary>
    public async Task<ResultatOperation> ModifierAsync(long sortieId, NouvelleSortie demande, CancellationToken ct = default)
    {
        if (await sorties.ObtenirAsync(sortieId, ct) is not { } sortie)
        {
            return ResultatOperation.Echec("Sortie introuvable.");
        }

        if (sortie.Statut != StatutSortie.EnPreparation)
        {
            return ResultatOperation.Echec("Le départ est validé : la sortie ne peut plus être modifiée.");
        }

        var intitule = demande.Intitule.Trim();
        if (intitule.Length == 0)
        {
            return ResultatOperation.Echec("Indiquez un intitulé.");
        }

        if (demande.MoniteurId is not { } moniteurId)
        {
            return ResultatOperation.Echec("Choisissez le responsable : un moniteur ou une classe.");
        }

        var moniteur = await moniteurs.ObtenirAsync(moniteurId, ct);
        if (moniteur is null || (!moniteur.Actif && moniteurId != sortie.MoniteurId))
        {
            return ResultatOperation.Echec(moniteur?.EstClasse == true ? "Cette classe n'est pas disponible." : "Ce moniteur n'est pas disponible.");
        }

        var (famille, typeId, utilisationsDuType, nominatif, erreurType) = await TypeAsync(demande, sortie.TypeId, ct);
        if (erreurType is not null)
        {
            return erreurType;
        }

        // Sans valeur donnée : la sortie garde la sienne, ou prend celle du nouveau type si le type change.
        // Une autre valeur (compteur changé à la main) demande le mode gestion.
        var attendues = typeId != sortie.TypeId ? utilisationsDuType : sortie.Utilisations;
        var utilisations = demande.Utilisations ?? attendues;
        if (utilisations != attendues && gestion?.Exiger(ActionProtegee.CompteurSortie) is { Reussi: false } refusCompteur)
        {
            return refusCompteur;
        }

        // Nominative : reprise du nouveau type s'il change ; sinon la sortie garde la sienne (réglage du type changé depuis).
        if (typeId != sortie.TypeId)
        {
            sortie.Nominatif = nominatif;
        }

        sortie.Utilisations = utilisations;
        sortie.DateSortie = demande.Date;
        sortie.Type = famille;
        sortie.TypeId = typeId;
        sortie.Intitule = intitule;
        sortie.MoniteurId = moniteurId;
        sortie.Lieu = demande.Lieu.Trim();
        sortie.Notes = demande.Notes.Trim();
        await sorties.MettreAJourAsync(sortie, ct);
        await JournaliserAsync(ActionsJournal.SortieModifiee, $"Sortie {sortieId} « {intitule} » du {Libelles.Date(demande.Date)} modifiée ({moniteur.NomComplet})", ct, sortieId: sortieId);
        return ResultatOperation.Ok("Sortie modifiée.", sortieId);
    }

    /// <summary>Supprime une sortie créée par erreur : seulement si aucun matériel n'y a été scanné.</summary>
    public async Task<ResultatOperation> SupprimerAsync(long sortieId, CancellationToken ct = default)
    {
        if (gestion?.Exiger(ActionProtegee.SupprimerSortie) is { Reussi: false } refus)
        {
            return refus;
        }

        if (await sorties.ObtenirAsync(sortieId, ct) is not { } sortie)
        {
            return ResultatOperation.Echec("Sortie introuvable.");
        }

        if ((await sorties.ListerAffectationsAsync(sortieId, ct)).Count > 0)
        {
            return ResultatOperation.Echec("Cette sortie contient du matériel. Retirez-le d'abord pour pouvoir la supprimer.");
        }

        if (!await sorties.SupprimerAsync(sortieId, ct))
        {
            return ResultatOperation.Echec("La sortie n'a pas pu être supprimée : du matériel vient d'y être scanné.");
        }

        var moniteur = await moniteurs.ObtenirAsync(sortie.MoniteurId, ct);
        await JournaliserAsync(ActionsJournal.SortieSupprimee,
            $"Sortie « {sortie.Intitule} » du {Libelles.Date(sortie.DateSortie)} supprimée{(moniteur is null ? "" : $" ({moniteur.NomComplet})")}", ct, sortieId: sortieId);
        return ResultatOperation.Ok($"Sortie « {sortie.Intitule} » supprimée.", sortieId);
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

        // Sortie nominative : chaque équipement doit avoir son adhérent.
        if (sortie.Nominatif && AAttribuer(true, await sorties.ListerLignesAsync(sortieId, ct)) is { } enAttente)
        {
            return ResultatOperation.Echec($"Attribuez {enAttente.Code} à un adhérent avant de valider le départ.");
        }

        sortie.Statut = StatutSortie.EnCours;
        sortie.DepartValideLe = horloge.MaintenantUtc;
        await sorties.MettreAJourAsync(sortie, ct);
        await JournaliserAsync(ActionsJournal.DepartValide, $"Départ validé pour la sortie {sortieId} ({Libelles.Pluriel(affectations.Count, "équipement")})", ct, sortieId: sortieId);
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
        // Confirmation seulement s'il reste du matériel dehors, et si le club ne l'a pas désactivée.
        return new ApercuCloture(nb, message, reglages.ConfirmerCloture && nb > 0);
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

        // Réglage « Code pour clôturer avec des manquants » : laisser du matériel dehors demande le mode gestion.
        if (manquants.Count > 0 && gestion?.Exiger(ActionProtegee.CloturerAvecManquants) is { Reussi: false } refus)
        {
            return refus;
        }

        foreach (var a in manquants)
        {
            a.Statut = StatutSortieEquipement.Manquant;
        }

        sortie.Statut = StatutSortie.Cloturee;
        sortie.ClotureeLe = horloge.MaintenantUtc;
        await sorties.EnregistrerClotureAsync(sortie, manquants, ct);

        if (manquants.Count > 0)
        {
            // Sortie nominative : l'entrée du journal garde l'adhérent qui avait l'équipement (« pris par Julie Martin »).
            var lignes = (await sorties.ListerLignesAsync(sortieId, ct)).ToDictionary(l => l.EquipementId);
            foreach (var a in manquants)
            {
                var ligne = lignes.GetValueOrDefault(a.EquipementId);
                var code = ligne?.Code ?? $"Équipement {a.EquipementId}";
                var pris = ligne?.AdherentNom is { Length: > 0 } nom ? $", pris par {nom}" : "";
                await journal.AjouterAsync(new EntreeJournal
                {
                    Horodatage = horloge.MaintenantUtc,
                    Action = ActionsJournal.ManquantDeclare,
                    Details = $"{code} manquant (sortie {sortieId}){pris}",
                    EquipementId = a.EquipementId,
                    SortieId = sortieId,
                    AdherentId = a.AdherentId,
                }, ct);
            }
        }

        await JournaliserAsync(ActionsJournal.SortieCloturee, $"Sortie {sortieId} clôturée, {Libelles.Pluriel(manquants.Count, "manquant")}", ct, sortieId: sortieId);

        return manquants.Count == 0
            ? ResultatOperation.Ok("Sortie clôturée. Tout est rendu.", sortieId)
            : ResultatOperation.Ok($"Sortie clôturée avec {Libelles.Pluriel(manquants.Count, "manquant")}.", sortieId);
    }

    // ---------------------------------------------------------------- Sorties nominatives

    /// <summary>
    /// Équipement d'une sortie nominative qui attend son adhérent (le dernier scanné d'abord), ou null.
    /// Tant qu'il y en a un, le scan suivant et la validation du départ sont refusés.
    /// </summary>
    public static LigneSortie? AAttribuer(bool nominatif, IEnumerable<LigneSortie> lignes) =>
        nominatif
            ? lignes.Where(l => l.Statut == StatutSortieEquipement.Dehors && l.AdherentId is null)
                .OrderByDescending(l => l.ScanneDepartLe).ThenByDescending(l => l.LigneId).FirstOrDefault()
            : null;

    /// <summary>
    /// Raccourcis du départ : les derniers adhérents choisis dans cette sortie (le plus récent d'abord), avec le nombre
    /// d'équipements de chacun. Un plongeur prend souvent bloc, gilet et détendeur à la suite.
    /// </summary>
    public static IReadOnlyList<AdherentRecent> AdherentsRecents(IEnumerable<LigneSortie> lignes, int nombre = 3) =>
        lignes.Where(l => l.AdherentId is not null)
            .GroupBy(l => l.AdherentId!.Value)
            .Select(g => (Recent: new AdherentRecent(g.Key, g.First().AdherentNom ?? "", g.Count()),
                Dernier: g.Max(l => (l.ScanneDepartLe, l.LigneId))))
            .OrderByDescending(x => x.Dernier)
            .Take(nombre)
            .Select(x => x.Recent)
            .ToList();

    /// <summary>
    /// Sortie nominative : l'équipement scanné est pris par cet adhérent (actif). Possible tant qu'il est dehors,
    /// pour corriger aussi une attribution.
    /// </summary>
    public async Task<ResultatOperation> AttribuerAdherentAsync(long sortieId, long equipementId, long adherentId, CancellationToken ct = default)
    {
        if (await sorties.ObtenirAsync(sortieId, ct) is not { } sortie)
        {
            return ResultatOperation.Echec("Sortie introuvable.");
        }

        if (!sortie.Nominatif)
        {
            return ResultatOperation.Echec("Cette sortie ne demande pas le nom des adhérents.");
        }

        if (sortie.Statut == StatutSortie.Cloturee)
        {
            return ResultatOperation.Echec("Cette sortie est clôturée.");
        }

        var affectation = await sorties.ObtenirAffectationAsync(sortieId, equipementId, ct);
        if (affectation is null || affectation.Statut != StatutSortieEquipement.Dehors)
        {
            return ResultatOperation.Echec("Cet équipement n'est plus dans la sortie.");
        }

        var adherent = adherents is null ? null : await adherents.ObtenirAsync(adherentId, ct);
        if (adherent is null)
        {
            return ResultatOperation.Echec("Cet adhérent n'existe plus.");
        }

        // Règles : un adhérent désactivé ne prend pas de matériel ; un bloc ou un détendeur nitrox ne part qu'avec un certifié nitrox.
        var ligne = (await sorties.ListerLignesAsync(sortieId, ct)).FirstOrDefault(l => l.EquipementId == equipementId);
        var code = ligne?.Code ?? "";
        if (ServiceAdherents.RefusPour(adherent, ligne?.Nitrox ?? false, code) is { } refus)
        {
            return ResultatOperation.Echec(refus);
        }

        await sorties.AttribuerAdherentAsync(affectation.Id, adherent.Id, ct);
        affectation.AdherentId = adherent.Id;
        return ResultatOperation.Ok($"{code} attribué à {adherent.NomComplet}.", adherent.Id);
    }

    private Task JournaliserAsync(string action, string details, CancellationToken ct, long? equipementId = null, long? sortieId = null) =>
        journal.AjouterAsync(new EntreeJournal { Horodatage = horloge.MaintenantUtc, Action = action, Details = details, EquipementId = equipementId, SortieId = sortieId }, ct);
}
