using System.Globalization;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>Filtres, raccourcis et tri de l'écran Inventaire (logique pure, sur les lignes déjà lues).</summary>
public sealed partial class ServiceInventaire
{
    private static readonly string[] OrdreTailles = ["XXS", "XS", "S", "M", "L", "XL", "XXL", "XXXL", "ENFANT", "JUNIOR", "ADULTE"];

    private static readonly StatutInventaire[] OrdreStatuts =
        [StatutInventaire.HorsService, StatutInventaire.Manquant, StatutInventaire.Dehors, StatutInventaire.Disponible, StatutInventaire.Reforme];

    /// <summary>Prochaine échéance : révision (ou TIV) ou requalification, la plus proche.</summary>
    public static DateOnly? ProchaineEcheance(LigneInventaire l) =>
        new[] { l.DateProchaineRevision, l.DateRequalification }.Where(d => d is not null).Min();

    /// <summary>Filtre « Révision » d'une ligne ; un équipement réformé n'a plus d'échéance.</summary>
    public FiltreRevision RevisionDe(LigneInventaire l) =>
        l.Etat == EtatEquipement.Reforme || ProchaineEcheance(l) is not { } date
            ? FiltreRevision.SansDate
            : EtatDeRevision(date) switch
            {
                EtatRevision.Depassee => FiltreRevision.Depassee,
                EtatRevision.Proche => FiltreRevision.Proche,
                _ => FiltreRevision.AJour,
            };

    /// <summary>Filtre « Utilisation » d'une ligne (sans compteur ni seuil : « Sans seuil »).</summary>
    public static FiltreUsure UsureDe(UtilisationInventaire? u) => u switch
    {
        null or { Seuil: null } => FiltreUsure.SansSeuil,
        { Etat: EtatCompteur.Atteint } => FiltreUsure.SeuilAtteint,
        { Etat: EtatCompteur.Proche } => FiltreUsure.PresDuSeuil,
        _ => FiltreUsure.Normale,
    };

    /// <summary>Libellé d'une valeur de filtre : « Dépassée », « Dans 30 jours », « Sans marque »…</summary>
    public string LibelleFiltre(FacetteInventaire facette, string valeur) => facette switch
    {
        FacetteInventaire.Statut when Enum.TryParse<StatutInventaire>(valeur, out var s) => Libelles.De(s),
        FacetteInventaire.Revision when Enum.TryParse<FiltreRevision>(valeur, out var r) => r switch
        {
            FiltreRevision.Depassee => "Dépassée",
            FiltreRevision.Proche => $"Dans {reglages.JoursAvantRevision} jours",
            FiltreRevision.AJour => "À jour",
            _ => "Sans date",
        },
        FacetteInventaire.Usure when Enum.TryParse<FiltreUsure>(valeur, out var u) => u switch
        {
            FiltreUsure.SeuilAtteint => "Seuil atteint",
            FiltreUsure.PresDuSeuil => "90 % du seuil",
            FiltreUsure.Normale => "Normale",
            _ => "Sans seuil",
        },
        FacetteInventaire.Marque when valeur.Length == 0 => "Sans marque",
        FacetteInventaire.Taille when valeur.Length == 0 => "Sans taille",
        _ => valeur,
    };

    public static string TitreFacette(FacetteInventaire facette) => facette switch
    {
        FacetteInventaire.Statut => "Statut",
        FacetteInventaire.Revision => "Révision",
        FacetteInventaire.Usure => "Utilisation",
        FacetteInventaire.Marque => "Marque",
        _ => "Taille",
    };

    /// <summary>Raccourcis de la maquette : combinaisons fréquentes, chacune avec son tri.</summary>
    public IReadOnlyList<RaccourciInventaire> Raccourcis =>
    [
        new("revision-depassee", "Révision dépassée", Un(FacetteInventaire.Revision, nameof(FiltreRevision.Depassee)), TriInventaire.Revision),
        new("revision-proche", $"À réviser sous {reglages.JoursAvantRevision} j", Un(FacetteInventaire.Revision, nameof(FiltreRevision.Proche)), TriInventaire.Revision),
        new("hors-service", "Hors service", Un(FacetteInventaire.Statut, nameof(StatutInventaire.HorsService))),
        new("dehors", "En sortie", Un(FacetteInventaire.Statut, nameof(StatutInventaire.Dehors))),
        new("usure", "Usure ≥ 90 %", Un(FacetteInventaire.Usure, nameof(FiltreUsure.PresDuSeuil), nameof(FiltreUsure.SeuilAtteint)), TriInventaire.Utilisation, Descendant: true),
    ];

    /// <summary>Liste « Trier par ».</summary>
    public static IReadOnlyList<ChoixTriInventaire> ChoixTris { get; } =
    [
        new("Code (A → Z)", TriInventaire.Code, false),
        new("Révision la plus urgente", TriInventaire.Revision, false),
        new("Les plus utilisés", TriInventaire.Utilisation, true),
        new("Les moins utilisés", TriInventaire.Utilisation, false),
        new("Statut", TriInventaire.Statut, false),
        new("Marque", TriInventaire.Marque, false),
        new("Équipement", TriInventaire.Equipement, false),
    ];

