using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Saisie;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>
/// Révisions : échéancier des contrôles (TIV, requalification, révision), enregistrement d'un contrôle,
/// passages en atelier et séances TIV. Délais pris dans <see cref="Reglages"/>.
/// </summary>
public sealed class ServiceRevisions(
    IEquipementRepository equipements,
    ICategorieRepository categories,
    ISortieRepository sorties,
    IRevisionRepository revisions,
    IMoniteurRepository moniteurs,
    IJournalRepository journal,
    IHorloge horloge,
    Reglages reglages)
{
    /// <summary>Une séance TIV prépare les blocs dont la TIV tombe au plus tard ce nombre de jours après elle.</summary>
    public const int JoursPreparationSeance = 30;

    /// <summary>Les blocs (catégorie « Blocs ») ont TIV et requalification ; le reste a une révision.</summary>
    public static bool EstBloc(string? categorie) =>
        categorie?.Trim().StartsWith("Bloc", StringComparison.OrdinalIgnoreCase) == true;

    public static IReadOnlyList<TypeControle> TypesPour(string? categorie) =>
        EstBloc(categorie) ? [TypeControle.Tiv, TypeControle.Requalification] : [TypeControle.Revision];

    /// <summary>Nom du contrôle affiché : « TIV annuelle », « Révision fabricant »...</summary>
    public static string LibelleControle(TypeControle type, string? categorie)
    {
        if (type == TypeControle.Tiv)
        {
            return "TIV annuelle";
        }

        if (type == TypeControle.Requalification)
        {
            return "Requalification";
        }

        var c = (categorie ?? "").Trim();
        return c.StartsWith("Détendeur", StringComparison.OrdinalIgnoreCase) || c.StartsWith("Detendeur", StringComparison.OrdinalIgnoreCase)
            ? "Révision fabricant"
            : c.StartsWith("Gilet", StringComparison.OrdinalIgnoreCase)
                ? "Contrôle annuel"
                : c.StartsWith("Ordinateur", StringComparison.OrdinalIgnoreCase) ? "Pile et étanchéité" : "Révision";
    }

    /// <summary>En retard, ce mois-ci, à planifier (sous <see cref="Reglages.JoursAPlanifier"/> jours) ou à jour.</summary>
    public EtatEcheance EtatDe(DateOnly echeance)
    {
        var aujourdHui = horloge.AujourdHui;
        if (echeance < aujourdHui)
        {
            return EtatEcheance.EnRetard;
        }

        var finDuMois = new DateOnly(aujourdHui.Year, aujourdHui.Month, DateTime.DaysInMonth(aujourdHui.Year, aujourdHui.Month));
        if (echeance <= finDuMois)
        {
            return EtatEcheance.CeMois;
        }

        return echeance <= aujourdHui.AddDays(reglages.JoursAPlanifier) ? EtatEcheance.Bientot : EtatEcheance.AJour;
    }

    /// <summary>Tous les contrôles à venir (matériel réformé exclu), du plus urgent au moins urgent.</summary>
    public async Task<IReadOnlyList<LigneEcheance>> ListerEcheancesAsync(CancellationToken ct = default)
    {
        var inventaire = await equipements.ListerInventaireAsync(null, null, ct);
        var derniers = (await revisions.ListerDerniersControlesAsync(ct))
            .GroupBy(d => (d.EquipementId, d.Type))
            .ToDictionary(g => g.Key, g => g.Max(d => d.DateControle));

        var lignes = new List<LigneEcheance>();
        foreach (var e in inventaire.Where(e => e.Etat != EtatEquipement.Reforme))
        {
            void Ajouter(TypeControle type, DateOnly? echeance)
            {
                if (echeance is { } date)
                {
                    lignes.Add(new LigneEcheance(
                        e.EquipementId, e.Code, e.Libelle, e.Categorie, type, LibelleControle(type, e.Categorie),
                        derniers.TryGetValue((e.EquipementId, type), out var d) ? d : null, date, EtatDe(date)));
                }
            }

            if (EstBloc(e.Categorie))
            {
                Ajouter(TypeControle.Tiv, e.DateProchaineRevision);
                Ajouter(TypeControle.Requalification, e.DateRequalification);
            }
            else
            {
                Ajouter(TypeControle.Revision, e.DateProchaineRevision);
            }
        }

        return lignes.OrderBy(l => l.Echeance).ThenBy(l => l.Code, StringComparer.Ordinal).ToList();
    }

    /// <summary>Équipement scanné pour un contrôle : contrôle proposé (le plus urgent) et derniers contrôles.</summary>
    public async Task<PreparationControle?> PreparerControleAsync(string codeBrut, CancellationToken ct = default)
    {
        var code = CodeScanne.Nettoyer(codeBrut, reglages.PrefixeEtiquette);
        if (code.Length == 0 || await equipements.ObtenirParCodeAsync(code, ct) is not { } equipement)
        {
            return null;
        }

        var categorie = (await categories.ListerAsync(ct)).FirstOrDefault(c => c.Id == equipement.CategorieId)?.Nom ?? "";
        var controles = await revisions.ListerControlesAsync(equipement.Id, ct);
        DateOnly? Dernier(TypeControle t) => controles.Where(c => c.Type == t).Select(c => (DateOnly?)c.DateControle).Max();

        var propose = TypeControle.Revision;
        if (EstBloc(categorie))
        {
            var tiv = equipement.DateProchaineRevision ?? DateOnly.MaxValue;
            var requalif = equipement.DateRequalification ?? DateOnly.MaxValue;
            propose = requalif < tiv ? TypeControle.Requalification : TypeControle.Tiv;
        }

        return new PreparationControle(equipement, categorie, propose, TypesPour(categorie),
            Dernier(TypeControle.Tiv), Dernier(TypeControle.Requalification), Dernier(TypeControle.Revision));
    }

    /// <summary>Ce que le résultat va provoquer, affiché avant de valider.</summary>
    public string Consequence(TypeControle type, ResultatControle resultat, DateOnly date)
    {
        var (revision, requalif) = NouvellesEcheances(type, resultat, date);
        var nom = type switch
        {
            TypeControle.Tiv => "TIV",
            TypeControle.Requalification => "requalification",
            _ => "révision",
        };
        return resultat switch
        {
            ResultatControle.Conforme when type == TypeControle.Requalification =>
                $"Prochaine requalification : {Mois(requalif!.Value)}. Prochaine TIV : {Mois(revision!.Value)}.",
            ResultatControle.Conforme => $"Prochaine {nom} calculée : {Mois(revision!.Value)}.",
            ResultatControle.ASurveiller => $"L'équipement reste utilisable. Contrôle anticipé en {Mois(revision!.Value)}.",
            _ => "L'équipement passe hors service et ne pourra plus sortir.",
        };
    }

    /// <summary>
    /// Enregistre un contrôle. Conforme : nouvelles échéances selon les délais ; à surveiller : contrôle anticipé ;
    /// refusé : l'équipement passe hors service.
    /// </summary>
    public async Task<ResultatOperation> EnregistrerControleAsync(
        long equipementId, TypeControle type, ResultatControle resultat, DateOnly date, long? inspecteurId, CancellationToken ct = default)
    {
        if (await equipements.ObtenirAsync(equipementId, ct) is not { } equipement)
        {
            return ResultatOperation.Echec("Équipement introuvable.");
        }

        if (equipement.Etat == EtatEquipement.Reforme)
        {
            return ResultatOperation.Echec($"{equipement.Code} est réformé : plus de contrôle à faire.");
        }

        if (date > horloge.AujourdHui)
        {
            return ResultatOperation.Echec("La date du contrôle ne peut pas être dans le futur.");
        }

        var categorie = (await categories.ListerAsync(ct)).FirstOrDefault(c => c.Id == equipement.CategorieId)?.Nom;
        if (!TypesPour(categorie).Contains(type))
        {
            return ResultatOperation.Echec(type == TypeControle.Revision
                ? "Pour un bloc, choisissez TIV ou requalification."
                : "TIV et requalification concernent seulement les blocs.");
        }

        if (type == TypeControle.Tiv)
        {
            var inspecteur = inspecteurId is { } id ? await moniteurs.ObtenirAsync(id, ct) : null;
            if (inspecteur is not { InspecteurTiv: true })
            {
                return ResultatOperation.Echec("Choisissez l'inspecteur TIV qui a fait le contrôle.");
            }
        }

        if (resultat == ResultatControle.Refuse && await sorties.ObtenirAffectationDehorsAsync(equipement.Id, ct) is not null)
        {
            return ResultatOperation.Echec($"{equipement.Code} est dehors dans une sortie. Scannez son retour avant de le déclarer refusé.");
        }

        await revisions.AjouterControleAsync(new Controle
        {
            EquipementId = equipement.Id,
            Type = type,
            DateControle = date,
            Resultat = resultat,
            InspecteurId = type == TypeControle.Tiv ? inspecteurId : null,
            SaisiLe = horloge.MaintenantUtc,
        }, ct);

        if (resultat == ResultatControle.Refuse)
        {
            equipement.Etat = EtatEquipement.HorsService;
        }
        else
        {
            var (revision, requalif) = NouvellesEcheances(type, resultat, date);
            equipement.DateProchaineRevision = revision;
            if (requalif is not null)
            {
                equipement.DateRequalification = requalif;
            }
        }

        await equipements.MettreAJourAsync(equipement, ct);
        await JournaliserAsync(ActionsJournal.ControleEnregistre,
            $"{equipement.Code} : {LibelleControle(type, categorie)} du {Libelles.Date(date)}, {LibelleResultat(resultat)}", ct);

        return ResultatOperation.Ok(resultat switch
        {
            ResultatControle.Refuse => $"{equipement.Designation} refusé : il passe hors service.",
            _ => $"{equipement.Designation} : contrôle enregistré. {Consequence(type, resultat, date)}",
        }, equipement.Id);
    }

    public static string LibelleResultat(ResultatControle resultat) => resultat switch
    {
        ResultatControle.Conforme => "Conforme",
        ResultatControle.ASurveiller => "À surveiller",
        _ => "Refusé",
    };

    // ---------------------------------------------------------------- Atelier

    public Task<IReadOnlyList<LigneAtelier>> ListerAtelierAsync(CancellationToken ct = default) =>
        revisions.ListerAtelierEnCoursAsync(ct);

    /// <summary>Envoie un équipement en atelier : il passe hors service jusqu'à son retour.</summary>
    public async Task<ResultatOperation> EnvoyerEnAtelierAsync(
        string codeBrut, string prestataire, string motif, DateOnly? retourPrevu, CancellationToken ct = default)
    {
        var code = CodeScanne.Nettoyer(codeBrut, reglages.PrefixeEtiquette);
        if (code.Length == 0)
        {
            return ResultatOperation.Echec("Scannez ou tapez le code de l'équipement.");
        }

        if (await equipements.ObtenirParCodeAsync(code, ct) is not { } equipement)
        {
            return ResultatOperation.Echec($"Code inconnu : {code}.");
        }

        if (string.IsNullOrWhiteSpace(prestataire))
        {
            return ResultatOperation.Echec("Indiquez l'atelier ou le magasin.");
        }

        if (equipement.Etat == EtatEquipement.Reforme)
        {
            return ResultatOperation.Echec($"{equipement.Code} est réformé.");
        }

        if (await sorties.ObtenirAffectationDehorsAsync(equipement.Id, ct) is not null)
        {
            return ResultatOperation.Echec($"{equipement.Code} est dehors dans une sortie. Scannez son retour d'abord.");
        }

        if (await revisions.ObtenirPassageEnCoursAsync(equipement.Id, ct) is not null)
        {
            return ResultatOperation.Echec($"{equipement.Code} est déjà en atelier.");
        }

        var aujourdHui = horloge.AujourdHui;
        if (retourPrevu is { } r && r < aujourdHui)
        {
            return ResultatOperation.Echec("La date de retour prévue est déjà passée.");
        }

        var id = await revisions.AjouterPassageAtelierAsync(new PassageAtelier
        {
            EquipementId = equipement.Id,
            Prestataire = prestataire.Trim(),
            Motif = (motif ?? "").Trim(),
            EnvoyeLe = aujourdHui,
            RetourPrevu = retourPrevu,
        }, ct);
        equipement.Etat = EtatEquipement.HorsService;
        await equipements.MettreAJourAsync(equipement, ct);
        await JournaliserAsync(ActionsJournal.EnvoiAtelier, $"{equipement.Code} envoyé chez {prestataire.Trim()}", ct);
        return ResultatOperation.Ok($"{equipement.Designation} envoyé en atelier. Il est hors service jusqu'à son retour.", id);
    }

    /// <summary>Retour d'atelier : l'équipement repasse en service.</summary>
    public async Task<ResultatOperation> RetourAtelierAsync(long passageId, CancellationToken ct = default)
    {
        if (await revisions.ObtenirPassageAsync(passageId, ct) is not { RevenuLe: null } passage)
        {
            return ResultatOperation.Echec("Ce passage en atelier est déjà terminé.");
        }

        passage.RevenuLe = horloge.AujourdHui;
        await revisions.MettreAJourPassageAsync(passage, ct);
        var equipement = await equipements.ObtenirAsync(passage.EquipementId, ct);
        if (equipement is { Etat: EtatEquipement.HorsService })
        {
            equipement.Etat = EtatEquipement.EnService;
            await equipements.MettreAJourAsync(equipement, ct);
        }

        var code = equipement?.Code ?? $"Équipement {passage.EquipementId}";
        await JournaliserAsync(ActionsJournal.RetourAtelier, $"{code} revenu de chez {passage.Prestataire}", ct);
        return ResultatOperation.Ok($"{code} revenu d'atelier : de nouveau en service.", passage.EquipementId);
    }

    // ---------------------------------------------------------------- Séances TIV

    public async Task<ResultatOperation> PlanifierSeanceAsync(DateOnly date, string? heure, string? lieu, CancellationToken ct = default)
    {
        if (date < horloge.AujourdHui)
        {
            return ResultatOperation.Echec("Choisissez une date à venir.");
        }

        var id = await revisions.AjouterSeanceAsync(new SeanceTiv { DateSeance = date, Heure = (heure ?? "").Trim(), Lieu = (lieu ?? "").Trim() }, ct);
        await JournaliserAsync(ActionsJournal.SeanceTivPlanifiee, $"Séance TIV le {Libelles.Date(date)}", ct);
        return ResultatOperation.Ok($"Séance TIV planifiée le {Libelles.DateLongue(date)}.", id);
    }

    /// <summary>Prochaine séance, ses inspecteurs, et les blocs dont la TIV tombe avant (ou peu après) elle.</summary>
    public async Task<ProchaineSeance?> ProchaineSeanceAsync(CancellationToken ct = default)
    {
        if (await revisions.ProchaineSeanceAsync(horloge.AujourdHui, ct) is not { } seance)
        {
            return null;
        }

        var inspecteurs = (await moniteurs.ListerAsync(actifsSeulement: true, ct))
            .Where(m => m.InspecteurTiv)
            .Select(m => m.NomComplet)
            .ToList();
        var limite = seance.DateSeance.AddDays(JoursPreparationSeance);
        var blocs = (await ListerEcheancesAsync(ct))
            .Where(l => l.Type == TypeControle.Tiv && l.Echeance <= limite)
            .ToList();
        return new ProchaineSeance(seance, inspecteurs, blocs);
    }

    private (DateOnly? Revision, DateOnly? Requalification) NouvellesEcheances(TypeControle type, ResultatControle resultat, DateOnly date)
    {
        var mois = resultat == ResultatControle.ASurveiller
            ? reglages.MoisSiASurveiller
            : type == TypeControle.Revision ? reglages.MoisEntreRevisions : reglages.MoisEntreTiv;
        var revision = date.AddMonths(mois);

        // Une requalification vaut aussi inspection : la TIV repart de cette date.
        DateOnly? requalif = type == TypeControle.Requalification ? date.AddYears(reglages.AnneesEntreRequalifications) : null;
        return resultat == ResultatControle.Refuse ? (null, null) : (revision, requalif);
    }

    private static string Mois(DateOnly date) => date.ToString("MM/yyyy", Libelles.Culture);

    private Task JournaliserAsync(string action, string details, CancellationToken ct) =>
        journal.AjouterAsync(new EntreeJournal { Horodatage = horloge.MaintenantUtc, Action = action, Details = details }, ct);
}
