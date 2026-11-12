namespace Asprenaut.Materiel.Core.Modeles;

/// <summary>Une sortie du club (cours piscine, fosse, mer...) avec le matériel qui part.</summary>
public sealed class Sortie
{
    public long Id { get; set; }
    public DateOnly DateSortie { get; set; }
    public TypeSortie Type { get; set; }

    /// <summary>Type de sortie choisi (table type_sortie) ; null pour une sortie d'avant la migration 004 non reliée.</summary>
    public long? TypeId { get; set; }

    /// <summary>Utilisations ajoutées au compteur de chaque équipement rentré (défaut : celles du type ; 2 plongées dans la journée : 2).</summary>
    public int Utilisations { get; set; } = 1;
    public string Intitule { get; set; } = "";
    public long MoniteurId { get; set; }
    public string Lieu { get; set; } = "";
    public string Notes { get; set; } = "";
    public StatutSortie Statut { get; set; } = StatutSortie.EnPreparation;
    public DateTime? DepartValideLe { get; set; }
    public DateTime? ClotureeLe { get; set; }

    /// <summary>
    /// Sortie nominative : chaque équipement qui part est attribué à un adhérent. Repris du type à la création
    /// (ou au changement de type en préparation) : changer le réglage du type ensuite ne touche pas la sortie.
    /// </summary>
    public bool Nominatif { get; set; }
}
