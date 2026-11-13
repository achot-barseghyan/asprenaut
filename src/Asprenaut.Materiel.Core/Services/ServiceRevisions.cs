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
    Reglages reglages,
    ServiceGestion? gestion = null,
    ServiceCompteur? compteur = null,
    ISignalementRepository? signalements = null,
    IPrestataireRepository? prestataires = null)
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

    /// <summary>
    /// Registre des contrôles à imprimer : tous les blocs (même sans contrôle enregistré, pour que l'inspecteur les voie),
    /// puis le reste du matériel qui a un contrôle ou une révision prévue. Réformés inclus s'ils ont un historique.
    /// </summary>
    public async Task<RegistreControles> RegistreAsync(CancellationToken ct = default)
    {
        var inventaire = await equipements.ListerInventaireAsync(null, null, ct);
        var controles = (await revisions.ListerControlesPeriodeAsync(DateOnly.MinValue, DateOnly.MaxValue, ct)).ToLookup(c => c.EquipementId);
        var noms = (await moniteurs.ListerAsync(actifsSeulement: false, ct)).ToDictionary(m => m.Id, m => m.NomComplet);

        FicheRegistre Fiche(LigneInventaire e) => new(
            e.Code, e.Libelle, e.Categorie, e.Etat, e.DateProchaineRevision, e.DateRequalification,
            controles[e.EquipementId]
                .OrderBy(c => c.DateControle).ThenBy(c => c.Id)
                .Select(c => new LigneRegistre(
                    c.DateControle,
                    LibelleControle(c.Type, e.Categorie),
                    LibelleResultat(c.Resultat),
                    c.Resultat,
                    c.InspecteurId is { } id && noms.TryGetValue(id, out var nom) ? nom : "",
                    c.Notes))
                .ToList());

        bool Suivi(LigneInventaire e) => controles[e.EquipementId].Any() || (e.Etat != EtatEquipement.Reforme && e.DateProchaineRevision is not null);
        var parCode = inventaire.OrderBy(e => e.Code, StringComparer.Ordinal).ToList();
        return new RegistreControles(
            horloge.AujourdHui,
            parCode.Where(e => EstBloc(e.Categorie) && (e.Etat != EtatEquipement.Reforme || controles[e.EquipementId].Any())).Select(Fiche).ToList(),
            parCode.Where(e => !EstBloc(e.Categorie) && Suivi(e)).Select(Fiche).ToList());
    }

    /// <summary>Nom de fichier proposé : registre_controles_2026-11-10.pdf.</summary>
    public string NomFichierRegistre() => $"registre_controles_{horloge.AujourdHui:yyyy-MM-dd}.pdf";

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

        // Compteur d'utilisation : seuil atteint → en retard ; à 90 % → à planifier. Échéance affichée « 92 / 100 sorties ».
        if (compteur is not null)
        {
            var aujourdHui = horloge.AujourdHui;
            foreach (var (id, usage) in await compteur.ListerUsagesAsync(inventaire, ct))
            {
                if (usage.Etat == EtatCompteur.Normal)
                {
                    continue;
                }

                var e = inventaire.First(x => x.EquipementId == id);
                var type = EstBloc(e.Categorie) ? TypeControle.Tiv : TypeControle.Revision; // Un bloc repart à zéro à sa TIV.
                lignes.Add(new LigneEcheance(
                    e.EquipementId, e.Code, e.Libelle, e.Categorie, type, $"{LibelleControle(type, e.Categorie)} · usage",
                    derniers.TryGetValue((e.EquipementId, type), out var d) ? d : null, aujourdHui,
                    usage.Etat == EtatCompteur.Atteint ? EtatEcheance.EnRetard : EtatEcheance.Bientot,
                    usage.Texte));
            }
        }

        return lignes.OrderBy(l => l.Etat).ThenBy(l => l.Echeance).ThenBy(l => l.Code, StringComparer.Ordinal).ToList();
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

    /// <summary>
    /// Ce que le résultat va provoquer, affiché avant de valider. <paramref name="surUnComposant"/> : intervention sur un
    /// composant (octopus...), qui ne change pas les échéances de l'équipement (seul un refus le met hors service).
    /// </summary>
    public string Consequence(TypeControle type, ResultatControle resultat, DateOnly date, string? categorie = null, bool surUnComposant = false)
    {
        if (surUnComposant && resultat != ResultatControle.Refuse)
        {
            return "Notée sur ce composant : les échéances et le compteur de l'équipement ne changent pas.";
        }

        var (revision, requalif) = NouvellesEcheances(type, resultat, date, categorie);
        if (resultat == ResultatControle.Conforme && type == TypeControle.Revision && revision is null)
        {
            return "Pas de date obligatoire pour ce matériel : aucune prochaine révision n'est fixée.";
        }

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
        long equipementId, TypeControle type, ResultatControle resultat, DateOnly date, long? inspecteurId,
        string? nature = null, string? intervenant = null, CancellationToken ct = default) =>
        await EnregistrerControleAsync(equipementId, type, resultat, date, inspecteurId, nature, intervenant, composant: null, notes: null, ct);

    /// <summary>
    /// Enregistre un contrôle, visant éventuellement un composant de l'équipement (fiche de suivi : premier étage, octopus...)
    /// avec des détails. Un composant qui n'existe pas sur l'équipement est refusé ; « Équipement complet » = aucun composant.
    /// </summary>
    public async Task<ResultatOperation> EnregistrerControleAsync(
        long equipementId, TypeControle type, ResultatControle resultat, DateOnly date, long? inspecteurId,
        string? nature, string? intervenant, string? composant, string? notes, CancellationToken ct = default)
    {
        if (gestion?.Exiger(ActionProtegee.Revisions) is { Reussi: false } refus)
        {
            return refus;
        }

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

        var composantVise = (composant ?? "").Trim();
        if (string.Equals(composantVise, ServiceInventaire.ComposantComplet, StringComparison.OrdinalIgnoreCase))
        {
            composantVise = "";
        }

        if (composantVise.Length > 0)
        {
            var connu = ServiceInventaire.ComposantsDe(equipement, categorie).FirstOrDefault(c => string.Equals(c, composantVise, StringComparison.OrdinalIgnoreCase));
            if (connu is null)
            {
                return ResultatOperation.Echec($"{equipement.Code} n'a pas de composant « {composantVise} ».");
            }

            composantVise = connu;
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

        var controle = new Controle
        {
            EquipementId = equipement.Id,
            Type = type,
            DateControle = date,
            Resultat = resultat,
            InspecteurId = type == TypeControle.Tiv ? inspecteurId : null,
            SaisiLe = horloge.MaintenantUtc,
            Nature = (nature ?? "").Trim(),
            Intervenant = (intervenant ?? "").Trim(),
            Composant = composantVise,
            Notes = (notes ?? "").Trim(),
        };
        await revisions.AjouterControleAsync(controle, ct);

        // Intervention faite : l'équipement quitte la séance TIV où il était prévu, et sa révision passe dans « Fait ».
        await revisions.RetirerDesSeancesAsync(equipement.Id, ct);
        await TerminerSuiviAsync(equipement.Id, controle.Id, date, ct);

        // Et le problème signalé est traité. S'il avait mis l'équipement hors service, une intervention non refusée
        // le remet en service (hors atelier : c'est le retour d'atelier qui s'en charge).
        if (signalements is not null && await signalements.ObtenirOuvertAsync(equipement.Id, ct) is { } signalement)
        {
            if (signalement.MisHorsService && resultat != ResultatControle.Refuse && equipement.Etat == EtatEquipement.HorsService
                && await revisions.ObtenirPassageEnCoursAsync(equipement.Id, ct) is null)
            {
                equipement.Etat = EtatEquipement.EnService;
            }

            await signalements.TraiterAsync(equipement.Id, horloge.MaintenantUtc, ct);
        }

        if (resultat == ResultatControle.Refuse)
        {
            equipement.Etat = EtatEquipement.HorsService;
        }
        else if (composantVise.Length == 0)
        {
            // Seule une intervention sur l'équipement complet recalcule ses échéances (un octopus réparé ne repousse pas
            // la révision du détendeur) ; elle remet aussi son compteur d'utilisation à zéro (voir ListerCompteursAsync).
            var (revision, requalif) = NouvellesEcheances(type, resultat, date, categorie);
            equipement.DateProchaineRevision = revision;
            if (requalif is not null)
            {
                equipement.DateRequalification = requalif;
            }
        }

        await equipements.MettreAJourAsync(equipement, ct);
        await JournaliserAsync(ActionsJournal.ControleEnregistre,
            $"{equipement.Code}{(composantVise.Length > 0 ? $" ({composantVise.ToLower(Libelles.Culture)})" : "")} : {LibelleControle(type, categorie)} du {Libelles.Date(date)}, {LibelleResultat(resultat)}", ct, equipement.Id);

        return ResultatOperation.Ok(resultat switch
        {
            ResultatControle.Refuse => $"{equipement.Designation} refusé : il passe hors service.",
            _ => $"{equipement.Designation} : contrôle enregistré. {Consequence(type, resultat, date, categorie, composantVise.Length > 0)}",
        }, equipement.Id);
    }

    public static string LibelleResultat(ResultatControle resultat) => resultat switch
    {
        ResultatControle.Conforme => "Conforme",
        ResultatControle.ASurveiller => "À surveiller",
        _ => "Refusé",
    };

    /// <summary>La révision en cours de l'équipement passe dans « Fait » (sinon une ligne « Fait » est créée pour cette intervention).</summary>
    private async Task TerminerSuiviAsync(long equipementId, long controleId, DateOnly date, CancellationToken ct)
    {
        var suivi = await revisions.ObtenirSuiviEnCoursAsync(equipementId, ct);
        var nouveau = suivi is null;
        suivi ??= new SuiviRevision { EquipementId = equipementId, CreeLe = horloge.MaintenantUtc };
        suivi.Etape = EtapeRevision.Fait;
        suivi.ControleId = controleId;
        suivi.DateFait = date;
        if (nouveau)
        {
            await revisions.AjouterSuiviAsync(suivi, ct);
        }
        else
        {
            await revisions.MettreAJourSuiviAsync(suivi, ct);
        }
    }

    // ---------------------------------------------------------------- Tableau (À prévoir → Interne → À envoyer → Chez le prestataire → Fait)

    /// <summary>Interventions récentes montrées dans « Fait ».</summary>
    public const int JoursFait = 30;

    /// <summary>Natures proposées pour une intervention de ce type (la première est celle par défaut).</summary>
    public static IReadOnlyList<string> NaturesPour(TypeControle type, string? categorie) => type switch
    {
        TypeControle.Tiv => ["TIV"],
        TypeControle.Requalification => ["Requalification"],
        _ => new[] { LibelleControle(type, categorie), "Contrôle visuel", "Révision atelier", "Réparation", "Changement de pièce" }.Distinct().ToList(),
    };

    /// <summary>
    /// Tableau des révisions. À prévoir (calculé) : échéances à 90 jours ou dépassées, 90 % du seuil d'utilisations, problèmes
    /// signalés, sauf pour le matériel déjà engagé dans une révision. Réparation interne, À envoyer, Chez le prestataire : la
    /// révision en cours de l'équipement (table revision). Fait : interventions des 30 derniers jours, non archivées.
    /// « Bloqué pour les sorties » suit les réglages (révision dépassée, seuil atteint) ou un signalement « Hors service ».
    /// </summary>
    public async Task<TableauRevisions> TableauAsync(CancellationToken ct = default)
    {
        var aujourdHui = horloge.AujourdHui;
        var inventaire = await equipements.ListerInventaireAsync(null, null, ct);
        var parId = inventaire.ToDictionary(e => e.EquipementId);
        var echeances = await ListerEcheancesAsync(ct);
        var usages = compteur is null ? new Dictionary<long, UsageEquipement>() : await compteur.ListerUsagesAsync(inventaire, ct);
        var seance = await ProchaineSeanceAsync(ct);
        var suivis = await revisions.ListerSuivisAsync(aujourdHui.AddDays(-JoursFait), ct);
        var enCours = suivis.Where(s => s.Etape != EtapeRevision.Fait).GroupBy(s => s.EquipementId).ToDictionary(g => g.Key, g => g.First());
        var ouverts = signalements is null ? [] : (await signalements.ListerOuvertsAsync(ct)).GroupBy(s => s.EquipementId).ToDictionary(g => g.Key, g => g.First());

        // Déjà engagé dans une révision (ou en atelier sans révision enregistrée) : plus dans « À prévoir ».
        var engages = enCours.Keys.Concat((await revisions.ListerAtelierEnCoursAsync(ct)).Select(a => a.EquipementId)).ToHashSet();

        var cartes = new List<CarteRevision>();
        // Hors service (refusé) : il ne sortira plus, rien à prévoir ; il reste visible dans « Fait ».
        foreach (var l in echeances.Where(l => l.Etat != EtatEcheance.AJour && !engages.Contains(l.EquipementId)
                     && parId.TryGetValue(l.EquipementId, out var eq) && eq.Etat == EtatEquipement.EnService))
        {
            cartes.Add(Carte(EtapeRevision.APrevoir, l, usages.GetValueOrDefault(l.EquipementId)));
        }

        // Problèmes signalés par les moniteurs : en tête de « À prévoir » (même hors service), sauf déjà engagés.
        if (signalements is not null)
        {
            var signales = new List<CarteRevision>();
            foreach (var s in ouverts.Values)
            {
                if (!parId.TryGetValue(s.EquipementId, out var e) || e.Etat == EtatEquipement.Reforme || engages.Contains(s.EquipementId))
                {
                    continue;
                }

                var sortie = s.SortieId is { } sid ? await sorties.ObtenirResumeAsync(sid, ct) : null;
                var detail = string.Join(" · ", new[]
                {
                    $"Signalé le {DateOnly.FromDateTime(s.SignaleLe.ToLocalTime()).ToString("dd/MM", Libelles.Culture)}",
                    sortie?.Intitule ?? "inventaire",
                    s.Precision,
                }.Where(t => !string.IsNullOrWhiteSpace(t)));
                var horsService = s.Gravite == GraviteSignalement.HorsService;
                signales.Add(new CarteRevision(EtapeRevision.APrevoir, e.EquipementId, e.Code, e.Libelle, e.Categorie, FamilleSignalement,
                    TypesPour(e.Categorie)[0], s.Cause, detail,
                    horsService ? EtatCarte.HorsService : EtatCarte.ASurveiller, ServiceSignalements.Libelle(s.Gravite),
                    horsService, null, Signalement: true));
            }

            cartes.InsertRange(0, signales);
        }

        // Révisions en cours : réparation interne, à envoyer, chez le prestataire.
        foreach (var s in enCours.Values.OrderBy(s => s.DateEnvoi ?? DateOnly.MaxValue).ThenBy(s => s.CreeLe).ThenBy(s => s.Id))
        {
            if (parId.TryGetValue(s.EquipementId, out var e))
            {
                cartes.Add(CarteSuivi(s, e, echeances, usages.GetValueOrDefault(s.EquipementId), ouverts.GetValueOrDefault(s.EquipementId), seance));
            }
        }

        var controles = (await revisions.ListerControlesPeriodeAsync(aujourdHui.AddDays(-JoursFait), aujourdHui, ct)).ToDictionary(c => c.Id);
        foreach (var s in suivis.Where(s => s.Etape == EtapeRevision.Fait).OrderByDescending(s => s.DateFait).ThenByDescending(s => s.Id))
        {
            if (!parId.TryGetValue(s.EquipementId, out var e) || s.ControleId is not { } controleId || !controles.TryGetValue(controleId, out var c))
            {
                continue;
            }

            var prochaine = c.Type == TypeControle.Requalification ? e.DateRequalification : e.DateProchaineRevision;
            var detail = string.Join(" · ", new[]
            {
                $"Le {c.DateControle.ToString("dd/MM", Libelles.Culture)}",
                c.Intervenant,
                c.Resultat == ResultatControle.Refuse ? "hors service" : prochaine is { } p ? $"prochaine {Mois(p)}" : "",
            }.Where(t => !string.IsNullOrWhiteSpace(t)));
            var (etat, badge) = c.Resultat switch
            {
                ResultatControle.Conforme => (EtatCarte.Conforme, "Conforme"),
                ResultatControle.ASurveiller => (EtatCarte.ASurveiller, "À surveiller"),
                _ => (EtatCarte.HorsService, "Hors service"),
            };
            cartes.Add(new CarteRevision(EtapeRevision.Fait, e.EquipementId, e.Code, e.Libelle, e.Categorie, Famille(c.Type, e.Categorie), c.Type,
                c.Nature.Length > 0 ? c.Nature : LibelleControle(c.Type, e.Categorie), detail, etat, badge, false, null, SuiviId: s.Id));
        }

        return new TableauRevisions(seance, cartes);
    }

    /// <summary>
    /// Carte d'une révision en cours : l'échéance la plus urgente de l'équipement donne la pastille, la barre d'utilisations et
    /// le blocage ; la révision donne le motif, le prestataire, la note d'envoi et les dates.
    /// </summary>
    private CarteRevision CarteSuivi(SuiviRevision s, LigneInventaire e, IReadOnlyList<LigneEcheance> echeances, UsageEquipement? usage,
        Signalement? signalement, ProchaineSeance? seance)
    {
        var aujourdHui = horloge.AujourdHui;
        var pire = echeances.Where(l => l.EquipementId == e.EquipementId && l.Etat != EtatEcheance.AJour).OrderBy(l => l.Etat).FirstOrDefault();
        var type = pire?.Type ?? TypesPour(e.Categorie)[0];
        var horsService = signalement?.Gravite == GraviteSignalement.HorsService;
        var baseCarte = pire is not null
            ? Carte(s.Etape, pire, usage)
            : new CarteRevision(s.Etape, e.EquipementId, e.Code, e.Libelle, e.Categorie,
                signalement is not null ? FamilleSignalement : Famille(type, e.Categorie), type,
                signalement?.Cause ?? LibelleControle(type, e.Categorie), "",
                signalement is null ? EtatCarte.EnAtelier : horsService ? EtatCarte.HorsService : EtatCarte.ASurveiller,
                signalement is null ? "" : ServiceSignalements.Libelle(signalement.Gravite), false, null);

        var detail = s.Etape switch
        {
            EtapeRevision.Interne when s.SeanceTiv => seance is { } prochaine
                ? $"Séance TIV du {prochaine.Seance.DateSeance.ToString("dd/MM", Libelles.Culture)}"
                : "Séance TIV à planifier",
            EtapeRevision.Interne => string.Join(" · ", new[] { s.IntervenantInterne.Length > 0 ? $"Par {s.IntervenantInterne}" : "", baseCarte.Detail }
                .Where(t => t.Length > 0)),
            _ => baseCarte.Detail,
        };

        return baseCarte with
        {
            Controle = s.Motif.Length > 0 ? s.Motif : baseCarte.Controle,
            Detail = detail,
            Bloque = baseCarte.Bloque || horsService,
            Signalement = signalement is not null,
            SuiviId = s.Id,
            PassageAtelierId = s.AtelierId,
            Prestataire = s.Prestataire.Length > 0 ? s.Prestataire : null,
            NoteEnvoi = s.NoteEnvoi,
            DateEnvoi = s.DateEnvoi,
            DateRetourPrevue = s.DateRetourPrevue,
            JoursDehors = s.DateEnvoi is { } envoi ? Math.Max(0, aujourdHui.DayNumber - envoi.DayNumber) : null,
            RetourEnRetard = RetourEnRetard(s.Etape, s.DateRetourPrevue, aujourdHui),
            SeanceTiv = s.SeanceTiv,
        };
    }

    /// <summary>Nom de la colonne : « À prévoir », « Réparation interne », « À envoyer », « Chez le prestataire », « Fait ».</summary>
    public static string LibelleEtape(EtapeRevision etape) => etape switch
    {
        EtapeRevision.APrevoir => "À prévoir",
        EtapeRevision.Interne => "Réparation interne",
        EtapeRevision.AEnvoyer => "À envoyer",
        EtapeRevision.ChezPrestataire => "Chez le prestataire",
        _ => "Fait",
    };

    /// <summary>Retour en retard : toujours chez le prestataire alors que la date de retour prévue est passée.</summary>
    public static bool RetourEnRetard(EtapeRevision etape, DateOnly? retourPrevu, DateOnly aujourdHui) =>
        etape == EtapeRevision.ChezPrestataire && retourPrevu is { } prevu && prevu < aujourdHui;

    // ---------------------------------------------------------------- Transitions

    /// <summary>« Réparer au club » : depuis À prévoir ou À envoyer, la révision passe en réparation interne (responsable matériel).</summary>
    public async Task<ResultatOperation> ReparerAuClubAsync(long equipementId, string? motif = null, CancellationToken ct = default)
    {
        if (gestion?.Exiger(ActionProtegee.Revisions) is { Reussi: false } refus)
        {
            return refus;
        }

        if (await equipements.ObtenirAsync(equipementId, ct) is not { } equipement)
        {
            return ResultatOperation.Echec("Équipement introuvable.");
        }

        if (equipement.Etat == EtatEquipement.Reforme)
        {
            return ResultatOperation.Echec($"{equipement.Code} est réformé.");
        }

        var suivi = await revisions.ObtenirSuiviEnCoursAsync(equipementId, ct);
        switch (suivi?.Etape)
        {
            case EtapeRevision.ChezPrestataire:
                return ResultatOperation.Echec($"{equipement.Code} est chez {NomOuPrestataire(suivi.Prestataire)} : enregistrez d'abord son retour.");
            case EtapeRevision.Interne:
                return ResultatOperation.Echec($"{equipement.Code} est déjà en réparation au club.");
        }

        var intervenant = string.IsNullOrWhiteSpace(reglages.ResponsableMateriel) ? "le responsable matériel" : reglages.ResponsableMateriel.Trim();
        var nouveau = suivi is null;
        suivi ??= new SuiviRevision { EquipementId = equipementId, CreeLe = horloge.MaintenantUtc, Motif = (motif ?? "").Trim() };
        suivi.Etape = EtapeRevision.Interne;
        suivi.PrestataireId = null;
        suivi.Prestataire = "";
        suivi.NoteEnvoi = "";
        suivi.SeanceTiv = false;
        suivi.IntervenantInterne = intervenant;
        await EnregistrerSuiviAsync(suivi, nouveau, ct);
        await JournaliserAsync(ActionsJournal.RevisionEtape, $"{equipement.Code} : réparation au club, par {intervenant}", ct, equipementId);
        return ResultatOperation.Ok($"{equipement.Designation} : réparation au club.", suivi.Id);
    }

    /// <summary>
    /// « Envoyer chez un prestataire » : depuis À prévoir ou Réparation interne (pas un bloc prévu à la séance TIV), la
    /// révision passe dans « À envoyer ». Prestataire obligatoire (actif), note d'envoi facultative. L'équipement reste au club.
    /// </summary>
    public async Task<ResultatOperation> EnvoyerChezPrestataireAsync(long equipementId, long? prestataireId, string? noteEnvoi, string? motif = null, CancellationToken ct = default)
    {
        if (gestion?.Exiger(ActionProtegee.Revisions) is { Reussi: false } refus)
        {
            return refus;
        }

        if (prestataireId is not { } idPrestataire || prestataires is null
            || await prestataires.ObtenirAsync(idPrestataire, ct) is not { Actif: true } prestataire)
        {
            return ResultatOperation.Echec("Choisissez le magasin ou l'organisme.");
        }

        if (await equipements.ObtenirAsync(equipementId, ct) is not { } equipement)
        {
            return ResultatOperation.Echec("Équipement introuvable.");
        }

        if (equipement.Etat == EtatEquipement.Reforme)
        {
            return ResultatOperation.Echec($"{equipement.Code} est réformé.");
        }

        var note = (noteEnvoi ?? "").Trim();
        if (note.Length > LongueurNoteEnvoi)
        {
            return ResultatOperation.Echec($"La note d'envoi ne doit pas dépasser {LongueurNoteEnvoi} caractères.");
        }

        var suivi = await revisions.ObtenirSuiviEnCoursAsync(equipementId, ct);
        if (suivi is { Etape: EtapeRevision.ChezPrestataire })
        {
            return ResultatOperation.Echec($"{equipement.Code} est déjà chez {NomOuPrestataire(suivi.Prestataire)}.");
        }

        if (suivi is { Etape: EtapeRevision.Interne, SeanceTiv: true })
        {
            return ResultatOperation.Echec($"{equipement.Code} est prévu à la séance TIV : la TIV se fait au club.");
        }

        var nouveau = suivi is null;
        suivi ??= new SuiviRevision { EquipementId = equipementId, CreeLe = horloge.MaintenantUtc, Motif = (motif ?? "").Trim() };
        suivi.Etape = EtapeRevision.AEnvoyer;
        suivi.PrestataireId = prestataire.Id;
        suivi.Prestataire = prestataire.Nom;
        suivi.NoteEnvoi = note;
        suivi.IntervenantInterne = "";
        suivi.SeanceTiv = false;
        await EnregistrerSuiviAsync(suivi, nouveau, ct);
        await JournaliserAsync(ActionsJournal.RevisionEtape,
            $"{equipement.Code} : à envoyer chez {prestataire.Nom}{(note.Length > 0 ? $" ({note})" : "")}", ct, equipementId);
        return ResultatOperation.Ok($"{equipement.Designation} : à envoyer chez {prestataire.Nom}. Il reste au club jusqu'au départ.", suivi.Id);
    }

    /// <summary>Note d'envoi : longueur maximale.</summary>
    public const int LongueurNoteEnvoi = 120;

    /// <summary>
    /// « Marquer comme parti » : d'À envoyer à Chez le prestataire. Date d'envoi = aujourd'hui, retour prévu facultatif (pas passé).
    /// Ouvre un passage en atelier : l'équipement est hors service jusqu'à son retour. Refusé s'il est dehors dans une sortie.
    /// </summary>
    public async Task<ResultatOperation> MarquerPartiAsync(long equipementId, DateOnly? retourPrevu, CancellationToken ct = default)
    {
        if (gestion?.Exiger(ActionProtegee.Revisions) is { Reussi: false } refus)
        {
            return refus;
        }

        if (await equipements.ObtenirAsync(equipementId, ct) is not { } equipement)
        {
            return ResultatOperation.Echec("Équipement introuvable.");
        }

        if (await revisions.ObtenirSuiviEnCoursAsync(equipementId, ct) is not { Etape: EtapeRevision.AEnvoyer } suivi)
        {
            return ResultatOperation.Echec($"{equipement.Code} n'est pas dans « À envoyer ».");
        }

        var ouverture = await OuvrirPassageAsync(equipement, suivi.Prestataire, suivi.Motif, retourPrevu, ct);
        if (!ouverture.Reussi)
        {
            return ouverture;
        }

        suivi.Etape = EtapeRevision.ChezPrestataire;
        suivi.DateEnvoi = horloge.AujourdHui;
        suivi.DateRetourPrevue = retourPrevu;
        suivi.AtelierId = ouverture.Id;
        await revisions.MettreAJourSuiviAsync(suivi, ct);
        return ResultatOperation.Ok($"{equipement.Designation} est parti chez {suivi.Prestataire}. Il est hors service jusqu'à son retour.", suivi.Id);
    }

    /// <summary>
    /// Archive les cartes « Fait » (toutes celles de la colonne, ou une seule) : elles quittent la colonne, sans rien supprimer
    /// (l'intervention reste dans l'Historique et la fiche EPI). Pas de code gestionnaire : aucune donnée de matériel ne change.
    /// </summary>
    public async Task<ResultatOperation> ArchiverAsync(IReadOnlyList<long> suiviIds, CancellationToken ct = default)
    {
        var n = suiviIds.Count == 0 ? 0 : await revisions.ArchiverSuivisAsync(suiviIds, true, horloge.AujourdHui, ct);
        return n == 0
            ? ResultatOperation.Echec("Rien à archiver.")
            : ResultatOperation.Ok($"{Libelles.Pluriel(n, "intervention archivée", "interventions archivées")}.", n);
    }

    /// <summary>Annule l'archivage : les cartes reviennent dans « Fait ».</summary>
    public async Task<ResultatOperation> DesarchiverAsync(IReadOnlyList<long> suiviIds, CancellationToken ct = default)
    {
        var n = suiviIds.Count == 0 ? 0 : await revisions.ArchiverSuivisAsync(suiviIds, false, null, ct);
        return ResultatOperation.Ok($"{Libelles.Pluriel(n, "intervention revenue", "interventions revenues")} dans « Fait ».");
    }

    private static string NomOuPrestataire(string nom) => string.IsNullOrWhiteSpace(nom) ? "le prestataire" : nom;

    private async Task EnregistrerSuiviAsync(SuiviRevision suivi, bool nouveau, CancellationToken ct)
    {
        if (nouveau)
        {
            await revisions.AjouterSuiviAsync(suivi, ct);
        }
        else
        {
            await revisions.MettreAJourSuiviAsync(suivi, ct);
        }
    }

    /// <summary>Famille du filtre de Révisions pour les problèmes signalés.</summary>
    public const string FamilleSignalement = "Signalé";

    private static string Famille(TypeControle type, string categorie) => type switch
    {
        TypeControle.Tiv => "TIV",
        TypeControle.Requalification => "Requalification",
        _ => categorie,
    };

    private static string FamilleDe(LigneEcheance l) => l.ParUsage ? "Utilisation" : Famille(l.Type, l.Categorie);

    /// <summary>Carte d'une échéance (date ou compteur d'utilisation).</summary>
    private CarteRevision Carte(EtapeRevision etape, LigneEcheance l, UsageEquipement? usage)
    {
        var parUsage = l.ParUsage && usage is not null;
        var controle = parUsage ? $"{(l.Type == TypeControle.Tiv ? "TIV" : "Révision")} · {usage!.Seuil} utilisations" : l.Controle;
        var detail = parUsage
            ? usage!.Etat == EtatCompteur.Atteint ? "Seuil atteint" : $"{Libelles.Pluriel(usage.Seuil - usage.Utilisations, "utilisation")} {(usage.Seuil - usage.Utilisations > 1 ? "restantes" : "restante")}"
            : l.Etat == EtatEcheance.EnRetard ? $"Échue en {Mois(l.Echeance)}" : $"Échéance {Mois(l.Echeance)}";
        var (etat, badge) = l.Etat switch
        {
            EtatEcheance.EnRetard => (EtatCarte.EnRetard, "En retard"),
            EtatEcheance.CeMois => (EtatCarte.CeMois, "Ce mois-ci"),
            _ => (EtatCarte.APlanifier, "À planifier"),
        };
        var bloque = parUsage
            ? usage!.Etat == EtatCompteur.Atteint && reglages.BloquerAuSeuil
            : l.Etat == EtatEcheance.EnRetard && !reglages.DepartSiRevisionDepassee;
        return new CarteRevision(etape, l.EquipementId, l.Code, l.Libelle, l.Categorie, FamilleDe(l), l.Type, controle, detail, etat, badge, bloque, parUsage ? usage : null);
    }

    /// <summary>« Séance TIV » : un bloc rejoint la prochaine séance (colonne « Réparation interne »).</summary>
    public async Task<ResultatOperation> AjouterASeanceAsync(long equipementId, CancellationToken ct = default)
    {
        if (gestion?.Exiger(ActionProtegee.Revisions) is { Reussi: false } refus)
        {
            return refus;
        }

        if (await revisions.ProchaineSeanceAsync(horloge.AujourdHui, ct) is not { } seance)
        {
            return ResultatOperation.Echec("Aucune séance TIV prévue : planifiez-en une d'abord.");
        }

        if (await equipements.ObtenirAsync(equipementId, ct) is not { } equipement)
        {
            return ResultatOperation.Echec("Équipement introuvable.");
        }

        var categorie = (await categories.ListerAsync(ct)).FirstOrDefault(c => c.Id == equipement.CategorieId)?.Nom;
        if (!EstBloc(categorie))
        {
            return ResultatOperation.Echec("Seuls les blocs passent en séance TIV.");
        }

        if (equipement.Etat == EtatEquipement.Reforme)
        {
            return ResultatOperation.Echec($"{equipement.Code} est réformé.");
        }

        var suivi = await revisions.ObtenirSuiviEnCoursAsync(equipement.Id, ct);
        if (suivi is { Etape: EtapeRevision.ChezPrestataire })
        {
            return ResultatOperation.Echec($"{equipement.Code} est chez {NomOuPrestataire(suivi.Prestataire)} : enregistrez d'abord son retour.");
        }

        var nouveau = suivi is null;
        suivi ??= new SuiviRevision { EquipementId = equipement.Id, CreeLe = horloge.MaintenantUtc };
        suivi.Etape = EtapeRevision.Interne;
        suivi.SeanceTiv = true;
        suivi.IntervenantInterne = "Séance TIV";
        suivi.PrestataireId = null;
        suivi.Prestataire = "";
        suivi.NoteEnvoi = "";
        await EnregistrerSuiviAsync(suivi, nouveau, ct);
        await revisions.AjouterASeanceAsync(seance.Id, equipement.Id, ct);
        await JournaliserAsync(ActionsJournal.AjoutSeanceTiv, $"{equipement.Code} ajouté à la séance TIV du {Libelles.Date(seance.DateSeance)}", ct, equipement.Id);
        return ResultatOperation.Ok($"{equipement.Designation} ajouté à la séance TIV du {seance.DateSeance.ToString("d MMMM", Libelles.Culture)}.", equipement.Id);
    }

    /// <summary>Retire un bloc de la séance TIV (il revient dans « À prévoir » si son échéance approche).</summary>
    public async Task<ResultatOperation> RetirerDeSeanceAsync(long equipementId, CancellationToken ct = default)
    {
        if (gestion?.Exiger(ActionProtegee.Revisions) is { Reussi: false } refus)
        {
            return refus;
        }

        var equipement = await equipements.ObtenirAsync(equipementId, ct);
        await revisions.RetirerDesSeancesAsync(equipementId, ct);
        if (await revisions.ObtenirSuiviEnCoursAsync(equipementId, ct) is { Etape: EtapeRevision.Interne, SeanceTiv: true } suivi)
        {
            await revisions.SupprimerSuiviAsync(suivi.Id, ct); // De retour dans « À prévoir » si son échéance approche.
        }

        await JournaliserAsync(ActionsJournal.RetraitSeanceTiv, $"{equipement?.Code ?? equipementId.ToString(Libelles.Culture)} retiré de la séance TIV", ct, equipementId);
        return ResultatOperation.Ok("Retiré de la séance TIV.", equipementId);
    }

    /// <summary>
    /// Intervention enregistrée depuis le tableau : le contrôle (nature, intervenant, résultat), puis le retour d'atelier
    /// s'il y était (de nouveau en service, sauf refusé). L'équipement quitte la séance TIV où il était prévu.
    /// </summary>
    public async Task<ResultatOperation> EnregistrerInterventionAsync(Intervention intervention, CancellationToken ct = default)
    {
        if (gestion?.Exiger(ActionProtegee.Revisions) is { Reussi: false } refus)
        {
            return refus;
        }

        if (intervention.PassageAtelierId is { } passageId
            && await revisions.ObtenirPassageAsync(passageId, ct) is not { RevenuLe: null } passage)
        {
            return ResultatOperation.Echec("Ce passage en atelier est déjà terminé.");
        }

        // Chez le prestataire : l'intervention est son retour (même saisie depuis la fiche EPI).
        var passageOuvert = intervention.PassageAtelierId
            ?? (await revisions.ObtenirSuiviEnCoursAsync(intervention.EquipementId, ct))?.AtelierId;
        var resultat = await EnregistrerControleAsync(intervention.EquipementId, intervention.Type, intervention.Resultat, intervention.Date,
            intervention.InspecteurId, intervention.Nature, intervention.Intervenant, intervention.Composant, intervention.Notes, ct);
        if (!resultat.Reussi || passageOuvert is not { } passageAFermer
            || await revisions.ObtenirPassageAsync(passageAFermer, ct) is not { RevenuLe: null })
        {
            return resultat;
        }

        await RetourAtelierAsync(passageAFermer, ct);
        if (intervention.Resultat == ResultatControle.Refuse && await equipements.ObtenirAsync(intervention.EquipementId, ct) is { } equipement)
        {
            equipement.Etat = EtatEquipement.HorsService; // Revenu d'atelier, mais refusé : il ne repart pas.
            await equipements.MettreAJourAsync(equipement, ct);
        }

        return resultat;
    }

    // ---------------------------------------------------------------- Atelier

    public Task<IReadOnlyList<LigneAtelier>> ListerAtelierAsync(CancellationToken ct = default) =>
        revisions.ListerAtelierEnCoursAsync(ct);

    /// <summary>Envoie un équipement en atelier : il passe hors service jusqu'à son retour.</summary>
    public async Task<ResultatOperation> EnvoyerEnAtelierAsync(
        string codeBrut, string prestataire, string motif, DateOnly? retourPrevu, CancellationToken ct = default)
    {
        if (gestion?.Exiger(ActionProtegee.Revisions) is { Reussi: false } refus)
        {
            return refus;
        }

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

        var ouverture = await OuvrirPassageAsync(equipement, prestataire, motif, retourPrevu, ct);
        if (!ouverture.Reussi)
        {
            return ouverture;
        }

        // La révision passe « Chez le prestataire » (prestataire de la liste s'il porte ce nom).
        var nom = prestataire.Trim();
        var connu = prestataires is null ? null
            : (await prestataires.ListerAsync(ct)).FirstOrDefault(p => string.Equals(p.Nom.Trim(), nom, StringComparison.OrdinalIgnoreCase));
        var suivi = await revisions.ObtenirSuiviEnCoursAsync(equipement.Id, ct);
        var nouveau = suivi is null;
        suivi ??= new SuiviRevision { EquipementId = equipement.Id, CreeLe = horloge.MaintenantUtc, Motif = (motif ?? "").Trim() };
        suivi.Etape = EtapeRevision.ChezPrestataire;
        suivi.PrestataireId = connu?.Id;
        suivi.Prestataire = connu?.Nom ?? nom;
        suivi.SeanceTiv = false;
        suivi.DateEnvoi = horloge.AujourdHui;
        suivi.DateRetourPrevue = retourPrevu;
        suivi.AtelierId = ouverture.Id;
        await EnregistrerSuiviAsync(suivi, nouveau, ct);
        return ResultatOperation.Ok($"{equipement.Designation} envoyé en atelier. Il est hors service jusqu'à son retour.", ouverture.Id);
    }

    /// <summary>
    /// Ouvre un passage en atelier (envoi chez le prestataire) : refusé si l'équipement est dehors, déjà en atelier, ou si le
    /// retour prévu est passé. L'équipement passe hors service jusqu'à son retour. Id du passage dans le résultat.
    /// </summary>
    private async Task<ResultatOperation> OuvrirPassageAsync(Equipement equipement, string prestataire, string? motif, DateOnly? retourPrevu, CancellationToken ct)
    {
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
        await JournaliserAsync(ActionsJournal.EnvoiAtelier, $"{equipement.Code} envoyé chez {prestataire.Trim()}", ct, equipement.Id);
        return ResultatOperation.Ok("", id);
    }

    /// <summary>Retour d'atelier : l'équipement repasse en service.</summary>
    public async Task<ResultatOperation> RetourAtelierAsync(long passageId, CancellationToken ct = default)
    {
        if (gestion?.Exiger(ActionProtegee.Revisions) is { Reussi: false } refus)
        {
            return refus;
        }

        if (await revisions.ObtenirPassageAsync(passageId, ct) is not { RevenuLe: null } passage)
        {
            return ResultatOperation.Echec("Ce passage en atelier est déjà terminé.");
        }

        passage.RevenuLe = horloge.AujourdHui;
        await revisions.MettreAJourPassageAsync(passage, ct);

        // Revenu sans intervention enregistrée : la révision de ce passage est abandonnée (il revient dans « À prévoir » si besoin).
        if (await revisions.ObtenirSuiviEnCoursAsync(passage.EquipementId, ct) is { } suivi && suivi.AtelierId == passage.Id)
        {
            await revisions.SupprimerSuiviAsync(suivi.Id, ct);
        }

        var equipement = await equipements.ObtenirAsync(passage.EquipementId, ct);
        if (equipement is { Etat: EtatEquipement.HorsService })
        {
            equipement.Etat = EtatEquipement.EnService;
            await equipements.MettreAJourAsync(equipement, ct);
        }

        var code = equipement?.Code ?? $"Équipement {passage.EquipementId}";
        await JournaliserAsync(ActionsJournal.RetourAtelier, $"{code} revenu de chez {passage.Prestataire}", ct, passage.EquipementId);
        return ResultatOperation.Ok($"{code} revenu d'atelier : de nouveau en service.", passage.EquipementId);
    }

    // ---------------------------------------------------------------- Séances TIV

    public async Task<ResultatOperation> PlanifierSeanceAsync(DateOnly date, string? heure, string? lieu, CancellationToken ct = default)
    {
        if (gestion?.Exiger(ActionProtegee.Revisions) is { Reussi: false } refus)
        {
            return refus;
        }

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

    /// <summary>
    /// Délai de révision d'une catégorie en mois (Détendeurs : 24) ; 0 = pas de date obligatoire (Gilets).
    /// Catégorie sans délai propre : <see cref="Reglages.MoisEntreRevisions"/>.
    /// </summary>
    public static int MoisRevision(Reglages r, string? categorie)
    {
        var propre = r.MoisEntreRevisionsParCategorie.FirstOrDefault(p => string.Equals(p.Key, (categorie ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        return propre.Key is null ? r.MoisEntreRevisions : Math.Max(0, propre.Value);
    }

    private (DateOnly? Revision, DateOnly? Requalification) NouvellesEcheances(TypeControle type, ResultatControle resultat, DateOnly date, string? categorie)
    {
        var mois = resultat == ResultatControle.ASurveiller
            ? reglages.MoisSiASurveiller
            : type == TypeControle.Revision ? MoisRevision(reglages, categorie) : reglages.MoisEntreTiv;

        // Pas de date obligatoire (gilets) : une révision conforme ne fixe aucune échéance.
        DateOnly? revision = mois == 0 ? null : date.AddMonths(mois);

        // Une requalification vaut aussi inspection : la TIV repart de cette date.
        DateOnly? requalif = type == TypeControle.Requalification ? date.AddYears(reglages.AnneesEntreRequalifications) : null;
        return resultat == ResultatControle.Refuse ? (null, null) : (revision, requalif);
    }

    private static string Mois(DateOnly date) => date.ToString("MM/yyyy", Libelles.Culture);

    private Task JournaliserAsync(string action, string details, CancellationToken ct, long? equipementId = null, long? sortieId = null) =>
        journal.AjouterAsync(new EntreeJournal { Horodatage = horloge.MaintenantUtc, Action = action, Details = details, EquipementId = equipementId, SortieId = sortieId }, ct);
}
