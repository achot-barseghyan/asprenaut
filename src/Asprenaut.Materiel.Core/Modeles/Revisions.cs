namespace Asprenaut.Materiel.Core.Modeles;

// Révisions : contrôles périodiques (TIV, requalification, révision fabricant), atelier, séances TIV.
// Les valeurs des énumérations sont stockées en texte dans la base : ne jamais renommer un membre sans migration.

/// <summary>Type de contrôle périodique.</summary>
public enum TypeControle
{
    /// <summary>Inspection visuelle annuelle d'un bloc.</summary>
    Tiv,

    /// <summary>Requalification d'un bloc (épreuve hydraulique).</summary>
    Requalification,

    /// <summary>Révision ou contrôle des autres équipements (délai du fabricant).</summary>
    Revision,
}

/// <summary>Résultat d'un contrôle.</summary>
public enum ResultatControle
{
    Conforme,
    ASurveiller,
    Refuse,
}

/// <summary>Où en est une échéance de contrôle (calculé, non stocké).</summary>
public enum EtatEcheance
{
    EnRetard,
    CeMois,
    Bientot,
    AJour,
}

/// <summary>Un contrôle enregistré (historique, table controle).</summary>
public sealed class Controle
{
    public long Id { get; set; }
    public long EquipementId { get; set; }
    public TypeControle Type { get; set; }
    public DateOnly DateControle { get; set; }
    public ResultatControle Resultat { get; set; }
    public long? InspecteurId { get; set; }
    public string Notes { get; set; } = "";
    public DateTime SaisiLe { get; set; }

    /// <summary>Nature de l'intervention (« Révision atelier », « Réparation »...) ; vide = celle du type.</summary>
    public string Nature { get; set; } = "";

    /// <summary>Qui l'a faite : inspecteur, magasin, organisme (texte libre).</summary>
    public string Intervenant { get; set; } = "";

    /// <summary>Composant visé (« Premier étage », « Octopus »...) ; vide = l'équipement complet (migration 011).</summary>
    public string Composant { get; set; } = "";
}

/// <summary>Un passage en atelier (envoi chez un prestataire, puis retour).</summary>
public sealed class PassageAtelier
{
    public long Id { get; set; }
    public long EquipementId { get; set; }
    public string Prestataire { get; set; } = "";
    public string Motif { get; set; } = "";
    public DateOnly EnvoyeLe { get; set; }
    public DateOnly? RetourPrevu { get; set; }
    public DateOnly? RevenuLe { get; set; }
}

/// <summary>Une séance de TIV prévue au club.</summary>
public sealed class SeanceTiv
{
    public long Id { get; set; }
    public DateOnly DateSeance { get; set; }
    public string Heure { get; set; } = "";
    public string Lieu { get; set; } = "";
}

/// <summary>Date du dernier contrôle d'un type pour un équipement.</summary>
public sealed class DernierControle
{
    public long EquipementId { get; set; }
    public TypeControle Type { get; set; }
    public DateOnly DateControle { get; set; }
}

/// <summary>Une ligne de l'échéancier : un contrôle à venir pour un équipement.</summary>
public sealed record LigneEcheance(
    long EquipementId,
    string Code,
    string Libelle,
    string Categorie,
    TypeControle Type,
    string Controle,
    DateOnly? Dernier,
    DateOnly Echeance,
    EtatEcheance Etat,
    string? Usage = null)
{
    /// <summary>Ligne du compteur d'utilisation (« 92 / 100 sorties ») plutôt que d'une date.</summary>
    public bool ParUsage => Usage is not null;
}

/// <summary>Ce que l'écran affiche avant d'enregistrer un contrôle sur l'équipement scanné.</summary>
public sealed record PreparationControle(
    Equipement Equipement,
    string Categorie,
    TypeControle TypePropose,
    IReadOnlyList<TypeControle> TypesPossibles,
    DateOnly? DerniereTiv,
    DateOnly? DerniereRequalification,
    DateOnly? DerniereRevision);

/// <summary>Équipement actuellement en atelier, pour l'affichage.</summary>
public sealed record LigneAtelier(long PassageId, long EquipementId, string Code, string Libelle, string Prestataire, string Motif, DateOnly EnvoyeLe, DateOnly? RetourPrevu);

/// <summary>Prochaine séance TIV avec les inspecteurs et les blocs à préparer.</summary>
public sealed record ProchaineSeance(SeanceTiv Seance, IReadOnlyList<string> Inspecteurs, IReadOnlyList<LigneEcheance> BlocsAPreparer);

/// <summary>Un contrôle dans le registre : date, contrôle, résultat, inspecteur, notes.</summary>
public sealed record LigneRegistre(DateOnly Date, string Controle, string Resultat, ResultatControle ResultatBrut, string Inspecteur, string Notes);

