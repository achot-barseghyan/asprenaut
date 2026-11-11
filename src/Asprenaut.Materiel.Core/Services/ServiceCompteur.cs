using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Saisie;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>
/// Compteur d'utilisation : chaque retour scanné compte une sortie ; la révision tombe au seuil (depuis le dernier
/// contrôle enregistré, quel qu'il soit) ou à la date, le premier atteint. Seuil d'un équipement : son seuil propre
/// (code), sinon celui de son modèle (même nom), sinon celui de sa catégorie ; 0 ou absent = pas de compteur.
/// Les réglages sont une action protégée (Paramètres).
/// </summary>
public sealed class ServiceCompteur(
    Reglages reglages,
    IStockageReglages stockage,
    IEquipementRepository equipements,
    ICategorieRepository categories,
    ServiceGestion? gestion = null)
{
    public const int SeuilCategorieMax = 500;
    public const int SeuilSpecifiqueMin = 10;
    public const int SeuilSpecifiqueMax = 1000;
    public const int Pas = 10;
    public const int SeuilParDefaut = 100;

    /// <summary>Part du seuil à partir de laquelle l'équipement est « à planifier ».</summary>
    public const double PartAvantSeuil = 0.9;

    // ---------------------------------------------------------------- Règles

    /// <summary>Seuil qui s'applique à cet équipement, ou null s'il n'a pas de compteur.</summary>
    public static int? Seuil(Reglages r, string code, string libelle, string categorie) => SeuilEtOrigine(r, code, libelle, categorie)?.Seuil;

    /// <summary>Seuil qui s'applique et d'où il vient (code, modèle ou catégorie), ou null s'il n'y a pas de compteur.</summary>
    public static (int Seuil, OrigineSeuil Origine)? SeuilEtOrigine(Reglages r, string code, string libelle, string categorie)
    {
        var propre = r.SeuilsSpecifiques.FirstOrDefault(s => s.Portee == PorteeSeuil.Equipement && string.Equals(s.Cle, code, StringComparison.OrdinalIgnoreCase))
            ?? r.SeuilsSpecifiques.FirstOrDefault(s => s.Portee == PorteeSeuil.Modele && string.Equals(s.Cle, libelle.Trim(), StringComparison.OrdinalIgnoreCase));
        if (propre is not null)
        {
            var origine = propre.Portee == PorteeSeuil.Equipement ? OrigineSeuil.Equipement : OrigineSeuil.Modele;
            return propre.Sorties > 0 ? (propre.Sorties, origine) : null;
        }

        var parCategorie = r.SeuilsSorties.FirstOrDefault(p => string.Equals(p.Key, categorie.Trim(), StringComparison.OrdinalIgnoreCase));
        return parCategorie.Value > 0 ? (parCategorie.Value, OrigineSeuil.Categorie) : null;
    }

    /// <summary>Atteint au seuil ; proche à 90 % (arrondi au-dessus) si le club veut être prévenu.</summary>
    public static EtatCompteur Etat(int utilisations, int seuil, bool prevenir) =>
        utilisations >= seuil ? EtatCompteur.Atteint
        : prevenir && utilisations >= (int)Math.Ceiling(seuil * PartAvantSeuil) ? EtatCompteur.Proche
        : EtatCompteur.Normal;

    /// <summary>Usage d'un équipement (scan de départ), ou null s'il n'a pas de compteur.</summary>
    public async Task<UsageEquipement?> UsageAsync(Equipement e, CancellationToken ct = default)
    {
        var categorie = (await categories.ListerAsync(ct)).FirstOrDefault(c => c.Id == e.CategorieId)?.Nom ?? "";
        if (Seuil(reglages, e.Code, e.Libelle, categorie) is not { } seuil)
        {
            return null;
        }

        var utilisations = (await equipements.ListerCompteursAsync(e.Id, ct)).FirstOrDefault()?.Utilisations ?? 0;
        return new UsageEquipement(utilisations, seuil, Etat(utilisations, seuil, reglages.PrevenirAvantSeuil));
    }

    /// <summary>Usage de tous les équipements qui ont un compteur (hors réformés), par identifiant.</summary>
    public async Task<IReadOnlyDictionary<long, UsageEquipement>> ListerUsagesAsync(IReadOnlyList<LigneInventaire> inventaire, CancellationToken ct = default)
    {
        var compteurs = (await equipements.ListerCompteursAsync(null, ct)).ToDictionary(c => c.EquipementId, c => c.Utilisations);
        var usages = new Dictionary<long, UsageEquipement>();
        foreach (var e in inventaire.Where(e => e.Etat != EtatEquipement.Reforme))
        {
            if (Seuil(reglages, e.Code, e.Libelle, e.Categorie) is { } seuil)
            {
                var utilisations = compteurs.GetValueOrDefault(e.EquipementId);
                usages[e.EquipementId] = new UsageEquipement(utilisations, seuil, Etat(utilisations, seuil, reglages.PrevenirAvantSeuil));
            }
        }

        return usages;
    }

    /// <summary>
    /// Colonne « Utilisation » de l'inventaire : tous les équipements hors réformés, avec ou sans seuil
    /// (« 64 / 80 utilisations », sinon « 23 utilisations »).
    /// </summary>
    public async Task<IReadOnlyDictionary<long, UtilisationInventaire>> ListerUtilisationsAsync(IReadOnlyList<LigneInventaire> inventaire, CancellationToken ct = default)
    {
        var compteurs = (await equipements.ListerCompteursAsync(null, ct)).ToDictionary(c => c.EquipementId, c => c.Utilisations);
        var resultat = new Dictionary<long, UtilisationInventaire>();
        foreach (var e in inventaire.Where(e => e.Etat != EtatEquipement.Reforme))
        {
            var utilisations = compteurs.GetValueOrDefault(e.EquipementId);
            resultat[e.EquipementId] = SeuilEtOrigine(reglages, e.Code, e.Libelle, e.Categorie) is ({ } seuil, var origine)
                ? new UtilisationInventaire(utilisations, seuil, Etat(utilisations, seuil, reglages.PrevenirAvantSeuil), origine)
                : new UtilisationInventaire(utilisations, null, EtatCompteur.Normal, OrigineSeuil.Categorie);
        }

        return resultat;
    }

    /// <summary>
    /// Modèles de l'inventaire (même nom d'équipement, hors réformés, catégories à compteur) pour « Ajouter un seuil spécifique » :
    /// catégorie la plus fréquente, nombre d'équipements, seuil qui s'applique aujourd'hui. Filtrés par la recherche.
    /// </summary>
    public async Task<IReadOnlyList<ModeleCandidat>> ListerModelesAsync(string? recherche = null, CancellationToken ct = default)
    {
        var texte = (recherche ?? "").Trim();
        return (await equipements.ListerInventaireAsync(null, null, ct))
            .Where(e => e.Etat != EtatEquipement.Reforme && e.Libelle.Trim().Length > 0 && AUnCompteur(e.Categorie))
            .GroupBy(e => e.Libelle.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var categorie = g.GroupBy(e => e.Categorie).OrderByDescending(c => c.Count()).First().Key;
                var nom = g.First().Libelle.Trim();
                return new ModeleCandidat(nom, categorie, g.Count(), Seuil(reglages, "", nom, categorie));
            })
            .Where(m => texte.Length == 0 || Contient(m.Nom, texte) || Contient(m.Categorie, texte))
            .OrderBy(m => m.Nom, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Équipements de l'inventaire (hors réformés) pour « Ajouter un seuil spécifique », avec leurs utilisations et
    /// leur seuil actuel. La recherche accepte un code, une médaille scannée, un nom, une marque ou un modèle.
    /// </summary>
    public async Task<IReadOnlyList<EquipementCandidat>> ListerEquipementsAsync(string? recherche = null, CancellationToken ct = default)
    {
        var inventaire = (await equipements.ListerInventaireAsync(null, null, ct)).Where(e => e.Etat != EtatEquipement.Reforme && AUnCompteur(e.Categorie)).ToList();
        var usages = await ListerUtilisationsAsync(inventaire, ct);
        var texte = (recherche ?? "").Trim();
        var code = CodeScanne.Nettoyer(texte, reglages.PrefixeEtiquette);
        return inventaire
            .Where(e => texte.Length == 0 || e.Code.Contains(code, StringComparison.OrdinalIgnoreCase)
                || Contient(e.Libelle, texte) || Contient(e.Marque, texte) || Contient(e.Modele, texte))
            .Select(e => new EquipementCandidat(e.Code, e.Libelle.Trim(),
                string.Join(" ", new[] { e.Marque, e.Modele }.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim())),
                usages[e.EquipementId]))
            .OrderBy(e => e.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Catégories réglables dans « Compteur d'utilisation » (les autres n'ont jamais de compteur).</summary>
    private static bool AUnCompteur(string categorie) =>
        Reglages.CategoriesCompteur.Contains(categorie.Trim(), StringComparer.OrdinalIgnoreCase);

    private static bool Contient(string valeur, string texte) =>
        System.Globalization.CultureInfo.CurrentCulture.CompareInfo.IndexOf(valeur, texte,
            System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0;

    // ---------------------------------------------------------------- Réglages

    private ResultatOperation Autorisation() => gestion?.Exiger(ActionProtegee.Parametres) ?? ResultatOperation.Ok("");

    /// <summary>Seuil d'une catégorie (0 = désactivé), borné entre 0 et 500. Retourne la valeur retenue.</summary>
    public ResultatOperation ChangerSeuilCategorie(string categorie, int sorties)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        var cle = Reglages.CategoriesCompteur.FirstOrDefault(c => string.Equals(c, categorie, StringComparison.OrdinalIgnoreCase));
        if (cle is null)
        {
            return ResultatOperation.Echec("Catégorie inconnue.");
        }

        var valeur = Math.Clamp(sorties, 0, SeuilCategorieMax);
        reglages.SeuilsSorties[cle] = valeur;
        stockage.Enregistrer(reglages);
        return ResultatOperation.Ok(valeur == 0 ? "Compteur désactivé." : "Enregistré.", valeur);
    }

    /// <summary>
    /// Ajoute un seuil spécifique (100 utilisations par défaut, borné entre 10 et 1000). Équipement : code ou médaille
    /// scannée, qui doit exister.
    /// Modèle : nom exact d'au moins un équipement de l'inventaire. Pas deux fois le même.
    /// </summary>
    public async Task<ResultatOperation> AjouterSeuilSpecifiqueAsync(PorteeSeuil portee, string saisie, int sorties = SeuilParDefaut, CancellationToken ct = default)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        string cle;
        if (portee == PorteeSeuil.Equipement)
        {
            cle = CodeScanne.Nettoyer(saisie, reglages.PrefixeEtiquette);
            if (cle.Length == 0)
            {
                return ResultatOperation.Echec("Scannez ou tapez le code de l'équipement.");
            }

            if (await equipements.ObtenirParCodeAsync(cle, ct) is null)
            {
                return ResultatOperation.Echec($"Code inconnu : {cle}.");
            }
        }
        else
        {
            cle = (saisie ?? "").Trim();
            if (cle.Length == 0)
            {
                return ResultatOperation.Echec("Tapez le nom du modèle, tel qu'il apparaît dans l'inventaire.");
            }

            var modele = (await equipements.ListerInventaireAsync(null, null, ct))
                .FirstOrDefault(e => string.Equals(e.Libelle.Trim(), cle, StringComparison.OrdinalIgnoreCase));
            if (modele is null)
            {
                return ResultatOperation.Echec($"Aucun équipement ne s'appelle « {cle} » dans l'inventaire.");
            }

            cle = modele.Libelle.Trim(); // Orthographe de l'inventaire.
        }

        if (reglages.SeuilsSpecifiques.Any(s => s.Portee == portee && string.Equals(s.Cle, cle, StringComparison.OrdinalIgnoreCase)))
        {
            return ResultatOperation.Echec($"« {cle} » a déjà son seuil.");
        }

        var valeur = Math.Clamp(sorties, SeuilSpecifiqueMin, SeuilSpecifiqueMax);
        reglages.SeuilsSpecifiques.Add(new SeuilSpecifique { Portee = portee, Cle = cle, Sorties = valeur });
        stockage.Enregistrer(reglages);
        return ResultatOperation.Ok($"Seuil de {valeur} utilisations ajouté pour {cle}.");
    }

    /// <summary>Change un seuil spécifique, borné entre 10 et 1000. Retourne la valeur retenue.</summary>
    public ResultatOperation ChangerSeuilSpecifique(PorteeSeuil portee, string cle, int sorties)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        if (Trouver(portee, cle) is not { } seuil)
        {
            return ResultatOperation.Echec("Ce seuil n'existe plus.");
        }

        seuil.Sorties = Math.Clamp(sorties, SeuilSpecifiqueMin, SeuilSpecifiqueMax);
        stockage.Enregistrer(reglages);
        return ResultatOperation.Ok("Enregistré.", seuil.Sorties);
    }

    public ResultatOperation RetirerSeuilSpecifique(PorteeSeuil portee, string cle)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        if (Trouver(portee, cle) is not { } seuil)
        {
            return ResultatOperation.Echec("Ce seuil n'existe plus.");
        }

        reglages.SeuilsSpecifiques.Remove(seuil);
        stockage.Enregistrer(reglages);
        return ResultatOperation.Ok($"Seuil de {cle} retiré.");
    }

    /// <summary>Seuils spécifiques avec ce qu'ils concernent : « 4 équipements » pour un modèle, le nom pour un code.</summary>
    public async Task<IReadOnlyList<SeuilSpecifiqueDecrit>> DecrireSeuilsSpecifiquesAsync(CancellationToken ct = default)
    {
        var inventaire = await equipements.ListerInventaireAsync(null, null, ct);
        return reglages.SeuilsSpecifiques.Select(s =>
        {
            var precision = s.Portee == PorteeSeuil.Modele
                ? Libelles.Pluriel(inventaire.Count(e => string.Equals(e.Libelle.Trim(), s.Cle, StringComparison.OrdinalIgnoreCase) && e.Etat != EtatEquipement.Reforme), "équipement")
                : inventaire.FirstOrDefault(e => string.Equals(e.Code, s.Cle, StringComparison.OrdinalIgnoreCase)) is { } e ? e.Libelle : "Code introuvable";
            return new SeuilSpecifiqueDecrit(s.Portee, s.Cle, s.Sorties, precision);
        }).ToList();
    }

    private SeuilSpecifique? Trouver(PorteeSeuil portee, string cle) =>
        reglages.SeuilsSpecifiques.FirstOrDefault(s => s.Portee == portee && string.Equals(s.Cle, cle, StringComparison.OrdinalIgnoreCase));
}
