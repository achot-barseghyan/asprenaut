using System.Text;
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
        [ActionsJournal.EquipementCree] = (TypeEvenement.Materiel, "Ajout"),
        [ActionsJournal.EquipementModifie] = (TypeEvenement.Materiel, "Modification"),
        [ActionsJournal.ControleEnregistre] = (TypeEvenement.Revision, "Contrôle"),
        [ActionsJournal.EnvoiAtelier] = (TypeEvenement.Revision, "Envoi en atelier"),
        [ActionsJournal.RetourAtelier] = (TypeEvenement.Revision, "Retour d'atelier"),
        [ActionsJournal.SeanceTivPlanifiee] = (TypeEvenement.Revision, "Séance TIV planifiée"),
        [ActionsJournal.MoniteurCree] = (TypeEvenement.Autre, "Moniteur ajouté"),
        [ActionsJournal.MoniteurModifie] = (TypeEvenement.Autre, "Moniteur modifié"),
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
            ActionsJournal.SortieCreee =>
                (l.SortieIntitule, l.SortieId is null ? "" : Joindre(
                    l.SortieDate?.ToString("dd/MM", Libelles.Culture),
                    l.SortieLieu is { Length: > 0 } lieu ? lieu : l.SortieType is { } t ? Libelles.De(t) : null,
                    l.MoniteurNom)),
            ActionsJournal.SortieCloturee =>
                (l.SortieIntitule, l.SortieId is null ? "" : Joindre(
                    Libelles.Pluriel(l.SortieNbEquipements, "équipement"),
                    l.SortieNbManquants == 0 ? "tout rendu" : Libelles.Pluriel(l.SortieNbManquants, "manquant"),
                    l.MoniteurNom)),
            ActionsJournal.EquipementCree => (equipement, l.EquipementId is null ? "" : Joindre("Nouvel équipement", l.Categorie)),
            ActionsJournal.EquipementModifie => (equipement, l.Categorie ?? ""),
            ActionsJournal.ControleEnregistre or ActionsJournal.EnvoiAtelier or ActionsJournal.RetourAtelier =>
                (equipement, SansCode(l.Details, l.Code)),
            _ => (null, ""),
        };

        var titre = sujet is { Length: > 0 } ? $"{debut} : {sujet}" : debut;
        // Entrée ancienne ou sans lien : le texte enregistré reste le meilleur détail.
        if (detail is null or "")
        {
            detail = l.Details;
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
    public static string ExporterCsv(IEnumerable<EvenementHistorique> evenements)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Date;Heure;Type;Événement;Détail;Code");
        foreach (var e in evenements)
        {
            csv.AppendJoin(';',
                e.Moment.ToString("dd/MM/yyyy", Libelles.Culture),
                e.Moment.ToString("HH:mm", Libelles.Culture),
                Champ(Libelle(e.Type)),
                Champ(e.Titre),
                Champ(e.Detail),
                Champ(e.Code ?? ""));
            csv.AppendLine();
        }

        return csv.ToString();
    }

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

        var categorie = (await categories.ListerAsync(ct)).FirstOrDefault(c => c.Id == e.CategorieId)?.Nom ?? "";
        var passages = await sorties.ListerPassagesAsync(e.Id, ct);
        var controles = await revisions.ListerControlesAsync(e.Id, ct);
        var ateliers = await revisions.ListerPassagesAtelierAsync(e.Id, ct);
        var noms = (await moniteurs.ListerAsync(actifsSeulement: false, ct)).ToDictionary(m => m.Id, m => m.NomComplet);

        var debutSaison = ServiceMoniteurs.DebutSaison(horloge.AujourdHui);
        var sortiesSaison = passages.Where(p => p.DateSortie >= debutSaison).Select(p => p.SortieId).Distinct().Count();
        var (statut, libelle, precision) = Statut(e, passages, ateliers);
        var (echeance, etat) = Echeance(e, categorie);
        return new VieEquipement(e, categorie, sortiesSaison, statut, libelle, precision, echeance, etat, Etapes(e, categorie, passages, controles, ateliers, noms));
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
        Dictionary<long, string> noms)
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

    private static string Majuscule(string texte) =>
        texte.Length == 0 ? texte : char.ToUpper(texte[0], Libelles.Culture) + texte[1..];

    private static string Champ(string valeur) =>
        valeur.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{valeur.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : valeur;
}
