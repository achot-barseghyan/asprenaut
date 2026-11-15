using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Saisie;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>
/// Lecture et modification des réglages du club. Chaque modification est validée,
/// appliquée tout de suite (instance partagée) et enregistrée.
/// </summary>
public sealed class ServiceReglages(
    Reglages reglages, IStockageReglages stockage, IServiceSauvegarde sauvegardes, ServiceGestion? gestion = null, IStockageLogo? logos = null)
{
    public const int LongueurMaxPrefixe = 8;

    public Reglages Actuels => reglages;

    /// <summary>Fiche du club enregistrée (nom, logo…) : le menu affiche le nouveau nom.</summary>
    public event EventHandler? ClubChange;

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

    public const int LongueurMaxNomClub = 60;
    public const int LongueurMaxResponsable = 60;
    public const int LongueurMaxNumeroClub = 20;
    public const int LongueurMaxTelephone = 20;

    /// <summary>Poids maximal du fichier choisi comme logo (2 Mo).</summary>
    public const long TailleMaxLogo = 2 * 1024 * 1024;

    /// <summary>Formats d'image acceptés pour le logo (lus par Windows sans bibliothèque).</summary>
    public static readonly string[] ExtensionsLogo = [".png", ".jpg", ".jpeg", ".bmp", ".gif"];

    /// <summary>Message d'erreur si ce fichier ne peut pas servir de logo (format, poids), sinon null.</summary>
    public static string? VerifierFichierLogo(string nomFichier, long taille)
    {
        if (!ExtensionsLogo.Contains(Path.GetExtension(nomFichier ?? "").ToLowerInvariant()))
        {
            return "Format non pris en charge. Utilisez une image PNG, JPG, BMP ou GIF.";
        }

        if (taille <= 0)
        {
            return "Ce fichier est vide.";
        }

        return taille > TailleMaxLogo
            ? $"Fichier trop lourd ({(taille / 1048576.0).ToString("0.0", Libelles.Culture)} Mo). 2 Mo au plus."
            : null;
    }

    /// <summary>Initiales affichées à la place du logo (« Asprenaut » → « A », « Club Subaquatique » → « CS ») ; « ? » sans nom.</summary>
    public static string InitialesClub(string? nom)
    {
        var initiales = string.Concat((nom ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(m => char.IsLetterOrDigit(m[0])).Take(2).Select(m => char.ToUpper(m[0], Libelles.Culture)));
        return initiales.Length == 0 ? "?" : initiales;
    }

    /// <summary>
    /// Fiche du club (Paramètres › Club) : nom (obligatoire, 60 car.), numéro d'affiliation (20), responsable matériel (60) et son
    /// téléphone (mis en forme par paires, 20), et le logo. Tout est vérifié avant d'écrire : rien ne change en cas de refus.
    /// </summary>
    public ResultatOperation ChangerClub(SaisieClub saisie)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        var nom = (saisie.Nom ?? "").Trim();
        var numero = (saisie.Numero ?? "").Trim();
        var responsable = (saisie.Responsable ?? "").Trim();
        var telephone = ServiceAdherents.FormaterTelephone((saisie.Telephone ?? "").Trim());
        if (nom.Length == 0)
        {
            return ResultatOperation.Echec("Indiquez le nom du club.");
        }

        if (nom.Length > LongueurMaxNomClub)
        {
            return ResultatOperation.Echec($"Nom du club trop long ({LongueurMaxNomClub} caractères au plus).");
        }

        if (numero.Length > LongueurMaxNumeroClub)
        {
            return ResultatOperation.Echec($"Numéro d'affiliation trop long ({LongueurMaxNumeroClub} caractères au plus).");
        }

        if (responsable.Length > LongueurMaxResponsable)
        {
            return ResultatOperation.Echec($"Nom du responsable trop long ({LongueurMaxResponsable} caractères au plus).");
        }

        if (telephone.Length > LongueurMaxTelephone)
        {
            return ResultatOperation.Echec($"Téléphone trop long ({LongueurMaxTelephone} caractères au plus).");
        }

        var logo = saisie.Logo ?? ChangementLogo.Garder;
        if (logo.Image is { } image && (image.Length == 0 || image.Length > TailleMaxLogo))
        {
            return ResultatOperation.Echec("Image du logo trop lourde (2 Mo au plus).");
        }

        if (logo.Choix is { } choix && choix != reglages.LogoClub || logo.Image is not null)
        {
            if (logos is null)
            {
                return ResultatOperation.Echec("Le logo ne peut pas être enregistré ici.");
            }

            try
            {
                if (logo.Image is { } png)
                {
                    logos.Enregistrer(png);
                }
                else if (logo.Choix != ChoixLogo.Personnalise)
                {
                    logos.Supprimer();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return ResultatOperation.Echec("Impossible d'enregistrer le logo sur ce PC.");
            }

            reglages.LogoClub = logo.Image is not null ? ChoixLogo.Personnalise : logo.Choix!.Value;
        }

        reglages.NomClub = nom;
        reglages.NumeroClub = numero;
        reglages.ResponsableMateriel = responsable;
        reglages.TelephoneResponsable = telephone;
        stockage.Enregistrer(reglages);
        ClubChange?.Invoke(this, EventArgs.Empty);
        return ResultatOperation.Ok("Club enregistré.");
    }

    /// <summary>Chemin de l'image du logo choisi par le club, ou null (logo fourni, aucun logo, ou fichier disparu).</summary>
    public string? CheminLogo() => reglages.LogoClub == ChoixLogo.Personnalise ? logos?.Chemin() : null;

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

/// <summary>Fiche du club saisie dans Paramètres › Club (<see cref="ServiceReglages.ChangerClub"/>).</summary>
public sealed record SaisieClub(string? Nom, string? Numero, string? Responsable, string? Telephone, ChangementLogo? Logo = null);

/// <summary>Ce qu'on fait du logo en enregistrant le club : le garder, le remplacer par une image, le retirer, revenir à celui d'origine.</summary>
public sealed record ChangementLogo
{
    private ChangementLogo(ChoixLogo? choix, byte[]? image)
    {
        Choix = choix;
        Image = image;
    }

    /// <summary>Choix demandé (null : on garde le logo actuel, ou une nouvelle image).</summary>
    public ChoixLogo? Choix { get; }

    /// <summary>Nouvelle image, déjà mise en PNG par l'écran.</summary>
    public byte[]? Image { get; }

    public static ChangementLogo Garder { get; } = new(null, null);

    public static ChangementLogo Retirer { get; } = new(ChoixLogo.Aucun, null);

    public static ChangementLogo Origine { get; } = new(ChoixLogo.Fourni, null);

    public static ChangementLogo Remplacer(byte[] png) => new(null, png);
}
