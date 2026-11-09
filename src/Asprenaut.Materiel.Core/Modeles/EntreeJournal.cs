namespace Asprenaut.Materiel.Core.Modeles;

/// <summary>Une ligne du journal : chaque scan et chaque changement y est tracé.</summary>
public sealed class EntreeJournal
{
    public long Id { get; set; }
    public DateTime Horodatage { get; set; }
    public string Action { get; set; } = "";
    public string Details { get; set; } = "";

    /// <summary>Équipement concerné (scan, contrôle, atelier...), ou null.</summary>
    public long? EquipementId { get; set; }

    /// <summary>Sortie concernée (création, scans, validation, clôture), ou null.</summary>
    public long? SortieId { get; set; }
}

/// <summary>Noms d'actions utilisés dans le journal.</summary>
public static class ActionsJournal
{
    public const string SortieCreee = "SortieCreee";
    public const string ScanDepart = "ScanDepart";
    public const string RetraitDepart = "RetraitDepart";
    public const string DepartValide = "DepartValide";
    public const string ScanRetour = "ScanRetour";
    public const string ManquantRetrouve = "ManquantRetrouve";
    public const string ManquantDeclare = "ManquantDeclare";
    public const string SortieCloturee = "SortieCloturee";
    public const string SortieModifiee = "SortieModifiee";
    public const string SortieSupprimee = "SortieSupprimee";
    public const string EquipementCree = "EquipementCree";
    public const string EquipementModifie = "EquipementModifie";
    public const string ControleEnregistre = "ControleEnregistre";
    public const string EnvoiAtelier = "EnvoiAtelier";
    public const string RetourAtelier = "RetourAtelier";
    public const string SeanceTivPlanifiee = "SeanceTivPlanifiee";
    public const string MoniteurCree = "MoniteurCree";
    public const string MoniteurModifie = "MoniteurModifie";
}
