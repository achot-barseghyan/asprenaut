namespace Asprenaut.Materiel.Core.Modeles;

/// <summary>
/// Adhérent du club (table adherent, migration 014) : il prend du matériel lors d'une sortie nominative. Toute personne du
/// club est un adhérent ; un adhérent peut aussi être enseignant (<see cref="MoniteurId"/> : sa fiche de responsable de sortie,
/// migration 029). Jamais supprimé (<see cref="Actif"/> faux : plus proposé au départ, gardé dans l'historique).
/// </summary>
public sealed class Adherent
{
    public const int LongueurMaxNom = 50;
    public const int LongueurMaxNiveau = 30;
    public const int LongueurMaxLicence = 30;
    public const int LongueurMaxTelephone = 30;
    public const int LongueurMaxBrevet = 30;
    public const int NombreMaxBrevets = 10;

    public long Id { get; set; }
    public string Nom { get; set; } = "";
    public string Prenom { get; set; } = "";

    /// <summary>
    /// Formation en cours : la classe du club qu'il suit (Membres › Classes : « Niveau 2 » = il prépare le niveau 2, pas
    /// son brevet), ou null. Lien réel (adherent.classe_id, migration 021) ; voir <see cref="Niveau"/> pour son nom.
    /// </summary>
    public long? ClasseId { get; set; }

    /// <summary>Nom de la classe de <see cref="ClasseId"/> (« Niveau 2 », « Autonome »), pour l'affichage et la recherche ; ou null.</summary>
    public string? Niveau { get; set; }

    /// <summary>
    /// Brevet obtenu (adherent.niveau_obtenu, migration 031) : « Débutant » (aucun brevet), « Niveau 1 »… « Niveau 4 », ou null
    /// (non renseigné). Celui qui prépare le niveau 2 (<see cref="Niveau"/>) a obtenu le niveau 1. Un autre brevet (PA40) va
    /// dans <see cref="AutresBrevets"/> (migration 032).
    /// </summary>
    public string? NiveauObtenu { get; set; }

    /// <summary>
    /// Autres brevets (adherent.autres_brevets, migration 032) : « PA40; RIFAP », séparés par « ; », ou null. Le nitrox n'y est
    /// pas : c'est <see cref="Nitrox"/>. Lus et écrits par <see cref="Services.ServiceAdherents.ListeBrevets"/> / <c>JoindreBrevets</c>.
    /// </summary>
    public string? AutresBrevets { get; set; }

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

    /// <summary>
    /// Sa fiche d'encadrement (table moniteur : responsable des sorties s'il est enseignant, inspecteur des TIV), ou null s'il
    /// n'a jamais eu de rôle. Écrite seulement par <see cref="Abstractions.IAdherentRepository.RelierEnseignantAsync"/>, jamais
    /// par l'enregistrement de l'adhérent.
    /// </summary>
    public long? MoniteurId { get; set; }

    /// <summary>Enseignant en ce moment : fiche d'encadrement reliée et active (moniteur.actif, lu par jointure).</summary>
    public bool Enseignant { get; set; }

    /// <summary>Niveau d'encadrement de sa fiche d'encadrement (E1… MF2), ou null (lu par jointure).</summary>
    public string? NiveauEncadrement { get; set; }

    /// <summary>Inspecteur TIV (moniteur.inspecteur_tiv, lu par jointure) : enseignant ou non (migration 030).</summary>
    public bool InspecteurTiv { get; set; }

    /// <summary>CACI coché et date d'expiration passée (le jour même est encore valable).</summary>
    public bool CaciExpire(DateOnly aujourdhui) => Caci && CaciExpiration is { } fin && fin < aujourdhui;

    /// <summary>CACI coché, encore valable, mais qui expire dans les <paramref name="jours"/> jours (à renouveler bientôt).</summary>
    public bool CaciBientot(DateOnly aujourdhui, int jours) =>
        Caci && CaciExpiration is { } fin && fin >= aujourdhui && fin <= aujourdhui.AddDays(jours);

    /// <summary>« Julie Martin ».</summary>
    public string NomComplet => $"{Prenom} {Nom}".Trim();

    /// <summary>« JM » : pastille de l'adhérent.</summary>
    public string Initiales => Libelles.Initiales(NomComplet);

    public override string ToString() => NomComplet;
}

