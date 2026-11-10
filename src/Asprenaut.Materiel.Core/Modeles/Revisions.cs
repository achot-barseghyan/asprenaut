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

/// <summary>Colonne du tableau des révisions : l'équipement avance de gauche à droite jusqu'à son retour en service.</summary>
public enum EtapeRevision
{
    /// <summary>Échéance dans les 90 jours ou dépassée, ou 90 % du seuil d'utilisations.</summary>
    APrevoir,

    /// <summary>Ajouté à la prochaine séance TIV.</summary>
    Planifie,

    /// <summary>Chez un magasin ou un organisme.</summary>
    EnAtelier,

    /// <summary>Intervention enregistrée ces 30 derniers jours.</summary>
    Fait,
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

/// <summary>Une carte du tableau des révisions.</summary>
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
    string? Prestataire = null);

/// <summary>Tableau des révisions : la prochaine séance TIV et toutes les cartes.</summary>
public sealed record TableauRevisions(ProchaineSeance? Seance, IReadOnlyList<CarteRevision> Cartes)
{
    /// <summary>Familles du filtre, dans l'ordre : TIV, Requalification, Utilisation, puis les catégories.</summary>
    public IReadOnlyList<string> Familles
    {
        get
        {
            var presentes = Cartes.Select(c => c.Famille).Distinct().ToList();
            return new[] { "TIV", "Requalification", "Utilisation" }.Where(presentes.Contains)
                .Concat(presentes.Where(f => f is not ("TIV" or "Requalification" or "Utilisation")).OrderBy(f => f, StringComparer.Create(Libelles.Culture, true)))
                .ToList();
        }
    }
}

/// <summary>Intervention enregistrée depuis le tableau (« Faire maintenant », « Enregistrer le résultat », « Retour d'atelier »).</summary>
public sealed record Intervention(
    long EquipementId,
    TypeControle Type,
    ResultatControle Resultat,
    DateOnly Date,
    string Nature,
    string Intervenant,
    long? InspecteurId = null,
    long? PassageAtelierId = null);
