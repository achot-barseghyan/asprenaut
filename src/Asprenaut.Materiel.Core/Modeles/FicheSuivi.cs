namespace Asprenaut.Materiel.Core.Modeles;

// Fiche de suivi EPI d'un équipement (écran ouvert depuis l'inventaire) : en-tête, composants suivis et interventions.

/// <summary>Origine d'une intervention de la fiche de suivi.</summary>
public enum TypeInterventionSuivie
{
    /// <summary>Contrôle ou révision enregistré (table controle).</summary>
    Controle,

    /// <summary>Passage en atelier (envoi chez un prestataire).</summary>
    Atelier,
}

/// <summary>D'où vient un composant ajouté à un détendeur.</summary>
public enum OrigineComposant
{
    /// <summary>Pièce neuve.</summary>
    Neuve,

    /// <summary>Récupérée sur un autre détendeur du club.</summary>
    Recuperee,
}

/// <summary>
/// Ce que la fiche matériel ne garde pas pour un composant ajouté (table composant, migration 012) : fabrication (premier
/// jour du mois), mise en service, origine et, pour le manomètre, le numéro de série.
/// </summary>
public sealed class DetailComposant
{
    public long EquipementId { get; set; }
    public string Role { get; set; } = "";
    public string NumeroSerie { get; set; } = "";
    public DateOnly? Fabrication { get; set; }
    public DateOnly? MiseEnService { get; set; }
    public OrigineComposant Origine { get; set; }
}

/// <summary>
/// Composant à ajouter (fenêtre « Ajouter un composant »). <paramref name="Fabrication"/> : texte « MM/AAAA », vide = inconnue.
/// <paramref name="NoterDansHistorique"/> : enregistre aussi une intervention « Montage octopus » sur ce composant.
/// </summary>
public sealed record NouveauComposant(
    string Role,
    string Modele,
    string NumeroSerie,
    string Fabrication,
    DateOnly? MiseEnService,
    OrigineComposant Origine,
    bool NoterDansHistorique);

/// <summary>Un composant possible d'un détendeur, présent ou non (« Composition actuelle »).</summary>
public sealed record EtatComposant(string Role, bool Present, string Modele);

/// <summary>Une tuile de la carte d'un composant (« Dernier contrôle », « Prochaine révision »...).</summary>
public sealed record TuileComposant(string Libelle, string Valeur);

/// <summary>
/// Un composant de l'équipement : rôle (premier étage, octopus... ou « Équipement complet »), modèle, numéro de série et
/// tuiles. Seul le dernier contrôle est propre au composant : la base ne suit pas les échéances composant par composant.
/// </summary>
public sealed record ComposantSuivi(string Role, string Modele, string NumeroSerie, IReadOnlyList<TuileComposant> Tuiles);

/// <summary>
/// Une ligne du tableau « Interventions » de la fiche de suivi. <paramref name="Composant"/> : rôle du composant visé,
/// ou « Équipement complet ». <paramref name="ResultatBrut"/> : null pour un passage en atelier.
/// </summary>
public sealed record InterventionSuivie(
    DateOnly Date,
    string Composant,
    string Nature,
    string Intervenant,
    TypeInterventionSuivie Type,
    string Resultat,
    ResultatControle? ResultatBrut);

/// <summary>
/// Fiche de suivi : la vie de l'équipement (en-tête), son titre, ses composants et toutes ses interventions.
/// <paramref name="DerniereRevision"/> : dernière révision enregistrée (champ de la fiche matériel quand on la modifie).
/// </summary>
public sealed record FicheSuiviEquipement(
    VieEquipement Vie,
    string Titre,
    IReadOnlyList<ComposantSuivi> Composants,
    IReadOnlyList<InterventionSuivie> Interventions,
    DateOnly? DerniereRevision)
{
    /// <summary>Plusieurs cartes (détendeur détaillé) : filtre des interventions par composant.</summary>
    public bool ParComposant => Composants.Count > 1;

    /// <summary>
    /// Interventions d'un composant : les siennes et celles de l'équipement complet (une révision complète le concerne aussi) ;
    /// null = toutes.
    /// </summary>
    public IReadOnlyList<InterventionSuivie> InterventionsDe(string? composant) =>
        composant is null
            ? Interventions
            : Interventions.Where(i => i.Composant == composant || i.Composant == Services.ServiceInventaire.ComposantComplet).ToList();
}
