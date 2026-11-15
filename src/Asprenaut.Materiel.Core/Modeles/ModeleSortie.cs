namespace Asprenaut.Materiel.Core.Modeles;

/// <summary>
/// Modèle de sortie (table modele_sortie, migration 024) : une sortie qui revient toujours pareil (cours du lundi, fosse du
/// jeudi…), créée ensuite en un clic depuis l'écran Sorties. Jour habituel facultatif ; intitulé vide = proposé
/// (<see cref="Services.ServiceSortie.IntitulePropose"/>) ; utilisations null = celles du type.
/// </summary>
public sealed class ModeleSortie
{
    public const int LongueurMaxNom = 40;
    public const int LongueurMaxIntitule = 60;
    public const int LongueurMaxLieu = 80;
    public const int LongueurMaxNotes = 400;

    public long Id { get; set; }
    public string Nom { get; set; } = "";

    /// <summary>Jour habituel : le modèle passe en premier ce jour-là et la sortie est datée de ce jour (le prochain).</summary>
    public DayOfWeek? Jour { get; set; }

    /// <summary>Type de sortie ; null si le type a été supprimé depuis (à choisir de nouveau).</summary>
    public long? TypeId { get; set; }
    public string Lieu { get; set; } = "";
    public string Intitule { get; set; } = "";

    /// <summary>Moniteur ou classe responsable ; null si à choisir.</summary>
    public long? MoniteurId { get; set; }

    /// <summary>Utilisations différentes de celles du type (réglées en mode gestion) ; null = celles du type.</summary>
    public int? Utilisations { get; set; }

    /// <summary>Un équipement par catégorie et par adhérent (sorties nominatives) ; le couper a demandé le mode gestion.</summary>
    public bool UnParCategorie { get; set; } = true;
    public string Notes { get; set; } = "";
}
