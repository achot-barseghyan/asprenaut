namespace Asprenaut.Materiel.Core.Modeles;

/// <summary>Colonne « Retour » d'une ligne de la page « Utilisations » : rendu, rendu avec un problème, encore dehors, manquant.</summary>
public enum EtatRetourUtilisation
{
    Rendu,
    Signale,
    Dehors,
    Manquant,
}

/// <summary>
/// Une ligne de la page « Utilisations d'un équipement » : une sortie (passage de l'équipement), ou un contrôle de
/// l'équipement complet qui remet le compteur à zéro (<see cref="EstControle"/>).
/// </summary>
public sealed record LigneUtilisation
{
    public required bool EstControle { get; init; }

    /// <summary>Date de la sortie, ou du contrôle.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>Intitulé de la sortie, ou « Révision · Atelier Dupont ».</summary>
    public required string Titre { get; init; }

    public long? SortieId { get; init; }

    public TypeSortie Famille { get; init; }

    /// <summary>Nom du type de sortie (« Piscine »), le filtre de la page.</summary>
    public string TypeNom { get; init; } = "";

    /// <summary>« Classe Niveau 1 », ou le nom du moniteur.</summary>
    public string Responsable { get; init; } = "";

    /// <summary>« 20:08 · 22:01 », « 20:12 · en cours », « 06:30 · 27/09 21:40 ».</summary>
    public string Horaires { get; init; } = "";

    /// <summary>Utilisations de la sortie (comptées au retour scanné).</summary>
    public int Utilisations { get; init; }

    /// <summary>Le nombre de la sortie n'est pas celui de son type (modifié à la création).</summary>
    public bool Modifiees { get; init; }

    /// <summary>Le retour a été scanné : les utilisations sont dans le compteur.</summary>
    public bool Comptee { get; init; }

    /// <summary>Compteur juste après ce retour (depuis le dernier contrôle) ; null si pas encore compté.</summary>
    public int? Compteur { get; init; }

    public EtatRetourUtilisation Retour { get; init; }

    /// <summary>« Rendu », « Rendu · À surveiller · Débit continu », « Sorti », « Manquant ».</summary>
    public string LibelleRetour { get; init; } = "";
}

/// <summary>
/// Page « Utilisations d'un équipement » : compteur depuis le dernier contrôle, chiffres clés, et toutes ses sorties
/// (de la plus récente), avec les contrôles qui ont remis le compteur à zéro.
/// </summary>
public sealed record UtilisationsEquipement(
    Equipement Equipement,
    string Categorie,
    string SousTitre,
    int Compteur,
    int? Seuil,
    OrigineSeuil Origine,
    EtatCompteur Etat,
    int SortiesDepuisControle,
    string DernierControle,
    int Total,
    string DetailTotal,
    int Problemes,
    IReadOnlyList<LigneUtilisation> Lignes)
{
    /// <summary>Lignes de sortie seulement.</summary>
    public IEnumerable<LigneUtilisation> Sorties => Lignes.Where(l => !l.EstControle);

    /// <summary>Types de sortie rencontrés (filtre), dans l'ordre des familles, avec leur nombre de sorties.</summary>
    public IReadOnlyList<(string Type, TypeSortie Famille, int Nombre)> Types =>
        Sorties.GroupBy(l => l.TypeNom)
            .Select(g => (g.Key, g.First().Famille, g.Count()))
            .OrderBy(t => t.Item2).ThenBy(t => t.Key, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Lignes d'un type de sortie (null = toutes) ; les contrôles restent pour expliquer les remises à zéro.</summary>
    public IReadOnlyList<LigneUtilisation> LignesDe(string? type) =>
        type is null ? Lignes : Lignes.Where(l => l.EstControle || l.TypeNom == type).ToList();

    /// <summary>Reste avant le seuil (0 s'il est atteint), null sans seuil.</summary>
    public int? Reste => Seuil is { } s ? Math.Max(0, s - Compteur) : null;
}
