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
