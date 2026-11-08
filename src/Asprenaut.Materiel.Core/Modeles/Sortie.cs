namespace Asprenaut.Materiel.Core.Modeles;

/// <summary>Une sortie du club (cours piscine, fosse, mer...) avec le matériel qui part.</summary>
public sealed class Sortie
{
    public long Id { get; set; }
    public DateOnly DateSortie { get; set; }
    public TypeSortie Type { get; set; }
    public string Intitule { get; set; } = "";
    public long MoniteurId { get; set; }
    public string Lieu { get; set; } = "";
    public string Notes { get; set; } = "";
    public StatutSortie Statut { get; set; } = StatutSortie.EnPreparation;
    public DateTime? DepartValideLe { get; set; }
    public DateTime? ClotureeLe { get; set; }
}
