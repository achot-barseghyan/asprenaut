using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>
/// Bilan d'une saison (1er septembre → 31 août) pour l'assemblée générale : sorties par mois et par type,
/// tension sur le stock, matériel le plus et le moins utilisé, pertes et contrôles faits.
/// Seules les sorties parties comptent (départ validé : en cours ou clôturées).
/// </summary>
public sealed class ServiceBilan(
    ISortieRepository sorties,
    IRevisionRepository revisions,
    IEquipementRepository equipements,
    IHorloge horloge)
{
    /// <summary>En dessous de ce nombre de sorties dans la saison, un équipement est « peu sorti ».</summary>
    public const int SeuilPeuSorti = 3;

    /// <summary>Nombre d'équipements du classement « Les plus utilisés ».</summary>
    public const int TaillePlusUtilises = 5;

    /// <summary>Nombre de groupes de matériel du tableau « Tension sur le stock ».</summary>
    public const int TailleTension = 8;

    /// <summary>Stock minimum d'un groupe pour figurer dans « Tension sur le stock ».</summary>
    public const int StockMinTension = 2;

    // Mois de la saison, de septembre à août ; juillet et août ne sont montrés que s'il y a eu des sorties.
    private static readonly int[] MoisSaison = [9, 10, 11, 12, 1, 2, 3, 4, 5, 6, 7, 8];

    private static readonly Dictionary<int, string> MoisCourts = new()
    {
        [1] = "Jan", [2] = "Fév", [3] = "Mar", [4] = "Avr", [5] = "Mai", [6] = "Juin",
        [7] = "Juil", [8] = "Août", [9] = "Sep", [10] = "Oct", [11] = "Nov", [12] = "Déc",
    };

    /// <summary>Saison en cours et saison précédente, la plus ancienne d'abord.</summary>
    public IReadOnlyList<SaisonBilan> ListerSaisons()
    {
        var annee = ServiceMoniteurs.DebutSaison(horloge.AujourdHui).Year;
        return [new SaisonBilan(annee - 1, false), new SaisonBilan(annee, true)];
    }

    public async Task<Bilan> ObtenirAsync(int anneeDebut, CancellationToken ct = default)
    {
        var saison = new SaisonBilan(anneeDebut, ServiceMoniteurs.DebutSaison(horloge.AujourdHui).Year == anneeDebut);

        // Sorties en préparation : rien n'est encore parti, elles ne comptent pas.
        var passages = (await sorties.ListerPassagesPeriodeAsync(saison.Debut, saison.Fin, ct))
            .Where(p => p.StatutSortie != StatutSortie.EnPreparation)
            .ToList();
        var controles = await revisions.ListerControlesPeriodeAsync(saison.Debut, saison.Fin, ct);
        var inventaire = await equipements.ListerInventaireAsync(null, null, ct);

        var parSortie = passages.GroupBy(p => p.SortieId).Select(g => g.First()).ToList();
        var mois = Mois(parSortie);
        var types = parSortie
            .GroupBy(s => (s.TypeId, Libelle: s.TypeNom is { Length: > 0 } nom ? nom : LibelleType(s.TypeSortie)))
            .Select(g => new TypeBilan(g.First().TypeSortie, g.Key.Libelle, g.Count()))
            .OrderByDescending(t => t.Sorties)
            .ThenBy(t => t.Type)
            .ThenBy(t => t.Libelle, StringComparer.Create(Libelles.Culture, true))
            .ToList();
        var pertes = Pertes(passages);
        var familles = Familles(controles, inventaire);

        var sortiesParEquipement = passages
            .GroupBy(p => p.EquipementId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.SortieId).Distinct().Count());
        var plusUtilises = passages
            .GroupBy(p => p.EquipementId)
            .Select(g => new UtilisationBilan(g.First().Code, g.First().Libelle, sortiesParEquipement[g.Key]))
            .OrderByDescending(u => u.Sorties)
            .ThenBy(u => u.Code, StringComparer.Ordinal)
            .Take(TaillePlusUtilises)
            .ToList();
        var peuSortis = inventaire
            .Where(e => e.Etat == EtatEquipement.EnService)
            .Select(e => new UtilisationBilan(e.Code, e.Libelle, sortiesParEquipement.GetValueOrDefault(e.EquipementId)))
            .Where(u => u.Sorties < SeuilPeuSorti)
            .OrderBy(u => u.Sorties)
            .ThenBy(u => u.Code, StringComparer.Ordinal)
            .ToList();

        var chiffres = new List<ChiffreBilan>
        {
            new("Sorties", Nombre(parSortie.Count), PrecisionSorties(saison, parSortie)),
            new("Équipements sortis", Nombre(passages.Count), parSortie.Count == 0
                ? "Aucun départ"
                : $"{Nombre((int)Math.Round((double)passages.Count / parSortie.Count))} en moyenne par sortie"),
            new("Manquants", Nombre(pertes.Count), PrecisionManquants(pertes)),
            new("Révisions faites", Nombre(controles.Count), familles.Count == 0
                ? "Aucun contrôle enregistré"
                : string.Join(", ", familles.Take(3).Select(f => $"{f.Nombre} {Minuscule(f.Libelle, f.Nombre)}"))),
        };

        return new Bilan(
            saison,
            parSortie.Count,
            passages.Count,
            chiffres,
            mois,
            NoteMois(saison, mois),
            types,
            Tension(passages, inventaire),
            plusUtilises,
            peuSortis,
            pertes,
            familles);
    }

    /// <summary>Sortie sans type du club : « Piscine », « Baptêmes »... (pluriel quand il le faut, pour une liste de totaux).</summary>
    public static string LibelleType(TypeSortie type) => type switch
    {
        TypeSortie.Bapteme => "Baptêmes",
        TypeSortie.Autre => "Autres",
        _ => Libelles.De(type),
    };

    /// <summary>Nom de fichier proposé pour le rapport : bilan_saison_2025-2026.pdf.</summary>
    public static string NomFichierRapport(SaisonBilan saison) => $"bilan_saison_{saison.Libelle}.pdf";

    // ---------------------------------------------------------------- Calculs

    private static List<MoisBilan> Mois(List<PassageBilan> parSortie) => MoisSaison
        .Select(m => new MoisBilan(
            MoisCourts[m],
            new DateOnly(2000, m, 1).ToString("MMMM", Libelles.Culture),
            parSortie.Count(s => s.DateSortie.Month == m)))
        .Where(m => m.Sorties > 0 || (m.Libelle is not ("Juil" or "Août")))
        .ToList();

    private static string NoteMois(SaisonBilan saison, List<MoisBilan> mois)
    {
        if (saison.EnCours)
        {
            return "Saison en cours";
        }

        var pic = mois.MaxBy(m => m.Sorties);
        return pic is null || pic.Sorties == 0 ? "Aucune sortie" : $"Pic en {pic.LibelleLong} : {Libelles.Pluriel(pic.Sorties, "sortie")}";
    }

    private static string PrecisionSorties(SaisonBilan saison, List<PassageBilan> parSortie)
    {
        if (saison.EnCours)
        {
            return $"Depuis {saison.Debut.ToString("MMMM yyyy", Libelles.Culture)}";
        }

        if (parSortie.Count == 0)
        {
            return "Aucune sortie";
        }

        var premiere = parSortie.Min(s => s.DateSortie);
        var derniere = parSortie.Max(s => s.DateSortie);
        return $"{Majuscule(premiere.ToString("MMMM yyyy", Libelles.Culture))} à {derniere.ToString("MMMM yyyy", Libelles.Culture)}";
    }

    /// <summary>
    /// Noté manquant à la clôture : encore manquant (perdu), ou rendu après la clôture (retrouvé, dans sa sortie
    /// ou depuis une autre). Les perdus d'abord, puis les plus récents.
    /// </summary>
    private static List<PerteBilan> Pertes(List<PassageBilan> passages) => passages
        .Select(p => p.Statut == StatutSortieEquipement.Manquant
            ? new PerteBilan(p.Code, p.Libelle, p.Intitule, p.DateSortie, p.MoniteurNom, false, null)
            : p.ScanneRetourLe is { } r && p.ClotureeLe is { } c && r > c
                ? new PerteBilan(p.Code, p.Libelle, p.Intitule, p.DateSortie, p.MoniteurNom, true, DateOnly.FromDateTime(r.ToLocalTime()))
                : null)
        .OfType<PerteBilan>()
        .OrderBy(p => p.Retrouve)
        .ThenByDescending(p => p.DateSortie)
        .ThenBy(p => p.Code, StringComparer.Ordinal)
        .ToList();

    private static string PrecisionManquants(List<PerteBilan> pertes)
    {
        if (pertes.Count == 0)
        {
            return "Tout est rentré";
        }

        var retrouves = pertes.Count(p => p.Retrouve);
        var perdus = pertes.Count - retrouves;
        return string.Join(", ", new[]
        {
            retrouves > 0 ? Libelles.Pluriel(retrouves, "retrouvé") : null,
            perdus > 0 ? Libelles.Pluriel(perdus, "perdu") : null,
        }.OfType<string>());
    }

    /// <summary>Contrôles faits par famille : TIV, requalifications, puis la catégorie pour les révisions (Détendeurs, Gilets...).</summary>
    private static List<RevisionsBilan> Familles(IReadOnlyList<Controle> controles, IReadOnlyList<LigneInventaire> inventaire)
    {
        var categories = inventaire.ToDictionary(e => e.EquipementId, e => e.Categorie);
        return controles
            .GroupBy(c => c.Type switch
            {
                TypeControle.Tiv => "TIV",
                TypeControle.Requalification => "Requalifications",
                _ => categories.TryGetValue(c.EquipementId, out var categorie) && categorie.Length > 0 ? categorie : "Autres",
            })
            .Select(g => new RevisionsBilan(g.Key, g.Count()))
            .OrderByDescending(f => f.Nombre)
            .ThenBy(f => f.Libelle, StringComparer.Create(Libelles.Culture, true))
            .ToList();
    }

    /// <summary>
    /// Pour chaque groupe (catégorie et taille), le nombre d'équipements partis lors de la sortie la plus chargée,
    /// rapporté au stock actuel (hors matériel réformé). Rupture = sortie où tout le stock était parti.
    /// </summary>
    private static List<TensionBilan> Tension(List<PassageBilan> passages, IReadOnlyList<LigneInventaire> inventaire)
    {
        var stock = inventaire
            .Where(e => e.Etat != EtatEquipement.Reforme)
            .GroupBy(e => Groupe(e.Categorie, e.Taille, e.Code, e.Libelle))
            .ToDictionary(g => g.Key, g => g.Count());

        return passages
            .GroupBy(p => Groupe(p.Categorie, p.Taille, p.Code, p.Libelle))
            .Select(g =>
            {
                var parSortie = g.GroupBy(p => p.SortieId).Select(s => s.Select(p => p.EquipementId).Distinct().Count()).ToList();
                var pic = parSortie.Max();
                // Équipement réformé depuis : le stock de la saison était au moins ce qui est parti en même temps.
                var total = Math.Max(stock.GetValueOrDefault(g.Key), pic);
                return new TensionBilan(g.Key, total, pic, parSortie.Count(n => n >= total));
            })
            // Un équipement seul de son groupe serait toujours « en rupture » quand il sort : rien à en tirer.
            .Where(t => t.Stock >= StockMinTension)
            .OrderByDescending(t => t.Pourcentage)
            .ThenByDescending(t => t.Ruptures)
            .ThenBy(t => t.Libelle, StringComparer.Create(Libelles.Culture, true))
            .Take(TailleTension)
            .ToList();
    }

    /// <summary>
    /// « Gilets taille M », « Palmes taille 42-43 », « Détendeurs adultes », « Détendeurs enfants » : les détendeurs enfant
    /// (médaille E03, ou « enfant » dans le nom) ne remplacent pas un détendeur adulte, leur stock est compté à part.
    /// </summary>
    public static string Groupe(string categorie, string? taille, string? code = null, string? libelle = null)
    {
        var nom = EstDetendeur(categorie) ? $"{categorie} {(EstDetendeurEnfant(code, libelle) ? "enfants" : "adultes")}" : categorie;
        return string.IsNullOrWhiteSpace(taille) ? nom : $"{nom} taille {taille.Trim()}";
    }

    private static bool EstDetendeur(string categorie)
    {
        var c = categorie.Trim();
        return c.StartsWith("Détendeur", StringComparison.OrdinalIgnoreCase) || c.StartsWith("Detendeur", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Étiquette du club « E » + numéro (E03), ou « enfant » dans le nom (« Détendeur enfant »).</summary>
    public static bool EstDetendeurEnfant(string? code, string? libelle) =>
        System.Text.RegularExpressions.Regex.IsMatch((code ?? "").Trim(), "^E[0-9]+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        || (libelle ?? "").Contains("enfant", StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- Textes

    private static string Nombre(int n) => n.ToString("N0", Libelles.Culture);

    private static string Majuscule(string texte) =>
        texte.Length == 0 ? texte : char.ToUpper(texte[0], Libelles.Culture) + texte[1..];

    // « TIV » reste en majuscules ; « Détendeurs » → « détendeurs », ou « détendeur » pour un seul.
    private static string Minuscule(string texte, int nombre)
    {
        if (texte == "TIV")
        {
            return texte;
        }

        var minuscule = texte.ToLower(Libelles.Culture);
        return nombre == 1 && minuscule.EndsWith('s') ? minuscule[..^1] : minuscule;
    }
}