/// <summary>Un équipement du registre et tous ses contrôles, du plus ancien au plus récent.</summary>
public sealed record FicheRegistre(
    string Code,
    string Libelle,
    string Categorie,
    EtatEquipement Etat,
    DateOnly? ProchaineRevision,
    DateOnly? ProchaineRequalification,
    IReadOnlyList<LigneRegistre> Controles);

/// <summary>Registre des contrôles (TIV et révisions) pour l'impression : blocs d'abord, puis le reste du matériel suivi.</summary>
public sealed record RegistreControles(DateOnly Edition, IReadOnlyList<FicheRegistre> Blocs, IReadOnlyList<FicheRegistre> Autres)
{
    public int NombreControles => Blocs.Concat(Autres).Sum(f => f.Controles.Count);
}

/// <summary>
/// Colonne du tableau des révisions : l'équipement avance de gauche à droite jusqu'à son retour en service.
/// Stockée par son nom dans revision.etape (sauf <see cref="APrevoir"/>, calculée) : ne jamais renommer un membre sans migration.
/// </summary>
public enum EtapeRevision
{
    /// <summary>Au club. Échéance dans les 90 jours ou dépassée, 90 % du seuil d'utilisations, ou problème signalé (calculé).</summary>
    APrevoir,

    /// <summary>Au club : séance TIV ou petite réparation par le responsable matériel (ancien « Planifié »).</summary>
    Interne,

    /// <summary>Au club, mais décidé pour partir chez un prestataire : à déposer ou expédier.</summary>
    AEnvoyer,

    /// <summary>Hors du club, chez un magasin ou un organisme (ancien « En atelier »).</summary>
    ChezPrestataire,

    /// <summary>Intervention enregistrée : remis en service (ou hors service si refusé).</summary>
    Fait,
}

/// <summary>Genre de prestataire.</summary>
public enum TypePrestataire
{
    /// <summary>Magasin de plongée (révisions, réparations).</summary>
    Magasin,

    /// <summary>Organisme (requalification des blocs).</summary>
    Organisme,
}

/// <summary>Magasin ou organisme qui révise le matériel du club (table prestataire, migration 016), géré dans Paramètres.</summary>
public sealed class Prestataire
{
    public long Id { get; set; }
    public string Nom { get; set; } = "";
    public TypePrestataire Type { get; set; }
    public string Telephone { get; set; } = "";
    public string Adresse { get; set; } = "";

    /// <summary>Inactif : plus proposé pour un envoi, gardé par les révisions passées.</summary>
    public bool Actif { get; set; } = true;
}

/// <summary>
/// Étape d'un équipement dans le tableau des révisions, une fois qu'on a décidé quoi faire (table revision, migration 016).
/// Une seule ligne hors « Fait » par équipement. « À prévoir » n'est pas stocké : il est calculé d'après les échéances.
/// </summary>
public sealed class SuiviRevision
{
    public long Id { get; set; }
    public long EquipementId { get; set; }
    public EtapeRevision Etape { get; set; }

    /// <summary>Prestataire choisi (À envoyer, Chez le prestataire), ou null.</summary>
    public long? PrestataireId { get; set; }

    /// <summary>Nom du prestataire (lu avec la ligne ; celui du passage en atelier si l'envoi n'a pas de prestataire enregistré).</summary>
    public string Prestataire { get; set; } = "";

    /// <summary>Ce qu'il y a à faire (« Révision · 200 utilisations », « Requalification »...).</summary>
    public string Motif { get; set; } = "";

    /// <summary>Organisation du dépôt (« Dépôt prévu le 15/11 », « Envoi groupé avec 2 autres blocs »).</summary>
    public string NoteEnvoi { get; set; } = "";

    /// <summary>Jour du départ chez le prestataire.</summary>
    public DateOnly? DateEnvoi { get; set; }

    public DateOnly? DateRetourPrevue { get; set; }

    /// <summary>Qui répare au club (responsable matériel, « Séance TIV »...).</summary>
    public string IntervenantInterne { get; set; } = "";

    /// <summary>Bloc rattaché à la prochaine séance TIV.</summary>
    public bool SeanceTiv { get; set; }

    /// <summary>Passage en atelier ouvert au départ chez le prestataire.</summary>
    public long? AtelierId { get; set; }

    /// <summary>Intervention qui a terminé la révision (étape Fait).</summary>
    public long? ControleId { get; set; }

    public DateTime CreeLe { get; set; }

    public DateOnly? DateFait { get; set; }

    /// <summary>Retirée de la colonne « Fait » (rien n'est supprimé : Historique et fiche EPI la gardent).</summary>
    public bool Archive { get; set; }

