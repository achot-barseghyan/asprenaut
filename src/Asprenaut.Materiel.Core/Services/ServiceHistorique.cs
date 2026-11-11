
using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Saisie;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>
/// Historique : le journal rendu lisible (type, titre, détail), filtré par période, avec les scans
/// rapprochés regroupés ; export CSV ; « vie » d'un équipement reconstituée depuis les sorties, contrôles et ateliers.
/// </summary>
public sealed class ServiceHistorique(
    IJournalRepository journal,
    IEquipementRepository equipements,
    ICategorieRepository categories,
    ISortieRepository sorties,
    IRevisionRepository revisions,
    IMoniteurRepository moniteurs,
    IHorloge horloge,
    Reglages reglages)
{
    /// <summary>Nombre maximum d'entrées lues (période « Tout » sur une base ancienne).</summary>
    public const int NombreMax = 5000;

    /// <summary>Deux scans de la même sortie à moins de 15 minutes d'écart sont regroupés.</summary>
    public static readonly TimeSpan EcartRegroupement = TimeSpan.FromMinutes(15);

    /// <summary>Types proposés en filtre (les « autres » ne sont visibles que dans « Tout »).</summary>
    public static readonly IReadOnlyList<TypeEvenement> TypesFiltrables =
        [TypeEvenement.Depart, TypeEvenement.Retour, TypeEvenement.Sortie, TypeEvenement.Revision, TypeEvenement.Materiel, TypeEvenement.Manquant];

    // Action du journal → type et début du titre.
    private static readonly Dictionary<string, (TypeEvenement Type, string Titre)> Actions = new()
    {
        [ActionsJournal.ScanDepart] = (TypeEvenement.Depart, "Départ"),
        [ActionsJournal.RetraitDepart] = (TypeEvenement.Depart, "Retiré du départ"),
        [ActionsJournal.DepartValide] = (TypeEvenement.Depart, "Départ validé"),
        [ActionsJournal.ScanRetour] = (TypeEvenement.Retour, "Retour"),
        [ActionsJournal.ManquantRetrouve] = (TypeEvenement.Manquant, "Retrouvé"),
        [ActionsJournal.ManquantDeclare] = (TypeEvenement.Manquant, "Manquant"),
        [ActionsJournal.SortieCreee] = (TypeEvenement.Sortie, "Sortie créée"),
        [ActionsJournal.SortieCloturee] = (TypeEvenement.Sortie, "Sortie clôturée"),
        [ActionsJournal.SortieModifiee] = (TypeEvenement.Sortie, "Sortie modifiée"),
        [ActionsJournal.SortieSupprimee] = (TypeEvenement.Sortie, "Sortie supprimée"),
        [ActionsJournal.EquipementCree] = (TypeEvenement.Materiel, "Ajout"),
        [ActionsJournal.EquipementModifie] = (TypeEvenement.Materiel, "Modification"),
        [ActionsJournal.ComposantAjoute] = (TypeEvenement.Materiel, "Composant ajouté"),
        [ActionsJournal.ControleEnregistre] = (TypeEvenement.Revision, "Contrôle"),
        [ActionsJournal.EnvoiAtelier] = (TypeEvenement.Revision, "Envoi en atelier"),
        [ActionsJournal.RetourAtelier] = (TypeEvenement.Revision, "Retour d'atelier"),
        [ActionsJournal.SeanceTivPlanifiee] = (TypeEvenement.Revision, "Séance TIV planifiée"),
        [ActionsJournal.AjoutSeanceTiv] = (TypeEvenement.Revision, "Ajouté à la séance TIV"),
        [ActionsJournal.RetraitSeanceTiv] = (TypeEvenement.Revision, "Retiré de la séance TIV"),
        [ActionsJournal.MoniteurCree] = (TypeEvenement.Autre, "Moniteur ajouté"),
        [ActionsJournal.MoniteurModifie] = (TypeEvenement.Autre, "Moniteur modifié"),
        [ActionsJournal.ClasseCreee] = (TypeEvenement.Autre, "Classe ajoutée"),
        [ActionsJournal.ClasseModifiee] = (TypeEvenement.Autre, "Classe modifiée"),
        ["DonneesDemo"] = (TypeEvenement.Autre, "Données de démonstration"),
    };

    public static string Libelle(TypeEvenement type) => type switch
    {
        TypeEvenement.Depart => "Départs",
        TypeEvenement.Retour => "Retours",
        TypeEvenement.Sortie => "Sorties",
        TypeEvenement.Revision => "Révisions",
        TypeEvenement.Materiel => "Matériel",
        TypeEvenement.Manquant => "Manquants",
        _ => "Autres",
    };

    public static TypeEvenement TypeDe(string action) =>
        Actions.TryGetValue(action, out var a) ? a.Type : TypeEvenement.Autre;

    /// <summary>« 7 derniers jours », « Saison 2026-2027 »...</summary>
    public string LibellePeriode(PeriodeHistorique periode) => periode switch
    {
        PeriodeHistorique.SeptJours => "7 derniers jours",
        PeriodeHistorique.TrenteJours => "30 derniers jours",
        PeriodeHistorique.Saison => $"Saison {ServiceMoniteurs.DebutSaison(horloge.AujourdHui).Year}-{ServiceMoniteurs.DebutSaison(horloge.AujourdHui).Year + 1}",
        _ => "Tout",
    };

    /// <summary>Premier jour de la période (inclus), ou null pour tout.</summary>
    public DateOnly? PremierJour(PeriodeHistorique periode) => periode switch
    {
        PeriodeHistorique.SeptJours => horloge.AujourdHui.AddDays(-6),
        PeriodeHistorique.TrenteJours => horloge.AujourdHui.AddDays(-29),
        PeriodeHistorique.Saison => ServiceMoniteurs.DebutSaison(horloge.AujourdHui),
        _ => null,
    };

    /// <summary>Événements de la période, les plus récents d'abord ; scans rapprochés regroupés si demandé.</summary>
    public async Task<IReadOnlyList<EvenementHistorique>> ListerAsync(PeriodeHistorique periode, bool regrouper = true, CancellationToken ct = default)
    {
        var depuis = PremierJour(periode) is { } jour
            ? jour.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime()
            : (DateTime?)null;
        var lignes = await journal.ListerAsync(depuis, NombreMax, ct);
        return regrouper ? Regrouper(lignes) : lignes.Select(Decrire).ToList();
    }

    /// <summary>
    /// Regroupe les scans successifs (départ ou retour) d'une même sortie, à moins de <see cref="EcartRegroupement"/>
    /// l'un de l'autre : « Retour : 3 équipements ». Les lignes arrivent les plus récentes d'abord.
    /// </summary>
    public static IReadOnlyList<EvenementHistorique> Regrouper(IReadOnlyList<LigneJournal> lignes)
    {
        var resultat = new List<EvenementHistorique>();
        var i = 0;
        while (i < lignes.Count)
        {
            var premiere = lignes[i];
            var groupe = new List<LigneJournal> { premiere };
            if (premiere.SortieId is not null && premiere.Action is ActionsJournal.ScanDepart or ActionsJournal.ScanRetour)
            {
                while (i + groupe.Count < lignes.Count)
                {
                    var suivante = lignes[i + groupe.Count];
                    var precedente = groupe[^1];
                    if (suivante.Action != premiere.Action
                        || suivante.SortieId != premiere.SortieId
                        || precedente.Horodatage - suivante.Horodatage > EcartRegroupement
                        || DateOnly.FromDateTime(precedente.Horodatage.ToLocalTime()) != DateOnly.FromDateTime(suivante.Horodatage.ToLocalTime()))
                    {
                        break;
                    }

                    groupe.Add(suivante);
                }
            }

            resultat.Add(groupe.Count == 1 ? Decrire(premiere) : DecrireGroupe(groupe));
            i += groupe.Count;
        }

        return resultat;
    }

    /// <summary>Titre et détail d'une entrée du journal, à partir de l'équipement et de la sortie liés.</summary>
    public static EvenementHistorique Decrire(LigneJournal l)
    {
        var (type, debut) = Actions.TryGetValue(l.Action, out var a) ? a : (TypeEvenement.Autre, l.Action);
        var equipement = l.Libelle is { Length: > 0 } ? l.Libelle : l.Code;
        var contexte = Joindre(ContexteSortie(l), l.MoniteurNom);

        var (sujet, detail) = l.Action switch
        {
            ActionsJournal.ScanDepart or ActionsJournal.RetraitDepart or ActionsJournal.ScanRetour or ActionsJournal.ManquantRetrouve =>
                (equipement, contexte),
            ActionsJournal.ManquantDeclare =>
                (equipement, l.SortieId is null ? "" : Joindre("Pas rendu à la clôture", contexte)),
            ActionsJournal.DepartValide =>
                (l.SortieIntitule, l.SortieId is null ? "" : Joindre(l.MoniteurNom, Libelles.Pluriel(l.SortieNbEquipements, "équipement"))),
            ActionsJournal.SortieCreee or ActionsJournal.SortieModifiee =>
                (l.SortieIntitule, l.SortieId is null ? "" : Joindre(
                    l.SortieDate?.ToString("dd/MM", Libelles.Culture),
                    l.SortieLieu is { Length: > 0 } lieu ? lieu : l.SortieTypeNom is { Length: > 0 } nomType ? nomType : l.SortieType is { } t ? Libelles.De(t) : null,
                    l.MoniteurNom)),
            ActionsJournal.SortieCloturee =>
                (l.SortieIntitule, l.SortieId is null ? "" : Joindre(
                    Libelles.Pluriel(l.SortieNbEquipements, "équipement"),
                    l.SortieNbManquants == 0 ? "tout rendu" : Libelles.Pluriel(l.SortieNbManquants, "manquant"),
                    l.MoniteurNom)),
            ActionsJournal.EquipementCree => (equipement, l.EquipementId is null ? "" : Joindre("Nouvel équipement", l.Categorie)),
            ActionsJournal.EquipementModifie => (equipement, l.Categorie ?? ""),
            ActionsJournal.ComposantAjoute => (equipement, SansCode(l.Details, l.Code)),
            ActionsJournal.ControleEnregistre or ActionsJournal.EnvoiAtelier or ActionsJournal.RetourAtelier
                or ActionsJournal.AjoutSeanceTiv or ActionsJournal.RetraitSeanceTiv =>
                (equipement, SansCode(l.Details, l.Code)),
            _ => (null, ""),
        };

        // Sortie supprimée (ou entrée ancienne) : l'intitulé est repris du texte enregistré, entre « ».
        if (sujet is null or "" && type == TypeEvenement.Sortie && l.SortieIntitule is null)
        {
            sujet = IntituleCite(l.Details);
        }

        var titre = sujet is { Length: > 0 } ? $"{debut} : {sujet}" : debut;
        // Entrée ancienne ou sans lien : le texte enregistré reste le meilleur détail (sans le numéro interne de sortie).
        if (detail is null or "")
        {
            detail = SansNumeroDeSortie(l.Details);
        }

        return new EvenementHistorique(l.Id, l.Horodatage.ToLocalTime(), type, titre, detail, l.Code is { Length: > 0 } ? l.Code : null);
    }

    private static EvenementHistorique DecrireGroupe(List<LigneJournal> groupe)
    {
        var premiere = groupe[0];
        var (type, debut) = Actions[premiere.Action];
        var noms = groupe.Select(l => l.Libelle is { Length: > 0 } ? l.Libelle : l.Code ?? "").Where(n => n.Length > 0).Distinct().ToList();
        var liste = string.Join(", ", noms.Take(2));
        if (noms.Count > 2)
        {
            liste += $" et {Libelles.Pluriel(noms.Count - 2, "autre")}";
        }

        // La sortie d'abord : sur un écran étroit, c'est la liste des équipements qui est coupée.
        return new EvenementHistorique(
            premiere.Id,
            premiere.Horodatage.ToLocalTime(),
            type,
            $"{debut} : {Libelles.Pluriel(groupe.Count, "équipement")}",
            Joindre(ContexteSortie(premiere), premiere.MoniteurNom, liste),
            null,
            groupe.Count);
    }

    /// <summary>Intitulé du jour : « Aujourd'hui », « Hier » (avec la date en précision), sinon « Vendredi 6 novembre ».</summary>
    public static (string Titre, string Precision) LibelleJour(DateOnly jour, DateOnly aujourdHui)
    {
        var format = jour.Year == aujourdHui.Year ? "dddd d MMMM" : "dddd d MMMM yyyy";
        var date = jour.ToString(format, Libelles.Culture);
        return (aujourdHui.DayNumber - jour.DayNumber) switch
        {
            0 => ("Aujourd'hui", date),
            1 => ("Hier", date),
            _ => (Majuscule(date), ""),
        };
    }

    /// <summary>Export CSV (séparateur « ; », lisible par Excel en français).</summary>
    public static string ExporterCsv(IEnumerable<EvenementHistorique> evenements) => Csv.Ecrire(
        ["Date", "Heure", "Type", "Événement", "Détail", "Code"],
        evenements.Select(e => new[]
        {
            e.Moment.ToString("dd/MM/yyyy", Libelles.Culture),
            e.Moment.ToString("HH:mm", Libelles.Culture),
            Libelle(e.Type),
            e.Titre,
            e.Detail,
            e.Code ?? "",
        }));

    /// <summary>Nom de fichier proposé : historique_2026-11-09.csv.</summary>
    public string NomFichierExport() => $"historique_{horloge.AujourdHui:yyyy-MM-dd}.csv";

    // ---------------------------------------------------------------- Vie d'un équipement

    /// <summary>Fiche d'un équipement scanné ou tapé ; null si le code est inconnu.</summary>
    public async Task<VieEquipement?> ObtenirVieAsync(string codeBrut, CancellationToken ct = default)
    {
        var code = CodeScanne.Nettoyer(codeBrut, reglages.PrefixeEtiquette);
        if (code.Length == 0 || await equipements.ObtenirParCodeAsync(code, ct) is not { } e)
        {
            return null;
        }

        return ConstruireVie(e, await ChargerAsync(e, ct));
    }

    /// <summary>Fiche d'un équipement par son identifiant (depuis l'inventaire) ; null s'il n'existe plus.</summary>
    public async Task<VieEquipement?> ObtenirVieAsync(long equipementId, CancellationToken ct = default) =>
        await equipements.ObtenirAsync(equipementId, ct) is { } e ? ConstruireVie(e, await ChargerAsync(e, ct)) : null;

    /// <summary>
    /// Fiche de suivi complète : la vie de l'équipement (où il est, échéance, parcours), ses composants et toutes
    /// ses interventions (contrôles et passages en atelier), de la plus récente à la plus ancienne.
    /// </summary>
    public async Task<FicheSuiviEquipement?> ObtenirFicheSuiviAsync(long equipementId, CancellationToken ct = default)
    {
        if (await equipements.ObtenirAsync(equipementId, ct) is not { } e)
        {
            return null;
        }

        var donnees = await ChargerAsync(e, ct);
        var composants = Composants(e, donnees.Categorie, donnees.Controles, await equipements.ListerComposantsAsync(e.Id, ct));
        return new FicheSuiviEquipement(ConstruireVie(e, donnees), TitreDe(e, composants), composants, Interventions(donnees),
            donnees.Controles.Where(c => c.Type == TypeControle.Revision && c.Composant.Length == 0).Select(c => (DateOnly?)c.DateControle).Max());
    }

    private sealed record DonneesVie(
        string Categorie,
        IReadOnlyList<PassageSortie> Passages,
        IReadOnlyList<Controle> Controles,
        IReadOnlyList<PassageAtelier> Ateliers,
        IReadOnlyDictionary<long, string> Noms);

    private async Task<DonneesVie> ChargerAsync(Equipement e, CancellationToken ct)
    {
        var categorie = (await categories.ListerAsync(ct)).FirstOrDefault(c => c.Id == e.CategorieId)?.Nom ?? "";
        var passages = await sorties.ListerPassagesAsync(e.Id, ct);
        var controles = await revisions.ListerControlesAsync(e.Id, ct);
        var ateliers = await revisions.ListerPassagesAtelierAsync(e.Id, ct);
        var noms = (await moniteurs.ListerAsync(actifsSeulement: false, ct)).ToDictionary(m => m.Id, m => m.NomComplet);
        return new DonneesVie(categorie, passages, controles, ateliers, noms);
    }

    private VieEquipement ConstruireVie(Equipement e, DonneesVie d)
    {
        var debutSaison = ServiceMoniteurs.DebutSaison(horloge.AujourdHui);
        var sortiesSaison = d.Passages.Where(p => p.DateSortie >= debutSaison).Select(p => p.SortieId).Distinct().Count();
        var (statut, libelle, precision) = Statut(e, d.Passages, d.Ateliers);
        var (echeance, etat) = Echeance(e, d.Categorie);
        return new VieEquipement(e, d.Categorie, sortiesSaison, statut, libelle, precision, echeance, etat,
            Etapes(e, d.Categorie, d.Passages, d.Controles, d.Ateliers, d.Noms));
    }

    // ---------------------------------------------------------------- Composants et interventions de la fiche

    /// <summary>
    /// Cartes « Composants » : un détendeur est détaillé (premier étage, puis deuxième étage, octopus et manomètre
    /// renseignés dans sa fiche) ; les autres équipements n'ont qu'une carte « Équipement complet ».
    /// </summary>
    private static IReadOnlyList<ComposantSuivi> Composants(Equipement e, string categorie, IReadOnlyList<Controle> controles, IReadOnlyList<DetailComposant> details)
    {
        var roles = ServiceInventaire.ComposantsDe(e, categorie);
        if (roles.Count == 0)
        {
            return [new ComposantSuivi(ServiceInventaire.ComposantComplet, ModeleDe(e.Marque, e.Modele, e.Libelle), e.NumeroSerie, Tuiles(e, controles, null))];
        }

        return roles.Select(role =>
        {
            var detail = details.FirstOrDefault(d => d.Role == role);
            var tuiles = Tuiles(e, controles, role, detail);
            return role switch
            {
                "Deuxième étage" => new ComposantSuivi(role, ModeleDe(e.MarqueDeuxiemeEtage, e.ModeleDeuxiemeEtage), e.NumeroDeuxiemeEtage, tuiles),
                "Octopus" => new ComposantSuivi(role, ModeleDe(e.Octopus), e.NumeroOctopus, tuiles),
                "Manomètre" => new ComposantSuivi(role, ModeleDe(e.Manometre), detail?.NumeroSerie ?? "", tuiles),
                _ => new ComposantSuivi(role, ModeleDe(e.Marque, e.Modele), e.NumeroSerie, tuiles),
            };
        }).ToList();
    }

    /// <summary>
    /// Tuiles d'une carte : date d'ajout, dernier contrôle (de ce composant ou de tout l'équipement), prochaine révision et
    /// état (ceux de l'équipement : la base ne suit pas les échéances composant par composant).
    /// </summary>
    private static IReadOnlyList<TuileComposant> Tuiles(Equipement e, IReadOnlyList<Controle> controles, string? role, DetailComposant? detail = null)
    {
        var ajoute = DateOnly.FromDateTime(e.CreeLe.ToLocalTime());
        var siens = controles.Where(c => c.Composant.Length == 0 || string.Equals(c.Composant, role, StringComparison.OrdinalIgnoreCase)).ToList();
        var dernier = siens.Count == 0 ? (DateOnly?)null : siens.Max(c => c.DateControle);
        TuileComposant derniereTuile = new("Dernier contrôle", dernier is { } date ? Libelles.Date(date) : "—");
        TuileComposant prochaine = new("Prochaine révision", e.ProchaineEcheance is { } revision ? revision.ToString("MM/yyyy", Libelles.Culture) : "—");
        TuileComposant etat = new("État", Libelles.De(e.Etat));
        if (detail is null)
        {
            return [new("Ajouté le", Libelles.Date(ajoute)), derniereTuile, prochaine, etat];
        }

        // Composant ajouté depuis la fiche de suivi : fabrication, mise en service et origine connues.
        return
        [
            new("Fabrication", detail.Fabrication is { } f ? f.ToString("MM/yyyy", Libelles.Culture) : "—"),
            new("Mise en service", detail.MiseEnService is { } s ? Libelles.Date(s) : "—"),
            derniereTuile,
            prochaine,
            new("Origine", detail.Origine == OrigineComposant.Neuve ? "Pièce neuve" : "Récupérée"),
            etat,
        ];
    }

    /// <summary>
    /// Titre de la fiche : le nom, puis le modèle de chaque composant sans les mots déjà écrits (la marque) :
    /// « Détendeur Scubapro · MK2 · R295 · R095 ». Le manomètre (une marque seulement) n'y figure pas.
    /// </summary>
    private static string TitreDe(Equipement e, IReadOnlyList<ComposantSuivi> composants)
    {
        var parties = new List<string> { e.Libelle.Trim().Length > 0 ? e.Libelle.Trim() : e.Code };
        foreach (var modele in composants.Where(c => c.Role != "Manomètre").Select(c => c.Modele).Where(m => m != "—"))
        {
            var deja = string.Join(" ", parties).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var nouveau = string.Join(" ", modele.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(mot => !deja.Contains(mot, StringComparer.OrdinalIgnoreCase)));
            if (nouveau.Length > 0)
            {
                parties.Add(nouveau);
            }
        }

        return string.Join(" · ", parties);
    }

    /// <summary>
    /// Contrôles et passages en atelier, du plus récent au plus ancien (à date égale, le dernier saisi d'abord).
    /// Nature : celle saisie (sinon celle du contrôle) suivie des détails ; atelier : le motif, « En atelier » ou la date de retour.
    /// </summary>
    private static IReadOnlyList<InterventionSuivie> Interventions(DonneesVie d)
    {
        var interventions = new List<(InterventionSuivie Ligne, int Ordre)>();
        foreach (var c in d.Controles)
        {
            var nature = c.Nature.Trim();
            if (nature.Length == 0)
            {
                nature = ServiceRevisions.LibelleControle(c.Type, d.Categorie);
            }

            if (c.Notes.Trim().Length > 0)
            {
                nature = $"{nature} : {c.Notes.Trim()}";
            }

            var intervenant = c.Intervenant.Trim();
            if (intervenant.Length == 0 && c.InspecteurId is { } id && d.Noms.TryGetValue(id, out var nom))
            {
                intervenant = nom;
            }

            interventions.Add((new InterventionSuivie(c.DateControle, c.Composant.Length > 0 ? c.Composant : ServiceInventaire.ComposantComplet,
                nature, intervenant.Length > 0 ? intervenant : "—", TypeInterventionSuivie.Controle,
                ServiceRevisions.LibelleResultat(c.Resultat), c.Resultat), 1));
        }

        foreach (var a in d.Ateliers)
        {
            var motif = a.Motif.Trim();
            var prestataire = a.Prestataire.Trim();
            interventions.Add((new InterventionSuivie(a.EnvoyeLe, ServiceInventaire.ComposantComplet,
                motif.Length > 0 ? motif : "Envoi en atelier", prestataire.Length > 0 ? prestataire : "—", TypeInterventionSuivie.Atelier,
                a.RevenuLe is { } revenu ? $"Revenu le {Libelles.Date(revenu)}" : "En atelier", null), 0));
        }

        // Les contrôles arrivent déjà du plus récent au plus ancien (date puis saisie) : le tri stable garde cet ordre.
        return interventions.OrderByDescending(i => i.Ligne.Date).ThenByDescending(i => i.Ordre).Select(i => i.Ligne).ToList();
    }

    private static string ModeleDe(string? marque, string? modele = null, string? libelle = null)
    {
        var parties = new[] { marque, modele }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .ToArray();
        if (parties.Length > 0)
        {
            return string.Join(" ", parties);
        }

        return string.IsNullOrWhiteSpace(libelle) ? "—" : libelle.Trim();
    }

    private (StatutInventaire, string, string) Statut(Equipement e, IReadOnlyList<PassageSortie> passages, IReadOnlyList<PassageAtelier> ateliers)
    {
        if (e.Etat == EtatEquipement.Reforme)
        {
            return (StatutInventaire.Reforme, "Réformé", "ne sort plus");
        }

        if (e.Etat == EtatEquipement.HorsService)
        {
            return ateliers.FirstOrDefault(a => a.RevenuLe is null) is { } atelier
                ? (StatutInventaire.HorsService, "Atelier", atelier.Prestataire)
                : (StatutInventaire.HorsService, "Hors service", "ne part pas");
        }

        if (passages.FirstOrDefault(p => p.Statut == StatutSortieEquipement.Dehors) is { } dehors)
        {
            return (StatutInventaire.Dehors, "Sorti", dehors.Intitule);
        }

        if (passages.FirstOrDefault(p => p.Statut == StatutSortieEquipement.Manquant) is { } manquant)
        {
            return (StatutInventaire.Manquant, "Manquant", manquant.ClotureeLe is { } c ? $"depuis le {c.ToLocalTime().ToString("dd/MM", Libelles.Culture)}" : "non rendu");
        }

        var dernierRetour = passages.Where(p => p.ScanneRetourLe is not null).Max(p => p.ScanneRetourLe);
        return (StatutInventaire.Disponible, "Au local", dernierRetour is { } r ? Depuis(DateOnly.FromDateTime(r.ToLocalTime())) : "disponible");
    }

    private string Depuis(DateOnly jour) => (horloge.AujourdHui.DayNumber - jour.DayNumber) switch
    {
        <= 0 => "depuis aujourd'hui",
        1 => "depuis hier",
        _ => $"depuis le {jour.ToString("dd/MM", Libelles.Culture)}",
    };

    /// <summary>« TIV 11/2026 », « Requalif. 03/2029 », « 05/2027 » ou « — », avec l'état (badge jaune / rouge).</summary>
    private (string, EtatRevision) Echeance(Equipement e, string categorie)
    {
        if (e.ProchaineEcheance is not { } date)
        {
            return ("—", EtatRevision.AJour);
        }

        var mois = date.ToString("MM/yyyy", Libelles.Culture);
        var texte = !ServiceRevisions.EstBloc(categorie) ? mois
            : date == e.DateProchaineRevision ? $"TIV {mois}"
            : $"Requalif. {mois}";
        var aujourdHui = horloge.AujourdHui;
        var etat = date < aujourdHui ? EtatRevision.Depassee
            : date <= aujourdHui.AddDays(reglages.JoursAvantRevision) ? EtatRevision.Proche
            : EtatRevision.AJour;
        return (texte, etat);
    }

    private static IReadOnlyList<EtapeVie> Etapes(
        Equipement e,
        string categorie,
        IReadOnlyList<PassageSortie> passages,
        IReadOnlyList<Controle> controles,
        IReadOnlyList<PassageAtelier> ateliers,
        IReadOnlyDictionary<long, string> noms)
    {
        var etapes = new List<EtapeVie>();
        foreach (var p in passages)
        {
            var contexte = Joindre(p.Intitule, p.MoniteurNom);
            etapes.Add(new(p.ScanneDepartLe.ToLocalTime(), false, TypeEvenement.Depart, "Parti", contexte));

            // Noté manquant à la clôture : il l'est encore, ou il a été rendu après la clôture (retrouvé).
            var retrouve = p.ScanneRetourLe is { } r && p.ClotureeLe is { } c && r > c;
            if (p.ClotureeLe is { } cloture && (p.Statut == StatutSortieEquipement.Manquant || retrouve))
            {
                etapes.Add(new(cloture.ToLocalTime(), false, TypeEvenement.Manquant, "Noté manquant", Joindre("Sortie clôturée", p.MoniteurNom)));
            }

            if (p.ScanneRetourLe is { } retour)
            {
                etapes.Add(new(retour.ToLocalTime(), false, retrouve ? TypeEvenement.Manquant : TypeEvenement.Retour, retrouve ? "Retrouvé et rendu" : "Rentré", contexte));
            }
        }

        foreach (var c in controles)
        {
            var resultat = ServiceRevisions.LibelleResultat(c.Resultat).ToLower(Libelles.Culture);
            var inspecteur = c.InspecteurId is { } id && noms.TryGetValue(id, out var nom) ? nom : null;
            etapes.Add(new(c.DateControle.ToDateTime(TimeOnly.MinValue), true, TypeEvenement.Revision,
                $"{ServiceRevisions.LibelleControle(c.Type, categorie)} {resultat}", Joindre(inspecteur, c.Notes)));
        }

        foreach (var a in ateliers)
        {
            etapes.Add(new(a.EnvoyeLe.ToDateTime(TimeOnly.MinValue), true, TypeEvenement.Revision, "Envoyé en atelier", Joindre(a.Prestataire, a.Motif)));
            if (a.RevenuLe is { } revenu)
            {
                etapes.Add(new(revenu.ToDateTime(TimeOnly.MinValue), true, TypeEvenement.Revision, "Revenu d'atelier", a.Prestataire));
            }
        }

        // Ajouté en dernier : à la même seconde qu'un premier départ, il reste en bas (le tri est stable).
        etapes.Add(new(e.CreeLe.ToLocalTime(), false, TypeEvenement.Materiel, "Ajouté à l'inventaire", categorie));

        // Le plus récent d'abord ; à date égale, une étape datée sans heure passe après celles de la journée.
        return etapes
            .OrderByDescending(x => DateOnly.FromDateTime(x.Moment))
            .ThenBy(x => x.DateSeule)
            .ThenByDescending(x => x.Moment)
            .ToList();
    }

    // ---------------------------------------------------------------- Outils de texte

    private static string? ContexteSortie(LigneJournal l) => l.SortieIntitule is not { Length: > 0 } intitule
        ? null
        : l.SortieDate is { } d ? $"{intitule} du {d.ToString("dd/MM", Libelles.Culture)}" : intitule;

    private static string Joindre(params string?[] parties) =>
        string.Join(" · ", parties.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

    /// <summary>« BLOC-031 envoyé chez Aqua » → « Envoyé chez Aqua » ; « BLOC-031 : TIV… » → « TIV… ».</summary>
    private static string SansCode(string details, string? code)
    {
        var texte = details;
        if (code is { Length: > 0 } && texte.StartsWith(code, StringComparison.OrdinalIgnoreCase))
        {
            texte = texte[code.Length..].TrimStart(' ', ':');
        }

        return Majuscule(texte);
    }

    /// <summary>« Sortie 8 « Fosse » du 09/11/2026 … » → « Fosse » ; null s'il n'y a pas de texte entre guillemets.</summary>
    private static string? IntituleCite(string details)
    {
        var debut = details.IndexOf('«', StringComparison.Ordinal);
        var fin = debut < 0 ? -1 : details.IndexOf('»', debut + 1);
        return fin > debut + 1 ? details[(debut + 1)..fin].Trim() : null;
    }

    /// <summary>« Sortie 8 « Fosse » du … » → « Sortie « Fosse » du … » : le numéro interne ne parle pas aux bénévoles.</summary>
    private static string SansNumeroDeSortie(string details) =>
        System.Text.RegularExpressions.Regex.Replace(details, @"^Sortie \d+ ", "Sortie ");

    private static string Majuscule(string texte) =>
        texte.Length == 0 ? texte : char.ToUpper(texte[0], Libelles.Culture) + texte[1..];
}