    /// <summary>
    /// Clic sur un raccourci : il remplace les filtres cochés (catégorie gardée) et choisit son tri ;
    /// déjà allumé, il les retire.
    /// </summary>
    public static FiltreInventaire AppliquerRaccourci(FiltreInventaire filtre, RaccourciInventaire raccourci)
    {
        if (filtre.MemesChoix(raccourci.Choix))
        {
            return filtre.AvecChoix(new Dictionary<FacetteInventaire, IReadOnlySet<string>>());
        }

        var nouveau = filtre.AvecChoix(raccourci.Choix);
        return raccourci.Tri is { } tri ? nouveau with { Tri = tri, Descendant = raccourci.Descendant } : nouveau;
    }

    /// <summary>
    /// Filtre et trie les lignes (déjà limitées par la recherche) et compte ce que donnerait chaque tuile, raccourci
    /// et valeur du panneau.
    /// </summary>
    public VueInventaire Filtrer(IReadOnlyList<LigneInventaire> lignes, IReadOnlyDictionary<long, UtilisationInventaire> utilisations, FiltreInventaire filtre)
    {
        var valeurs = lignes.ToDictionary(l => l.EquipementId, l => ValeursDe(l, utilisations.GetValueOrDefault(l.EquipementId)));
        bool Passe(LigneInventaire l, FacetteInventaire? sauf, bool categorie, FiltreInventaire f) =>
            (!categorie || f.CategorieId is null || l.CategorieId == f.CategorieId)
            && Enum.GetValues<FacetteInventaire>().All(k => k == sauf || f.De(k).Count == 0 || f.De(k).Contains(valeurs[l.EquipementId][k]));

        var retenues = lignes.Where(l => Passe(l, null, true, filtre)).ToList();
        retenues.Sort((a, b) => Comparer(a, b, filtre, utilisations));

        // Tuiles : tous les filtres sauf la catégorie.
        var horsCategorie = lignes.Where(l => Passe(l, null, false, filtre)).ToList();
        var parCategorie = horsCategorie.GroupBy(l => l.CategorieId).ToDictionary(g => g.Key, g => g.Count());

        var groupes = Enum.GetValues<FacetteInventaire>().Select(facette =>
        {
            var base_ = lignes.Where(l => Passe(l, facette, true, filtre)).ToList();
            var proposees = ValeursProposees(facette, lignes, valeurs, filtre);
            var options = proposees.Select(v => new OptionFiltreInventaire(
                v,
                LibelleFiltre(facette, v),
                base_.Count(l => string.Equals(valeurs[l.EquipementId][facette], v, StringComparison.OrdinalIgnoreCase)),
                filtre.Choisi(facette, v))).ToList();
            return new GroupeFiltreInventaire(facette, TitreFacette(facette), options);
        }).ToList();

        // Raccourcis : la catégorie et la recherche, puis les seules valeurs du raccourci.
        var raccourcis = Raccourcis.Select(r =>
        {
            var f = filtre.AvecChoix(r.Choix);
            return new RaccourciAffiche(r, lignes.Count(l => Passe(l, null, true, f)), filtre.MemesChoix(r.Choix));
        }).ToList();

        var etiquettes = Enum.GetValues<FacetteInventaire>()
            .SelectMany(f => filtre.De(f).Select(v => new EtiquetteFiltre(f, v, $"{TitreFacette(f)} : {LibelleFiltre(f, v)}")))
            .ToList();

        return new VueInventaire(
            retenues,
            retenues.Count(l => valeurs[l.EquipementId][FacetteInventaire.Revision] == nameof(FiltreRevision.Depassee)),
            horsCategorie.Count,
            parCategorie,
            groupes,
            raccourcis,
            etiquettes);
    }

    private Dictionary<FacetteInventaire, string> ValeursDe(LigneInventaire l, UtilisationInventaire? u) => new()
    {
        [FacetteInventaire.Statut] = l.Statut.ToString(),
        [FacetteInventaire.Revision] = RevisionDe(l).ToString(),
        [FacetteInventaire.Usure] = UsureDe(u).ToString(),
        [FacetteInventaire.Marque] = (l.Marque ?? "").Trim(),
        [FacetteInventaire.Taille] = (l.Taille ?? "").Trim(),
    };

