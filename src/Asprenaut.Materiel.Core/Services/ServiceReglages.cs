using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Saisie;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>
/// Lecture et modification des réglages du club. Chaque modification est validée,
/// appliquée tout de suite (instance partagée) et enregistrée.
/// </summary>
public sealed class ServiceReglages(Reglages reglages, IStockageReglages stockage, IServiceSauvegarde sauvegardes, ServiceGestion? gestion = null)
{
    public const int LongueurMaxPrefixe = 8;

    public Reglages Actuels => reglages;

    /// <summary>Tous les paramètres, thème compris, sont une action protégée.</summary>
    private ResultatOperation Autorisation() => gestion?.Exiger(ActionProtegee.Parametres) ?? ResultatOperation.Ok("");

    /// <summary>Interrupteurs et choix simples (sons, focus, confirmation...) : aucune validation nécessaire.</summary>
    public ResultatOperation Modifier(Action<Reglages> changement)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        changement(reglages);
        stockage.Enregistrer(reglages);
        return ResultatOperation.Ok("Enregistré.");
    }

    /// <summary>
    /// Tuiles de la fenêtre « Problème sur A07 » pour une catégorie (Paramètres › Problèmes signalés) : libellés, pictogrammes,
    /// ordre et précision obligatoire. Vérifiées par <see cref="ServiceSignalements.VerifierTuiles"/>.
    /// </summary>
    public ResultatOperation ChangerTuilesSignalement(string categorie, IReadOnlyList<CauseSignalement> tuiles)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        var nom = (categorie ?? "").Trim();
        if (nom.Length == 0)
        {
            return ResultatOperation.Echec("Choisissez une catégorie.");
        }

        if (ServiceSignalements.VerifierTuiles(tuiles) is { } erreur)
        {
            return ResultatOperation.Echec(erreur);
        }

        RetirerTuiles(nom);
        reglages.CausesSignalement[nom] = tuiles.Select(t => new CauseSignalement
        {
            Libelle = t.Libelle.Trim(),
            Pictogramme = string.IsNullOrWhiteSpace(t.Pictogramme) ? "Autre" : t.Pictogramme.Trim(),
            PrecisionObligatoire = t.PrecisionObligatoire,
        }).ToList();
        stockage.Enregistrer(reglages);
        return ResultatOperation.Ok("Enregistré.");
    }

    /// <summary>
    /// Niveaux des adhérents (Paramètres › Configuration du club) : un par ligne, 1 à 30 niveaux de 30 caractères au plus, sans
    /// doublon (majuscules et accents ignorés). Les adhérents gardent leur niveau même s'il n'est plus dans la liste.
    /// </summary>
    public ResultatOperation ChangerNiveauxAdherents(IEnumerable<string> niveaux)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        var liste = niveaux.Select(n => (n ?? "").Trim()).Where(n => n.Length > 0).ToList();
        if (liste.Count == 0)
        {
            return ResultatOperation.Echec("Gardez au moins un niveau.");
        }

        if (liste.Count > 30)
        {
            return ResultatOperation.Echec("30 niveaux au plus.");
        }

        if (liste.FirstOrDefault(n => n.Length > Adherent.LongueurMaxNiveau) is { } long_)
        {
            return ResultatOperation.Echec($"« {long_[..20]}… » : {Adherent.LongueurMaxNiveau} caractères au plus.");
        }

        var doublon = liste.GroupBy(ServiceAdherents.Simplifier).FirstOrDefault(g => g.Count() > 1);
        if (doublon is not null)
        {
            return ResultatOperation.Echec($"« {doublon.First()} » est en double.");
        }

        reglages.NiveauxAdherents = liste;
        stockage.Enregistrer(reglages);
        return ResultatOperation.Ok($"{Libelles.Pluriel(liste.Count, "niveau enregistré", "niveaux enregistrés")}.");
    }

    /// <summary>Revient aux tuiles d'origine de la catégorie.</summary>
    public ResultatOperation RetablirTuilesSignalement(string categorie)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        RetirerTuiles((categorie ?? "").Trim());
        stockage.Enregistrer(reglages);
        return ResultatOperation.Ok("Tuiles d'origine rétablies.");
    }

    private void RetirerTuiles(string categorie)
    {
        foreach (var cle in reglages.CausesSignalement.Keys.Where(k => string.Equals(k.Trim(), categorie, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            reglages.CausesSignalement.Remove(cle);
        }
    }

    /// <summary>Jours avant l'alerte de retour, bornés entre 1 et 14 (valeur inchangée en mode consultation).</summary>
    public int ChangerJoursAlerte(int jours)
    {
        if (!Autorisation().Reussi)
        {
            return reglages.JoursAvantAlerteRetour;
        }

        reglages.JoursAvantAlerteRetour = Math.Clamp(jours, Reglages.JoursAlerteMin, Reglages.JoursAlerteMax);
        stockage.Enregistrer(reglages);
        return reglages.JoursAvantAlerteRetour;
    }

    /// <summary>Durée du mode gestion sans activité : 5, 15, 30 ou 60 minutes.</summary>
    public ResultatOperation ChangerMinutesDeverrouillage(int minutes)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        if (!Reglages.MinutesDeverrouillagePossibles.Contains(minutes))
        {
            return ResultatOperation.Echec("Choisissez 5, 15, 30 ou 60 minutes.");
        }

        reglages.MinutesDeverrouillage = minutes;
        stockage.Enregistrer(reglages);
        return ResultatOperation.Ok("Enregistré.");
    }

    /// <summary>Jours de prévenance avant une révision : 15, 30 ou 60.</summary>
    public ResultatOperation ChangerJoursRevision(int jours)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        if (!Reglages.JoursRevisionPossibles.Contains(jours))
        {
            return ResultatOperation.Echec("Choisissez 15, 30 ou 60 jours.");
        }

        reglages.JoursAvantRevision = jours;
        stockage.Enregistrer(reglages);
        return ResultatOperation.Ok("Enregistré.");
    }

    /// <summary>Thème de l'interface (sombre ou clair), retenu pour les prochains démarrages.</summary>
    public ResultatOperation ChangerAffichage(ModeAffichage mode)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        if (!Enum.IsDefined(mode))
        {
            return ResultatOperation.Echec("Thème inconnu.");
        }

        reglages.Affichage = mode;
        stockage.Enregistrer(reglages);
        return ResultatOperation.Ok(mode == ModeAffichage.Clair ? "Thème clair." : "Thème sombre.");
    }

    /// <summary>
    /// Menu latéral réduit aux icônes ou complet. Simple confort d'affichage (pas un paramètre du club) : libre, même en
    /// mode consultation.
    /// </summary>
    /// <summary>
    /// Note une sauvegarde complète réussie (date et dossier, proposé la fois suivante) : libre, comme faire une sauvegarde.
    /// </summary>
    public void NoterSauvegardeComplete(DateTime quand, string? dossier)
    {
        reglages.DerniereSauvegardeComplete = quand;
        reglages.DossierSauvegardeComplete = string.IsNullOrWhiteSpace(dossier) ? reglages.DossierSauvegardeComplete : dossier;
        stockage.Enregistrer(reglages);
    }

    /// <summary>Rappel de la sauvegarde complète hebdomadaire (écran Sorties, Paramètres).</summary>
    public EtatSauvegardeComplete EtatSauvegardeComplete(DateTime maintenant) =>
        RappelSauvegarde.Etat(reglages.DerniereSauvegardeComplete, maintenant);

    public ResultatOperation ChangerMenuReduit(bool reduit)
    {
        if (reglages.MenuReduit != reduit)
        {
            reglages.MenuReduit = reduit;
            stockage.Enregistrer(reglages);
        }

        return ResultatOperation.Ok(reduit ? "Menu réduit." : "Menu complet.");
    }

    /// <summary>
    /// Colonnes du tableau des révisions repliées : confort d'affichage, libre comme le menu réduit. Au moins une colonne
    /// reste ouverte.
    /// </summary>
    public ResultatOperation ChangerColonnesRevisionsRepliees(IEnumerable<EtapeRevision> repliees)
    {
        var liste = repliees.Where(Enum.IsDefined).Distinct().Order().ToList();
        if (liste.Count >= Enum.GetValues<EtapeRevision>().Length)
        {
            return ResultatOperation.Echec("Gardez au moins une colonne ouverte.");
        }

        if (!liste.SequenceEqual(reglages.ColonnesRevisionsRepliees.Order()))
        {
            reglages.ColonnesRevisionsRepliees = liste;
            stockage.Enregistrer(reglages);
        }

        return ResultatOperation.Ok(liste.Count == 0 ? "Toutes les colonnes sont ouvertes." : $"{liste.Count} colonne{(liste.Count > 1 ? "s" : "")} repliée{(liste.Count > 1 ? "s" : "")}.");
    }

    /// <summary>Préfixe des étiquettes : sans espaces, en majuscules, 8 caractères au plus (vide = aucun).</summary>
    public ResultatOperation ChangerPrefixe(string? prefixe)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        var p = CodeScanne.NormaliserPrefixe(prefixe);
        if (p.Length > LongueurMaxPrefixe)
        {
            return ResultatOperation.Echec($"Préfixe trop long ({LongueurMaxPrefixe} caractères au plus).");
        }

        reglages.PrefixeEtiquette = p;
        stockage.Enregistrer(reglages);
        return ResultatOperation.Ok(p.Length == 0 ? "Aucun préfixe." : $"Préfixe « {p} » enregistré.");
    }

    /// <summary>Nom du club et responsable du matériel (textes libres, espaces superflus retirés).</summary>
    public ResultatOperation ChangerClub(string? nomClub, string? responsable)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        var nom = (nomClub ?? "").Trim();
        if (nom.Length == 0)
        {
            return ResultatOperation.Echec("Indiquez le nom du club.");
        }

        reglages.NomClub = nom;
        reglages.ResponsableMateriel = (responsable ?? "").Trim();
        stockage.Enregistrer(reglages);
        return ResultatOperation.Ok("Enregistré.");
    }

    /// <summary>Numéro d'affiliation du club (texte libre, 20 caractères au plus ; vide accepté).</summary>
    public ResultatOperation ChangerNumeroClub(string? numero)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        var n = (numero ?? "").Trim();
        if (n.Length > 20)
        {
            return ResultatOperation.Echec("Numéro trop long (20 caractères au plus).");
        }

        reglages.NumeroClub = n;
        stockage.Enregistrer(reglages);
        return ResultatOperation.Ok("Enregistré.");
    }

    /// <summary>Nouveau dossier des sauvegardes : vérifié par le service de sauvegarde, puis retenu.</summary>
    public ResultatOperation ChangerDossierSauvegardes(string dossier)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        var resultat = sauvegardes.ChangerDossier(dossier);
        if (resultat.Reussi)
        {
            reglages.DossierSauvegardes = sauvegardes.Dossier;
            stockage.Enregistrer(reglages);
        }

        return resultat;
    }
}
