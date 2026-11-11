using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>Ce que l'on saisit pour un manuel (ajout ou modification). Le fichier PDF n'est donné qu'à l'ajout.</summary>
public sealed record FicheManuel(
    string Marque,
    string Titre,
    long? CategorieId,
    string Modeles = "",
    string Edition = "",
    int? MoisRevision = null,
    int? PageEntretien = null,
    string Entretien = "",
    string Sommaire = "");

/// <summary>Notice livrée avec l'appli : sa fiche (catégorie donnée par son nom), son PDF extrait et son nombre de pages.</summary>
public sealed record ManuelFourni(string Categorie, FicheManuel Fiche, string CheminPdf, int NbPages);

/// <summary>
/// Manuels des fabricants (écran Manuels) : consultation libre ; ajouter, modifier, retirer = action protégée Matériel.
/// Le PDF est copié dans le dossier des manuels ; l'écran fournit son nombre de pages (lu par Windows).
/// Compare aussi le délai de révision du fabricant avec celui du club pour la catégorie du manuel.
/// </summary>
public sealed class ServiceManuels(
    IManuelRepository manuels,
    ICategorieRepository categories,
    IStockageManuels stockage,
    Reglages reglages,
    ServiceReglages serviceReglages,
    IStockageReglages stockageReglages,
    ServiceGestion? gestion = null)
{
    /// <summary>
    /// Installe les notices fournies avec l'appli, chacune une seule fois (<see cref="Reglages.ManuelsFournis"/>, clé = nom du
    /// fichier), sans code : c'est l'installation de l'appli, pas une action d'un utilisateur. Une notice déjà présente (même
    /// marque et même titre) n'est pas ajoutée deux fois ; une catégorie inconnue laisse le manuel sans catégorie.
    /// Retourne le nombre ajouté.
    /// </summary>
    public async Task<int> InstallerFournisAsync(IReadOnlyList<ManuelFourni> fournis, CancellationToken ct = default)
    {
        var aFaire = fournis.Where(f => !EstInstalle(f.CheminPdf)).ToList();
        if (aFaire.Count == 0)
        {
            return 0;
        }

        var existants = await manuels.ListerAsync(ct);
        var toutes = await categories.ListerAsync(ct);
        var ajoutes = 0;
        foreach (var f in aFaire)
        {
            reglages.ManuelsFournis.Add(Path.GetFileName(f.CheminPdf));
            if (existants.Any(m => string.Equals(m.Marque, f.Fiche.Marque, StringComparison.OrdinalIgnoreCase)
                                   && string.Equals(m.Titre, f.Fiche.Titre, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var categorie = toutes.FirstOrDefault(c => string.Equals(c.Nom, f.Categorie, StringComparison.OrdinalIgnoreCase));
            var manuel = new Manuel { NbPages = f.NbPages };
            if (f.NbPages < 1 || !File.Exists(f.CheminPdf) || await RemplirAsync(manuel, f.Fiche with { CategorieId = categorie?.Id }, ct) is not null)
            {
                continue;
            }

            manuel.Fichier = stockage.Importer(f.CheminPdf);
            await manuels.AjouterAsync(manuel, ct);
            ajoutes++;
        }

        stockageReglages.Enregistrer(reglages);
        return ajoutes;
    }

    /// <summary>Vrai si la notice fournie de ce fichier a déjà été installée (ou retirée ensuite par le club).</summary>
    public bool EstInstalle(string fichier) =>
        reglages.ManuelsFournis.Contains(Path.GetFileName(fichier), StringComparer.OrdinalIgnoreCase);

    private ResultatOperation Autorisation() => gestion?.Exiger(ActionProtegee.Materiel) ?? ResultatOperation.Ok("");

    public Task<IReadOnlyList<Manuel>> ListerAsync(CancellationToken ct = default) => manuels.ListerAsync(ct);

    /// <summary>
    /// Manuel de chaque équipement qui en a un (même catégorie, même marque) : id de l'équipement → id du manuel.
    /// Sert au bouton « Manuel » des lignes de matériel (Départ, Retour).
    /// </summary>
    public Task<IReadOnlyDictionary<long, long>> ManuelsDesEquipementsAsync(CancellationToken ct = default) =>
        manuels.ListerManuelsDesEquipementsAsync(ct);

    /// <summary>Catégories du club (filtre de l'écran, choix de la catégorie d'un manuel).</summary>
    public Task<IReadOnlyList<Categorie>> ListerCategoriesAsync(CancellationToken ct = default) => categories.ListerAsync(ct);

    public Task<IReadOnlyList<string>> ListerCodesEquipementsAsync(long manuelId, CancellationToken ct = default) =>
        manuels.ListerCodesEquipementsAsync(manuelId, ct);

    /// <summary>Chemin du PDF, ou null s'il a disparu du dossier des manuels.</summary>
    public string? CheminFichier(Manuel manuel) => stockage.Existe(manuel.Fichier) ? stockage.Chemin(manuel.Fichier) : null;

    /// <summary>Ajoute un manuel : vérifie la fiche, copie le PDF, enregistre. <paramref name="nbPages"/> est lu par l'écran.</summary>
    public async Task<ResultatOperation> AjouterAsync(FicheManuel fiche, string cheminPdf, int nbPages, CancellationToken ct = default)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        if (string.IsNullOrWhiteSpace(cheminPdf) || !File.Exists(cheminPdf))
        {
            return ResultatOperation.Echec("Choisissez le fichier PDF du manuel.");
        }

        if (!string.Equals(Path.GetExtension(cheminPdf), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return ResultatOperation.Echec("Le manuel doit être un fichier PDF.");
        }

        if (nbPages < 1)
        {
            return ResultatOperation.Echec("Ce fichier PDF ne peut pas être lu.");
        }

        var manuel = new Manuel { NbPages = nbPages };
        if (await RemplirAsync(manuel, fiche, ct) is { } erreur)
        {
            return erreur;
        }

        try
        {
            manuel.Fichier = stockage.Importer(cheminPdf);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ResultatOperation.Echec("Le fichier n'a pas pu être copié. Vérifiez qu'il n'est pas ouvert ailleurs.");
        }

        var id = await manuels.AjouterAsync(manuel, ct);
        return ResultatOperation.Ok($"Manuel « {manuel.Titre} » ajouté.", id);
    }

    /// <summary>Modifie la fiche d'un manuel (le PDF reste le même).</summary>
    public async Task<ResultatOperation> ModifierAsync(long id, FicheManuel fiche, CancellationToken ct = default)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        if (await manuels.ObtenirAsync(id, ct) is not { } manuel)
        {
            return ResultatOperation.Echec("Ce manuel n'existe plus.");
        }

        if (await RemplirAsync(manuel, fiche, ct) is { } erreur)
        {
            return erreur;
        }

        await manuels.MettreAJourAsync(manuel, ct);
        return ResultatOperation.Ok($"Manuel « {manuel.Titre} » enregistré.", id);
    }

    /// <summary>Retire un manuel et son fichier PDF du dossier des manuels.</summary>
    public async Task<ResultatOperation> SupprimerAsync(long id, CancellationToken ct = default)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        if (await manuels.ObtenirAsync(id, ct) is not { } manuel)
        {
            return ResultatOperation.Echec("Ce manuel n'existe plus.");
        }

        await manuels.SupprimerAsync(id, ct);
        try
        {
            stockage.Supprimer(manuel.Fichier);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fichier ouvert ailleurs : le manuel est retiré de la liste, le fichier restera dans le dossier.
        }

        return ResultatOperation.Ok($"Manuel « {manuel.Titre} » retiré.");
    }

    /// <summary>
    /// Délai du fabricant comparé à celui du club. Null si le manuel n'a pas de délai ou pas de catégorie, ou pour les
    /// blocs (TIV et requalification ont leurs propres délais). Conforme : le club révise au moins aussi souvent.
    /// </summary>
    public ConformiteEntretien? Conformite(Manuel manuel)
    {
        if (manuel.MoisRevision is not > 0 || string.IsNullOrWhiteSpace(manuel.Categorie)
            || manuel.Categorie.StartsWith("Bloc", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var fabricant = manuel.MoisRevision.Value;
        var club = ServiceRevisions.MoisRevision(reglages, manuel.Categorie);
        var famille = manuel.Categorie.ToLowerInvariant();
        if (club == 0)
        {
            return new ConformiteEntretien(fabricant, 0, false,
                $"Le club n'impose pas de date de révision pour les {famille}, le fabricant en demande une {DureeLisible(fabricant)}.");
        }

        return club <= fabricant
            ? new ConformiteEntretien(fabricant, club, true, $"Les {famille} du club sont à réviser {DureeLisible(club)}.")
            : new ConformiteEntretien(fabricant, club, false,
                $"Le club révise les {famille} {DureeLisible(club)}, le fabricant demande une révision {DureeLisible(fabricant)}.");
    }

    /// <summary>Règle le délai de révision du club pour la catégorie du manuel sur celui du fabricant (action protégée Paramètres).</summary>
    public ResultatOperation AlignerRevision(Manuel manuel)
    {
        if (Conformite(manuel) is not { Conforme: false } conformite)
        {
            return ResultatOperation.Echec("Le délai du club est déjà conforme au fabricant.");
        }

        var categorie = manuel.Categorie!.Trim();
        var resultat = serviceReglages.Modifier(r =>
        {
            var cle = r.MoisEntreRevisionsParCategorie.Keys.FirstOrDefault(k => string.Equals(k.Trim(), categorie, StringComparison.OrdinalIgnoreCase)) ?? categorie;
            r.MoisEntreRevisionsParCategorie[cle] = conformite.MoisFabricant;
        });
        return resultat.Reussi
            ? ResultatOperation.Ok($"Les {categorie.ToLowerInvariant()} sont maintenant à réviser {DureeLisible(conformite.MoisFabricant)}.")
            : resultat;
    }

    /// <summary>« chaque année », « tous les 2 ans », « tous les 6 mois ».</summary>
    public static string DureeLisible(int mois) => mois switch
    {
        12 => "chaque année",
        1 => "chaque mois",
        _ when mois % 12 == 0 => $"tous les {mois / 12} ans",
        _ => $"tous les {mois} mois",
    };

    /// <summary>Vérifie la fiche et la recopie dans le manuel ; retourne l'erreur à afficher, ou null.</summary>
    private async Task<ResultatOperation?> RemplirAsync(Manuel manuel, FicheManuel fiche, CancellationToken ct)
    {
        var marque = (fiche.Marque ?? "").Trim();
        var titre = (fiche.Titre ?? "").Trim();
        var modeles = (fiche.Modeles ?? "").Trim();
        var edition = (fiche.Edition ?? "").Trim();
        if (marque.Length == 0)
        {
            return ResultatOperation.Echec("Indiquez la marque.");
        }

        if (titre.Length == 0)
        {
            return ResultatOperation.Echec("Indiquez le titre du manuel.");
        }

        if (marque.Length > Manuel.LongueurMaxMarque || titre.Length > Manuel.LongueurMaxTitre
            || modeles.Length > Manuel.LongueurMaxModeles || edition.Length > Manuel.LongueurMaxEdition)
        {
            return ResultatOperation.Echec($"Texte trop long : marque {Manuel.LongueurMaxMarque} caractères, titre {Manuel.LongueurMaxTitre}, modèles {Manuel.LongueurMaxModeles}, édition {Manuel.LongueurMaxEdition}.");
        }

        if (fiche.CategorieId is { } categorieId && (await categories.ListerAsync(ct)).All(c => c.Id != categorieId))
        {
            return ResultatOperation.Echec("Cette catégorie n'existe plus.");
        }

        if (fiche.MoisRevision is { } mois && (mois < 1 || mois > Manuel.MoisRevisionMax))
        {
            return ResultatOperation.Echec($"Révision du fabricant : entre 1 et {Manuel.MoisRevisionMax} mois.");
        }

        if (fiche.PageEntretien is { } pageEntretien && (pageEntretien < 1 || pageEntretien > manuel.NbPages))
        {
            return ResultatOperation.Echec($"Page de l'entretien : entre 1 et {manuel.NbPages}, le nombre de pages du PDF.");
        }

        var (sections, erreur) = SectionManuel.Lire(fiche.Sommaire);
        if (erreur is not null)
        {
            return ResultatOperation.Echec(erreur);
        }

        if (sections.FirstOrDefault(s => s.Page > manuel.NbPages) is { } horsPdf)
        {
            return ResultatOperation.Echec($"Sommaire : « {horsPdf.Titre} » est indiqué page {horsPdf.Page}, le PDF n'a que {manuel.NbPages} pages.");
        }

        manuel.Marque = marque;
        manuel.Titre = titre;
        manuel.CategorieId = fiche.CategorieId;
        manuel.Modeles = modeles;
        manuel.Edition = edition;
        manuel.MoisRevision = fiche.MoisRevision;
        manuel.PageEntretien = fiche.PageEntretien;
        manuel.Entretien = string.Join("\n", (fiche.Entretien ?? "").Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        manuel.Sommaire = string.Join("\n", sections.Select(s => $"{s.Titre} ; {s.Page}"));
        return null;
    }
}