    public DateOnly? DateArchive { get; set; }
}

/// <summary>Pastille d'une carte du tableau.</summary>
public enum EtatCarte
{
    EnRetard,
    CeMois,
    APlanifier,
    EnAtelier,
    Conforme,
    ASurveiller,
    HorsService,
}

/// <summary>
/// Une carte du tableau des révisions. <paramref name="Signalement"/> : un problème signalé est ouvert (badge « Signalé au
/// retour »). <paramref name="SuiviId"/> : ligne de la table revision (null pour « À prévoir »). <paramref name="JoursDehors"/>
/// et <paramref name="RetourEnRetard"/> : chez le prestataire seulement.
/// </summary>
public sealed record CarteRevision(
    EtapeRevision Etape,
    long EquipementId,
    string Code,
    string Libelle,
    string Categorie,
    string Famille,
    TypeControle Type,
    string Controle,
    string Detail,
    EtatCarte Etat,
    string Badge,
    bool Bloque,
    UsageEquipement? Usage,
    long? PassageAtelierId = null,
    string? Prestataire = null,
    bool Signalement = false,
    long? SuiviId = null,
    string NoteEnvoi = "",
    DateOnly? DateEnvoi = null,
    DateOnly? DateRetourPrevue = null,
    int? JoursDehors = null,
    bool RetourEnRetard = false,
    bool SeanceTiv = false)
{
    /// <summary>Encore au club (À prévoir, Réparation interne, À envoyer) : tout sauf « Chez le prestataire » et « Fait ».</summary>
    public bool AuClub => Etape is EtapeRevision.APrevoir or EtapeRevision.Interne or EtapeRevision.AEnvoyer;
}

/// <summary>Tableau des révisions : la prochaine séance TIV et toutes les cartes.</summary>
public sealed record TableauRevisions(ProchaineSeance? Seance, IReadOnlyList<CarteRevision> Cartes)
{
    private int Equipements(Func<CarteRevision, bool> filtre) => Cartes.Where(filtre).Select(c => c.EquipementId).Distinct().Count();

    /// <summary>Équipements au club, en attente : À prévoir, Réparation interne et À envoyer.</summary>
    public int AuClub => Equipements(c => c.AuClub);

    /// <summary>Dont à envoyer chez un prestataire.</summary>
    public int AEnvoyer => Equipements(c => c.Etape == EtapeRevision.AEnvoyer);

    /// <summary>Équipements hors du club, chez un prestataire.</summary>
    public int HorsClub => Equipements(c => c.Etape == EtapeRevision.ChezPrestataire);

    /// <summary>Retours attendus dont la date prévue est passée.</summary>
    public int RetoursEnRetard => Equipements(c => c.Etape == EtapeRevision.ChezPrestataire && c.RetourEnRetard);

    /// <summary>« 2 chez Aqua Service, 1 chez Bureau Veritas » (le plus chargé d'abord).</summary>
    public IReadOnlyList<(string Prestataire, int Nombre)> ParPrestataire => Cartes
        .Where(c => c.Etape == EtapeRevision.ChezPrestataire)
        .GroupBy(c => string.IsNullOrWhiteSpace(c.Prestataire) ? "prestataire inconnu" : c.Prestataire!)
        .Select(g => (g.Key, g.Select(c => c.EquipementId).Distinct().Count()))
        .OrderByDescending(p => p.Item2).ThenBy(p => p.Key, StringComparer.Create(Libelles.Culture, true))
        .ToList();

    /// <summary>Familles du filtre, dans l'ordre : Signalé, TIV, Requalification, Utilisation, puis les catégories.</summary>
    public IReadOnlyList<string> Familles
    {
        get
        {
            var presentes = Cartes.Select(c => c.Famille).Distinct().ToList();
            string[] enTete = [Services.ServiceRevisions.FamilleSignalement, "TIV", "Requalification", "Utilisation"];
            return enTete.Where(presentes.Contains)
                .Concat(presentes.Where(f => !enTete.Contains(f)).OrderBy(f => f, StringComparer.Create(Libelles.Culture, true)))
                .ToList();
        }
    }
}

/// <summary>
/// Intervention enregistrée depuis le tableau (« Faire maintenant », « Enregistrer le résultat », « Retour d'atelier »)
/// ou depuis la fiche de suivi (composant visé, détails).
/// </summary>
public sealed record Intervention(
    long EquipementId,
    TypeControle Type,
    ResultatControle Resultat,
    DateOnly Date,
    string Nature,
    string Intervenant,
    long? InspecteurId = null,
    long? PassageAtelierId = null,
    string Composant = "",
    string Notes = "");
