namespace Asprenaut.Materiel.Core.Modeles;

// Modèles de lecture (projections) pour les écrans. Remplis par les dépôts.

/// <summary>Résumé d'une sortie pour la liste des sorties.</summary>
public sealed class SortieResume
{
    public long Id { get; set; }
    public DateOnly DateSortie { get; set; }
    /// <summary>Famille (pictogramme) du type.</summary>
    public TypeSortie Type { get; set; }

    public long? TypeId { get; set; }

    /// <summary>Utilisations comptées par équipement rentré.</summary>
    public int Utilisations { get; set; } = 1;

    /// <summary>Nom du type de sortie (table type_sortie) ; null si la sortie n'est reliée à aucun type.</summary>
    public string? TypeNom { get; set; }

    /// <summary>Nom affiché du type : celui du club, sinon celui de la famille.</summary>
    public string LibelleType => TypeNom is { Length: > 0 } nom ? nom : Libelles.De(Type);
    public string Intitule { get; set; } = "";
    public string Lieu { get; set; } = "";
    public StatutSortie Statut { get; set; }
    public long MoniteurId { get; set; }
    public string MoniteurNom { get; set; } = "";

    /// <summary>Le responsable est un moniteur ou une classe.</summary>
    public GenreResponsable MoniteurGenre { get; set; }

    /// <summary>« Moniteur : Pascal Durand » ou « Classe : Niveau 2 ».</summary>
    public string LibelleResponsable => $"{(MoniteurGenre == GenreResponsable.Classe ? "Classe" : "Moniteur")} : {MoniteurNom}";
    public DateTime? DepartValideLe { get; set; }
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
    public DateOnly? DateRequalification { get; set; }
    public StatutSortieEquipement Statut { get; set; }
    public DateTime ScanneDepartLe { get; set; }
    public DateTime? ScanneRetourLe { get; set; }

    /// <summary>Révision, TIV ou requalification dépassée.</summary>
    public bool RevisionDepassee(DateOnly aujourdHui) =>
        (DateProchaineRevision is { } r && r < aujourdHui) || (DateRequalification is { } q && q < aujourdHui);

    /// <summary>Échéance dépassée la plus ancienne (pour le message), ou null.</summary>
    public DateOnly? EcheanceDepassee(DateOnly aujourdHui) =>
        new[] { DateProchaineRevision, DateRequalification }.Where(d => d < aujourdHui).Min();
}

/// <summary>Carte d'un moniteur : sorties de la saison et matériel encore dehors sous sa responsabilité.</summary>
public sealed record FicheMoniteur(Moniteur Moniteur, int SortiesSaison, int NbDehors);

/// <summary>Sortie en cours d'un moniteur, avec le matériel encore dehors.</summary>
public sealed record SortieDehors(SortieResume Sortie, IReadOnlyList<LigneSortie> Dehors);

/// <summary>Fiche détaillée : sorties en cours avec matériel dehors, dernières sorties clôturées.</summary>
public sealed record DetailMoniteur(Moniteur Moniteur, IReadOnlyList<SortieDehors> EnCours, IReadOnlyList<SortieResume> Dernieres);

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
    public DateOnly? DateRequalification { get; set; }
    public long? SortieEnCoursId { get; set; }
    public string? SortieEnCoursIntitule { get; set; }
    public bool EstManquant { get; set; }

    // --- Fiche matériel (voir Equipement) ---
    public string Marque { get; set; } = "";
    public string Modele { get; set; } = "";
    public string NumeroSerie { get; set; } = "";
    public MontageDetendeur? Montage { get; set; }
    public string MarqueDeuxiemeEtage { get; set; } = "";
    public string ModeleDeuxiemeEtage { get; set; } = "";
    public string NumeroDeuxiemeEtage { get; set; } = "";
    public bool? DirectSystem { get; set; }
    public string Manometre { get; set; } = "";
    public string Octopus { get; set; } = "";
    public string NumeroOctopus { get; set; } = "";

    /// <summary>Date du dernier contrôle « révision » enregistré (colonne « Date de la dernière révision »).</summary>
    public DateOnly? DerniereRevision { get; set; }

    public StatutInventaire Statut => Etat switch
    {
        EtatEquipement.Reforme => StatutInventaire.Reforme,
        EtatEquipement.HorsService => StatutInventaire.HorsService,
        _ when SortieEnCoursId is not null => StatutInventaire.Dehors,
        _ when EstManquant => StatutInventaire.Manquant,
        _ => StatutInventaire.Disponible,
    };
}
