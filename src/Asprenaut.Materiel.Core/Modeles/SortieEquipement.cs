namespace Asprenaut.Materiel.Core.Modeles;

/// <summary>Un équipement affecté à une sortie (ligne de la table sortie_equipement).</summary>
public sealed class SortieEquipement
{
    public long Id { get; set; }
    public long SortieId { get; set; }
    public long EquipementId { get; set; }
    public DateTime ScanneDepartLe { get; set; }
    public DateTime? ScanneRetourLe { get; set; }
    public StatutSortieEquipement Statut { get; set; } = StatutSortieEquipement.Dehors;
}