/// <summary>
/// Rôles au club saisis dans la fiche d'un adhérent (<see cref="Services.ServiceAdherents.EnregistrerAsync"/>), indépendants :
/// enseignant (responsable des sorties, avec son niveau d'encadrement ; coupé, le niveau est gardé) et inspecteur TIV.
/// </summary>
public sealed record RolesAdherent(bool Enseignant, string? NiveauEncadrement = null, bool InspecteurTiv = false);

/// <summary>
/// Enseignant sans fiche d'adhérent que <see cref="Services.ServiceAdherents.RelierEnseignantsAsync"/> n'a pas relié seul :
/// plusieurs adhérents du même nom, ou des noms proches (faute de frappe ?). À relier à la main parmi <see cref="Candidats"/>,
/// ou fiche d'adhérent à créer.
/// </summary>
public sealed record EnseignantARelier(Moniteur Enseignant, IReadOnlyList<Adherent> Candidats);

/// <summary>Bilan du rapprochement des enseignants et des adhérents : fiches reliées, fiches créées, cas laissés à trancher.</summary>
public sealed record BilanLiaison(int Relies, int Crees, IReadOnlyList<EnseignantARelier> ARelier);

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

    /// <summary>Enseignants en ce moment.</summary>
    Enseignants,

    /// <summary>Inspecteurs TIV (enseignants ou non).</summary>
    InspecteursTiv,

    MaterielDehors,
    NonRendu,

    /// <summary>Certifiés nitrox.</summary>
    Nitrox,

    /// <summary>Désactivés (ne sortent plus de matériel).</summary>
    Desactives,

    /// <summary>CACI expiré ou qui expire bientôt (à relancer).</summary>
    CaciARenouveler,
}

/// <summary>Carte d'un adhérent : matériel encore dehors et matériel jamais rendu.</summary>
public sealed record FicheAdherent(Adherent Adherent, int NbDehors, int NbNonRendus, bool CaciExpire = false, bool CaciBientot = false);

/// <summary>Onglet Adhérents : les cartes filtrées et le nombre d'adhérents de chaque filtre (après recherche et niveau).</summary>
public sealed record ListeAdherents(IReadOnlyList<FicheAdherent> Fiches, int Tous, int MaterielDehors, int NonRendu, int Nitrox = 0, int Desactives = 0, int CaciARenouveler = 0, int Enseignants = 0, int InspecteursTiv = 0);

/// <summary>Une sortie nominative d'un adhérent : équipements pris, rendus, non rendus.</summary>
public sealed record SortieAdherent(long SortieId, DateOnly Date, string Intitule, string Type, int NbEquipements, int NbNonRendus, bool EnCours,
    TypeSortie Famille = TypeSortie.Piscine, StatutSortie Statut = StatutSortie.Cloturee)
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
    bool CaciExpire = false,
    bool CaciBientot = false,
    int NbSorties = 0);

/// <summary>
/// Bandeau d'état de la fiche d'un membre (maquette « Fiche d'un membre ») : équipements dehors (les siens et, pour un
/// enseignant, ceux des sorties dont il est responsable, sans compter deux fois le même) et matériel non rendu.
/// </summary>
public sealed record BilanMembre(int Dehors, int NonRendus)
{
    public bool ToutRentre => Dehors == 0 && NonRendus == 0;

    /// <summary>« Tout est rentré », « 3 équipements dehors · 1 non rendu », « 1 non rendu ».</summary>
    public string Message => ToutRentre
        ? "Tout est rentré"
        : string.Join(" · ", new[]
        {
            Dehors > 0 ? Libelles.Pluriel(Dehors, "équipement") + " dehors" : null,
            NonRendus > 0 ? $"{NonRendus} non rendu{(NonRendus > 1 ? "s" : "")}" : null,
        }.Where(t => t is not null));
}

/// <summary>Adhérent récent d'une sortie (raccourcis du départ) : le plus récent d'abord.</summary>
public sealed record AdherentRecent(long AdherentId, string Nom, int NbEquipements);

/// <summary>État du CACI pendant la saisie (pastille du formulaire, étiquette de l'en-tête).</summary>
public enum NiveauCaci
{
    ASaisir,
    Valide,
    Bientot,
    Expire,
}

/// <summary>
/// <see cref="Services.ServiceAdherents.EtatCaciSaisi"/> : niveau, texte de la pastille (« Expire dans 17 j »),
/// étiquette de l'en-tête (« CACI bientôt »), null tant que la date manque.
/// </summary>
public sealed record EtatCaci(NiveauCaci Niveau, string Texte, string? Etiquette);
