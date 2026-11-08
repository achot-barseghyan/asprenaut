namespace Asprenaut.Materiel.Core.Modeles;

// Modèles de lecture (projections) pour les écrans. Remplis par les dépôts.

/// <summary>Résumé d'une sortie pour la liste des sorties.</summary>
public sealed class SortieResume
{
    public long Id { get; set; }
    public DateOnly DateSortie { get; set; }
    public TypeSortie Type { get; set; }
    public string Intitule { get; set; } = "";
    public string Lieu { get; set; } = "";
    public StatutSortie Statut { get; set; }
    public string MoniteurNom { get; set; } = "";
    public DateTime? ClotureeLe { get; set; }
    public int NbTotal { get; set; }
    public int NbRendus { get; set; }
    public int NbManquants { get; set; }

    public int NbDehors => NbTotal - NbRendus - NbManquants;

    /// <summary>Part rendue, entre 0 et 1.</summary>
    public double Progression => NbTotal == 0 ? 0 : (double)NbRendus / NbTotal;
}

/// <summary>Un équipement dans une sortie, avec ses informations d'affichage.</summary>
public sealed class LigneSortie
{
    public long LigneId { get; set; }
    public long SortieId { get; set; }
    public long EquipementId { get; set; }
    public string Code { get; set; } = "";
    public string Libelle { get; set; } = "";
    public string Categorie { get; set; } = "";
    public string? Taille { get; set; }
    public DateOnly? DateProchaineRevision { get; set; }
    public StatutSortieEquipement Statut { get; set; }
    public DateTime ScanneDepartLe { get; set; }
    public DateTime? ScanneRetourLe { get; set; }

    public bool RevisionDepassee(DateOnly aujourdHui) =>
        DateProchaineRevision is { } date && date < aujourdHui;
}

/// <summary>Une ligne du tableau d'inventaire.</summary>
public sealed class LigneInventaire
{
    public long EquipementId { get; set; }
    public string Code { get; set; } = "";
    public string Libelle { get; set; } = "";
    public long CategorieId { get; set; }
    public string Categorie { get; set; } = "";
    public string? Taille { get; set; }
    public EtatEquipement Etat { get; set; }
    public DateOnly? DateProchaineRevision { get; set; }
    public long? SortieEnCoursId { get; set; }
    public string? SortieEnCoursIntitule { get; set; }
    public bool EstManquant { get; set; }

    public StatutInventaire Statut => Etat switch
    {
        EtatEquipement.Reforme => StatutInventaire.Reforme,
        EtatEquipement.HorsService => StatutInventaire.HorsService,
        _ when SortieEnCoursId is not null => StatutInventaire.Dehors,
        _ when EstManquant => StatutInventaire.Manquant,
        _ => StatutInventaire.Disponible,
    };
}