    /// <summary>
    /// Valeurs du panneau : toutes celles de l'énumération pour Statut, Révision et Utilisation (Réformé seulement s'il y en a) ;
    /// pour Marque et Taille, celles de l'inventaire (une seule écriture par valeur, sans les majuscules) et les valeurs cochées.
    /// </summary>
    private static List<string> ValeursProposees(FacetteInventaire facette, IReadOnlyList<LigneInventaire> lignes,
        Dictionary<long, Dictionary<FacetteInventaire, string>> valeurs, FiltreInventaire filtre)
    {
        var presentes = lignes.Select(l => valeurs[l.EquipementId][facette]).Concat(filtre.De(facette))
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.GroupBy(v => v).OrderByDescending(x => x.Count()).First().Key) // L'écriture la plus fréquente.
            .ToList();
        switch (facette)
        {
            case FacetteInventaire.Statut:
                return OrdreStatuts.Where(s => s != StatutInventaire.Reforme || presentes.Contains(s.ToString()))
                    .Select(s => s.ToString()).ToList();
            case FacetteInventaire.Revision:
                return Enum.GetNames<FiltreRevision>().ToList();
            case FacetteInventaire.Usure:
                return Enum.GetNames<FiltreUsure>().ToList();
            case FacetteInventaire.Taille:
                return presentes.OrderBy(RangTaille).ThenBy(v => v, ComparateurNaturel.Instance).ToList();
            default:
                return presentes.OrderBy(v => v.Length == 0 ? 1 : 0).ThenBy(v => v, ComparateurNaturel.Instance).ToList();
        }
    }

    private static int RangTaille(string taille)
    {
        if (taille.Length == 0)
        {
            return 999;
        }

        var i = Array.IndexOf(OrdreTailles, taille.ToUpperInvariant());
        return i < 0 ? 500 : i;
    }

    /// <summary>
    /// Tri : codes dans l'ordre naturel (A2 avant A10) ; échéance la plus proche d'abord, sans date à la fin ;
    /// utilisations en part du seuil ; statut du plus urgent ; textes de A à Z, vides à la fin. À égalité : le code.
    /// </summary>
    private int Comparer(LigneInventaire a, LigneInventaire b, FiltreInventaire filtre, IReadOnlyDictionary<long, UtilisationInventaire> utilisations)
    {
        var sens = filtre.Descendant ? -1 : 1;
        var code = ComparateurNaturel.Instance.Compare(a.Code, b.Code);
        int Textes(string? x, string? y)
        {
            var vx = (x ?? "").Trim();
            var vy = (y ?? "").Trim();
            if (vx.Length == 0 || vy.Length == 0)
            {
                return vx.Length == vy.Length ? 0 : vx.Length == 0 ? 1 : -1; // Vides à la fin, dans les deux sens.
            }

            return sens * ComparateurNaturel.Instance.Compare(vx, vy);
        }

        double Usure(LigneInventaire l) => utilisations.TryGetValue(l.EquipementId, out var u)
            ? u.Seuil is > 0 and var s ? u.Utilisations / (double)s : u.Utilisations / 100_000d
            : -1;

        var resultat = filtre.Tri switch
        {
            TriInventaire.Code => sens * code,
            TriInventaire.Equipement => Textes(a.Libelle, b.Libelle),
            TriInventaire.Marque => Textes(a.Marque, b.Marque),
            TriInventaire.Taille => RangTaille((a.Taille ?? "").Trim()) is var ra && RangTaille((b.Taille ?? "").Trim()) is var rb && ra != rb
                ? (ra == 999 || rb == 999 ? ra.CompareTo(rb) : sens * ra.CompareTo(rb))
                : Textes(a.Taille, b.Taille),
            TriInventaire.Statut => sens * Array.IndexOf(OrdreStatuts, a.Statut).CompareTo(Array.IndexOf(OrdreStatuts, b.Statut)),
            TriInventaire.Utilisation => sens * Usure(a).CompareTo(Usure(b)),
            TriInventaire.Revision => (Echeance(a), Echeance(b)) switch
            {
                (null, null) => 0,
                (null, _) => 1,
                (_, null) => -1,
                var (x, y) => sens * x.Value.CompareTo(y.Value),
            },
            _ => 0,
        };
        return resultat != 0 ? resultat : code;
    }

    private static DateOnly? Echeance(LigneInventaire l) => l.Etat == EtatEquipement.Reforme ? null : ProchaineEcheance(l);

    private static IReadOnlyDictionary<FacetteInventaire, IReadOnlySet<string>> Un(FacetteInventaire facette, params string[] valeurs) =>
        new Dictionary<FacetteInventaire, IReadOnlySet<string>> { [facette] = new HashSet<string>(valeurs, StringComparer.OrdinalIgnoreCase) };

    /// <summary>Comparaison « naturelle » en français, sans les majuscules ni les accents : A2 avant A10.</summary>
    private sealed class ComparateurNaturel : IComparer<string>
    {
        public static readonly ComparateurNaturel Instance = new();

        private static readonly CompareInfo Francais = CultureInfo.GetCultureInfo("fr-FR").CompareInfo;

        public int Compare(string? x, string? y) =>
            Francais.Compare(x, y, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.NumericOrdering);
    }
}
