namespace Asprenaut.Materiel.Core.Modeles;

/// <summary>
/// Adhérent du club (table adherent, migration 014) : il prend du matériel lors d'une sortie nominative.
/// Jamais responsable d'une sortie, jamais supprimé (<see cref="Actif"/> faux : plus proposé au départ, gardé dans l'historique).
/// </summary>
public sealed class Adherent
{
    public const int LongueurMaxNom = 50;
    public const int LongueurMaxNiveau = 30;
    public const int LongueurMaxLicence = 30;
    public const int LongueurMaxTelephone = 30;

    public long Id { get; set; }
    public string Nom { get; set; } = "";
    public string Prenom { get; set; } = "";

    /// <summary>« Niveau 2 », « Moniteur »... ou null.</summary>
    public string? Niveau { get; set; }

    /// <summary>Numéro de licence FFESSM (unique s'il est renseigné), ou null.</summary>
    public string? Licence { get; set; }
    public string? Telephone { get; set; }
    public bool Actif { get; set; } = true;

    /// <summary>Certifié nitrox : peut prendre un bloc ou un détendeur nitrox.</summary>
    public bool Nitrox { get; set; }

    /// <summary>
    /// CACI suivi (certificat d'absence de contre-indication) : avec <see cref="CaciExpiration"/>, l'adhérent ne sort plus de
    /// matériel une fois la date passée. Faux : aucun contrôle.
    /// </summary>
    public bool Caci { get; set; }

    /// <summary>Dernier jour de validité du CACI (null si <see cref="Caci"/> est faux).</summary>
    public DateOnly? CaciExpiration { get; set; }

    /// <summary>CACI coché et date d'expiration passée (le jour même est encore valable).</summary>
    public bool CaciExpire(DateOnly aujourdhui) => Caci && CaciExpiration is { } fin && fin < aujourdhui;

    /// <summary>« Julie Martin ».</summary>
    public string NomComplet => $"{Prenom} {Nom}".Trim();

    /// <summary>« JM » : pastille de l'adhérent.</summary>
    public string Initiales => Libelles.Initiales(NomComplet);

    public override string ToString() => NomComplet;
}

/// <summary>Matériel d'un adhérent : encore dehors, et jamais rendu (manquant à la clôture).</summary>
public sealed class SituationAdherent
{
    public long AdherentId { get; set; }
    public int NbDehors { get; set; }
    public int NbManquants { get; set; }
}

/// <summary>Un équipement pris par un adhérent lors d'une sortie nominative (sortie_equipement + sortie + équipement).</summary>
public sealed class PassageAdherent
{
    public long SortieId { get; set; }
    public string Intitule { get; set; } = "";
    public DateOnly DateSortie { get; set; }
    public string? TypeNom { get; set; }
    public TypeSortie TypeSortie { get; set; }
    public StatutSortie StatutSortie { get; set; }
    public long EquipementId { get; set; }
    public string Code { get; set; } = "";
    public string Libelle { get; set; } = "";
    public string Categorie { get; set; } = "";
    public StatutSortieEquipement Statut { get; set; }
    public DateTime ScanneDepartLe { get; set; }
    public DateTime? ScanneRetourLe { get; set; }
}

/// <summary>Filtres de l'onglet Adhérents.</summary>
public enum FiltreAdherents
{
    Tous,
    MaterielDehors,
    NonRendu,

    /// <summary>Certifiés nitrox.</summary>
    Nitrox,

    /// <summary>Désactivés (ne sortent plus de matériel).</summary>
    Desactives,
}

/// <summary>Carte d'un adhérent : matériel encore dehors et matériel jamais rendu.</summary>
public sealed record FicheAdherent(Adherent Adherent, int NbDehors, int NbNonRendus, bool CaciExpire = false);

/// <summary>Onglet Adhérents : les cartes filtrées et le nombre d'adhérents de chaque filtre (après recherche et niveau).</summary>
public sealed record ListeAdherents(IReadOnlyList<FicheAdherent> Fiches, int Tous, int MaterielDehors, int NonRendu, int Nitrox = 0, int Desactives = 0);

/// <summary>Une sortie nominative d'un adhérent : équipements pris, rendus, non rendus.</summary>
public sealed record SortieAdherent(long SortieId, DateOnly Date, string Intitule, string Type, int NbEquipements, int NbNonRendus, bool EnCours)
{
    /// <summary>« Tout rendu », « 1 non rendu », « En cours ».</summary>
    public string Etat => EnCours ? "En cours" : NbNonRendus == 0 ? "Tout rendu" : $"{NbNonRendus} non rendu{(NbNonRendus > 1 ? "s" : "")}";
}

/// <summary>Fiche d'un adhérent : matériel qu'il a en ce moment, matériel non rendu, dernières sorties nominatives.</summary>
public sealed record DetailAdherent(
    Adherent Adherent,
    IReadOnlyList<PassageAdherent> Dehors,
    IReadOnlyList<PassageAdherent> NonRendus,
    IReadOnlyList<SortieAdherent> Sorties,
    bool CaciExpire = false);

/// <summary>Adhérent récent d'une sortie (raccourcis du départ) : le plus récent d'abord.</summary>
public sealed record AdherentRecent(long AdherentId, string Nom, int NbEquipements);
