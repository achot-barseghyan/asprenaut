namespace Asprenaut.Materiel.Core.Modeles;

/// <summary>Groupe de filtres de l'inventaire (panneau « Filtres ») : on peut cocher plusieurs valeurs par groupe.</summary>
public enum FacetteInventaire
{
    Statut,
    Revision,
    Usure,
    Marque,
    Taille,
}

/// <summary>Prochaine échéance (révision, TIV ou requalification) vue par le filtre « Révision ».</summary>
public enum FiltreRevision
{
    Depassee,

    /// <summary>Dans les <see cref="Reglages.JoursAvantRevision"/> jours.</summary>
    Proche,
    AJour,

    /// <summary>Aucune date (ou équipement réformé).</summary>
    SansDate,
}

/// <summary>Compteur d'utilisation vu par le filtre « Utilisation ».</summary>
public enum FiltreUsure
{
    SeuilAtteint,

    /// <summary>90 % du seuil (réglage « Prévenir à 90 % »).</summary>
    PresDuSeuil,
    Normale,
    SansSeuil,
}

/// <summary>Colonne de tri du tableau de l'inventaire.</summary>
public enum TriInventaire
{
    Code,
    Equipement,
    Marque,
    Taille,
    Statut,
    Utilisation,
    Revision,
}

/// <summary>
/// Filtres et tri de l'inventaire : catégorie (tuiles), valeurs cochées par groupe (panneau « Filtres », raccourcis), tri.
/// Les valeurs sont des textes : nom de l'énumération (Statut, Révision, Utilisation), marque ou taille telle qu'écrite
/// (vide = « Sans marque » / « Sans taille »), comparées sans les majuscules.
/// </summary>
public sealed record FiltreInventaire
{
    private static readonly IReadOnlyDictionary<FacetteInventaire, IReadOnlySet<string>> Aucun = new Dictionary<FacetteInventaire, IReadOnlySet<string>>();
    private static readonly IReadOnlySet<string> Vide = new HashSet<string>();

    public long? CategorieId { get; init; }

    public IReadOnlyDictionary<FacetteInventaire, IReadOnlySet<string>> Choix { get; init; } = Aucun;

    public TriInventaire Tri { get; init; } = TriInventaire.Code;

    public bool Descendant { get; init; }

    /// <summary>Valeurs cochées d'un groupe (vide = pas de filtre sur ce groupe).</summary>
    public IReadOnlySet<string> De(FacetteInventaire facette) => Choix.TryGetValue(facette, out var v) ? v : Vide;

    public bool Choisi(FacetteInventaire facette, string valeur) => De(facette).Contains(valeur);

    /// <summary>Nombre de valeurs cochées (pastille du bouton « Filtres »).</summary>
    public int NombreCriteres => Choix.Values.Sum(v => v.Count);

    /// <summary>Coche ou décoche une valeur.</summary>
    public FiltreInventaire Basculer(FacetteInventaire facette, string valeur)
    {
        var valeurs = new HashSet<string>(De(facette), StringComparer.OrdinalIgnoreCase);
        if (!valeurs.Remove(valeur))
        {
            valeurs.Add(valeur);
        }

        var choix = Choix.ToDictionary(c => c.Key, c => c.Value);
        if (valeurs.Count == 0)
        {
            choix.Remove(facette);
        }
        else
        {
            choix[facette] = valeurs;
        }

        return this with { Choix = choix };
    }

    /// <summary>Remplace toutes les valeurs cochées (raccourci) ; catégorie et tri gardés.</summary>
    public FiltreInventaire AvecChoix(IReadOnlyDictionary<FacetteInventaire, IReadOnlySet<string>> choix) =>
        this with { Choix = choix.Where(c => c.Value.Count > 0).ToDictionary(c => c.Key, c => (IReadOnlySet<string>)new HashSet<string>(c.Value, StringComparer.OrdinalIgnoreCase)) };

    /// <summary>« Tout effacer » : ni catégorie ni valeur cochée ; le tri est gardé.</summary>
    public FiltreInventaire Effacer() => new() { Tri = Tri, Descendant = Descendant };

    /// <summary>Clic sur un en-tête : même colonne = sens inversé ; autre colonne = utilisations du plus grand, le reste croissant.</summary>
    public FiltreInventaire TrierPar(TriInventaire tri) =>
        this with { Tri = tri, Descendant = tri == Tri ? !Descendant : tri == TriInventaire.Utilisation };

    /// <summary>Mêmes valeurs cochées (un raccourci est « allumé » quand le filtre est exactement le sien).</summary>
    public bool MemesChoix(IReadOnlyDictionary<FacetteInventaire, IReadOnlySet<string>> choix) =>
        Enum.GetValues<FacetteInventaire>().All(f =>
        {
            var a = De(f);
            var b = choix.TryGetValue(f, out var v) ? v : Vide;
            return a.Count == b.Count && a.All(x => b.Contains(x, StringComparer.OrdinalIgnoreCase));
        });
}

/// <summary>Raccourci : une combinaison fréquente de filtres (« Révision dépassée »), avec le tri qui va avec.</summary>
public sealed record RaccourciInventaire(string Cle, string Libelle, IReadOnlyDictionary<FacetteInventaire, IReadOnlySet<string>> Choix, TriInventaire? Tri = null, bool Descendant = false);

/// <summary>Un tri proposé dans la liste « Trier par ».</summary>
public sealed record ChoixTriInventaire(string Libelle, TriInventaire Tri, bool Descendant);

/// <summary>Une valeur d'un groupe du panneau : « Hors service 4 », cochée ou non.</summary>
public sealed record OptionFiltreInventaire(string Valeur, string Libelle, int Nombre, bool Choisi);

/// <summary>Un groupe du panneau « Filtres » : titre et valeurs proposées, avec le nombre d'équipements de chacune.</summary>
public sealed record GroupeFiltreInventaire(FacetteInventaire Facette, string Titre, IReadOnlyList<OptionFiltreInventaire> Options);

/// <summary>Raccourci affiché : nombre d'équipements qu'il donnerait, allumé s'il est le filtre en cours.</summary>
public sealed record RaccourciAffiche(RaccourciInventaire Raccourci, int Nombre, bool Actif);

/// <summary>Filtre en cours, sous la barre : « Statut : Hors service » (clic = retirer).</summary>
public sealed record EtiquetteFiltre(FacetteInventaire Facette, string Valeur, string Texte);

/// <summary>
/// Inventaire filtré et trié, avec les nombres de chaque tuile, raccourci et valeur du panneau.
/// Chaque nombre tient compte des autres filtres, mais pas du sien (on voit ce qu'on obtiendrait en le choisissant).
/// </summary>
public sealed record VueInventaire(
    IReadOnlyList<LigneInventaire> Lignes,
    int RevisionsDepassees,
    int TotalCategories,
    IReadOnlyDictionary<long, int> ParCategorie,
    IReadOnlyList<GroupeFiltreInventaire> Groupes,
    IReadOnlyList<RaccourciAffiche> Raccourcis,
    IReadOnlyList<EtiquetteFiltre> Etiquettes);
