using System.Globalization;
using System.Text.RegularExpressions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Saisie;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>Import de l'inventaire depuis un fichier Excel (ou CSV), et modèle à remplir.</summary>
public sealed partial class ServiceInventaire
{
    private const string RienNaChange = "Rien n'a changé.";

    // Colonnes reconnues, dans l'ordre de ChampImport. En-têtes simplifiés (minuscules, sans accents, sans « . » ni « ° ») :
    // ceux de l'export CSV de l'inventaire en premier, pour qu'un export se réimporte tel quel.
    private enum ChampImport
    {
        Code, Nom, Categorie, Marque, Modele, Taille, NumeroSerie, DerniereRevision, ProchaineRevision, Requalification,
        Montage, MarqueDeuxiemeEtage, ModeleDeuxiemeEtage, NumeroDeuxiemeEtage, DirectSystem, Manometre, Octopus, NumeroOctopus,
        Nitrox, Notes, Utilisations,
    }

    private static readonly string[][] EnTetesImport =
    [
        ["code", "code etiquette", "medaille", "n medaille", "reference", "ref"],
        ["equipement", "nom", "libelle", "designation", "nom de l'equipement"],
        ["categorie", "famille"],
        ["marque", "marque premier etage"],
        ["modele", "modele premier etage", "premier etage"],
        ["taille"],
        ["n serie", "no serie", "numero de serie", "num serie", "n de serie", "serie", "n serie premier etage"],
        ["date de la derniere revision", "derniere revision", "date derniere revision", "revise le"],
        ["prochaine revision / tiv", "prochaine revision", "prochaine tiv", "a reviser avant", "tiv", "date tiv"],
        ["prochaine requalification", "requalification", "requalif"],
        ["etrier / din", "etrier/din", "montage"],
        ["marque deuxieme etage", "marque 2e etage"],
        ["deuxieme etage", "modele deuxieme etage", "2e etage", "modele 2e etage"],
        ["n deuxieme etage", "numero deuxieme etage", "n 2e etage", "n serie deuxieme etage"],
        ["ds", "direct system"],
        ["mano", "manometre", "marque manometre"],
        ["octopus", "modele octopus"],
        ["n octopus", "numero octopus", "n serie octopus"],
        ["nitrox"],
        ["notes", "note", "remarques", "commentaire", "commentaires", "observations"],
        ["utilisations", "utilisation", "nombre d'utilisations", "compteur", "compteur d'utilisation", "compteur d'utilisations",
         "utilisations depuis la derniere revision", "plongees", "nombre de plongees", "plongees depuis la derniere revision"],
    ];

    /// <summary>Compteur repris le plus grand accepté à l'import.</summary>
    public const int UtilisationsRepriseMax = 10000;

    /// <summary>
    /// Colonnes du modèle « Inventaire à remplir » : exactement celles que l'import reconnaît. Catégorie : liste des
    /// catégories du club ; Étrier / DIN, D.S et Nitrox : listes.
    /// </summary>
    public static IReadOnlyList<ServiceAdherents.ColonneModele> ColonnesModeleInventaire(IReadOnlyList<string> categories) =>
    [
        new("Code", "Code de l'étiquette (ex. A24, S12, BLOC-031) : sert à reconnaître l'équipement à l'import suivant", 14, true),
        new("Équipement", "Nom de l'équipement (ex. Détendeur adulte, Bloc 12 L)", 24, true),
        new("Catégorie", "Choisissez dans la liste (une catégorie inconnue est créée)", 18, true, categories),
        new("Marque", "Marque (premier étage pour un détendeur)", 16, false),
        new("Modèle", "Modèle (premier étage pour un détendeur)", 18, false),
        new("Taille", "Ex. : XL, 12 L", 10, false),
        new("N° série", "Numéro de série (premier étage pour un détendeur)", 18, false),
        new("Date de la dernière révision", "jj/mm/aaaa, hors blocs : la prochaine révision est calculée", 18, false),
        new("Prochaine révision / TIV", "jj/mm/aaaa : prochaine TIV pour un bloc", 18, false),
        new("Prochaine requalification", "jj/mm/aaaa, blocs seulement", 18, false),
        new("Étrier / DIN", "Détendeurs seulement", 12, false, ["Étrier", "DIN"]),
        new("Marque deuxième étage", "Détendeurs seulement", 18, false),
        new("Deuxième étage", "Modèle du deuxième étage (détendeurs)", 18, false),
        new("N° deuxième étage", "Numéro de série du deuxième étage (détendeurs)", 18, false),
        new("D.S", "Direct system (détendeurs)", 8, false, ["Oui", "Non"]),
        new("Mano", "Marque du manomètre (détendeurs) ; vide = sans manomètre", 14, false),
        new("Octopus", "Modèle de l'octopus (détendeurs) ; vide = sans octopus", 16, false),
        new("N° octopus", "Numéro de série de l'octopus", 16, false),
        new("Nitrox", "Blocs et détendeurs seulement", 10, false, ["Oui", "Non"]),
        new("Notes", "Remarques libres", 30, false),
        new("Utilisations", "Utilisations depuis la dernière révision (nombre) : le compteur repart de ce nombre ; vide = inchangé", 14, false),
    ];

    /// <summary>Nom proposé pour le modèle : « Inventaire à remplir - Asprenaut.xlsx ».</summary>
    public static string NomFichierModeleInventaire(string? nomClub) =>
        $"Inventaire à remplir - {(string.IsNullOrWhiteSpace(nomClub) ? "club" : nomClub.Trim())}.xlsx";

    /// <summary>Mode d'emploi écrit dans la seconde feuille du modèle.</summary>
    public static IReadOnlyList<string> ModeEmploiModeleInventaire { get; } =
    [
        "Comment remplir ce fichier",
        "1. Une ligne par équipement, dans la feuille « Inventaire », à partir de la ligne 2.",
        "2. Code, Équipement et Catégorie sont obligatoires pour un nouvel équipement. Les autres colonnes peuvent rester vides.",
        "3. Ne changez pas la première ligne (les titres des colonnes) : l'application s'en sert pour lire le fichier.",
        "4. Un code déjà dans l'inventaire met cet équipement à jour : seules les cases remplies changent.",
        "5. Dates au format jj/mm/aaaa (ex. 15/09/2027).",
        "6. Les colonnes des détendeurs (Étrier / DIN, deuxième étage, D.S, Mano, Octopus) sont ignorées pour les autres catégories.",
        "7. Utilisations : le nombre d'utilisations depuis la dernière révision (ex. 42). Le compteur de l'équipement repart de ce nombre.",
        "8. Enregistrez en .xlsx et rapportez le fichier au responsable matériel : Inventaire › Importer Excel.",
        "Rien n'est supprimé par l'import : le matériel absent du fichier reste dans l'inventaire.",
    ];

    private static string EnTeteSimplifie(string cellule) =>
        ServiceAdherents.Simplifier(cellule.Replace(".", "", StringComparison.Ordinal).Replace("°", " ", StringComparison.Ordinal).Replace('’', '\''));

    /// <summary>
    /// Importe l'inventaire (feuille Excel ou CSV déjà découpé en cellules ; la première ligne qui contient « Code » est
    /// l'en-tête). Code déjà connu : l'équipement est mis à jour avec les cases remplies (mêmes contrôles qu'une
    /// modification). Code nouveau : il est créé (nom et catégorie obligatoires ; une catégorie inconnue est ajoutée ;
    /// échéances passées acceptées). Une ligne en erreur est ignorée et signalée ; rien n'est supprimé. Action Matériel.
    /// </summary>
    public async Task<ResultatOperation> ImporterAsync(IReadOnlyList<IReadOnlyList<string>> tableau, CancellationToken ct = default)
    {
        if (gestion?.Exiger(ActionProtegee.Materiel) is { Reussi: false } refus)
        {
            return refus;
        }

        var entete = -1;
        var colonnes = new int[EnTetesImport.Length];
        for (var i = 0; i < tableau.Count && entete < 0; i++)
        {
            var cellules = tableau[i].Select(EnTeteSimplifie).ToList();
            for (var c = 0; c < EnTetesImport.Length; c++)
            {
                colonnes[c] = cellules.FindIndex(x => EnTetesImport[c].Contains(x));
            }

            if (colonnes[(int)ChampImport.Code] >= 0)
            {
                entete = i;
            }
        }

        if (entete < 0)
        {
            return ResultatOperation.Echec("Aucun équipement trouvé : la première ligne du tableau doit contenir la colonne « Code ».");
        }

        var listeCategories = (await categories.ListerAsync(ct)).ToList();
        int ajoutes = 0, misAJour = 0, inchanges = 0, lignes = 0, repris = 0;
        var categoriesCreees = new List<string>();
        var ignorees = new List<(int Ligne, string Raison)>();
        var codesVus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = entete + 1; i < tableau.Count; i++)
        {
            var ligne = tableau[i];
            if (ligne.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            lignes++;
            var numero = i + 1;
            string Cellule(ChampImport champ) =>
                colonnes[(int)champ] is var c && c >= 0 && c < ligne.Count ? ligne[c].Trim() : "";

            var code = CodeScanne.Nettoyer(Cellule(ChampImport.Code), reglages.PrefixeEtiquette);
            if (code.Length == 0)
            {
                ignorees.Add((numero, "code manquant"));
                continue;
            }

            if (!codesVus.Add(code))
            {
                ignorees.Add((numero, $"{code} déjà plus haut dans le fichier"));
                continue;
            }

            // Cases lisibles ? Une date, un montage ou un oui / non illisible fait ignorer la ligne (rien n'est deviné).
            var erreur = (string?)null;
            DateOnly? Date(ChampImport champ, string nom)
            {
                var texte = Cellule(champ);
                if (texte.Length == 0)
                {
                    return null;
                }

                var d = ServiceAdherents.LireDate(texte);
                erreur ??= d is null ? $"{nom} illisible" : null;
                return d;
            }

            bool? OuiNon(ChampImport champ, string nom)
            {
                var texte = ServiceAdherents.Simplifier(Cellule(champ));
                switch (texte)
                {
                    case "":
                        return null;
                    case "oui" or "o" or "x" or "1" or "vrai" or "yes" or "nitrox":
                        return true;
                    case "non" or "n" or "0" or "faux" or "no":
                        return false;
                    default:
                        erreur ??= $"{nom} : « {Cellule(champ)} » (oui ou non)";
                        return null;
                }
            }

            var derniere = Date(ChampImport.DerniereRevision, "date de la dernière révision");
            var prochaine = Date(ChampImport.ProchaineRevision, "date de la prochaine révision");
            var requalification = Date(ChampImport.Requalification, "date de requalification");
            var directSystem = OuiNon(ChampImport.DirectSystem, "D.S");
            var nitrox = OuiNon(ChampImport.Nitrox, "Nitrox");
            MontageDetendeur? montage = ServiceAdherents.Simplifier(Cellule(ChampImport.Montage)) switch
            {
                "" => null,
                "etrier" or "int" or "yoke" => MontageDetendeur.Etrier,
                "din" => MontageDetendeur.Din,
                _ => null,
            };
            if (montage is null && Cellule(ChampImport.Montage).Length > 0)
            {
                erreur ??= $"montage « {Cellule(ChampImport.Montage)} » (Étrier ou DIN)";
            }

            var utilisations = LireUtilisations(Cellule(ChampImport.Utilisations));
            if (utilisations is null && Cellule(ChampImport.Utilisations).Length > 0)
            {
                erreur ??= $"utilisations « {Cellule(ChampImport.Utilisations)} » (un nombre de 0 à {UtilisationsRepriseMax})";
            }

            if (erreur is not null)
            {
                ignorees.Add((numero, erreur));
                continue;
            }

            // Catégorie : reconnue sans majuscules, accents ni « s » final (« detendeur » = « Détendeurs ») ; sinon créée.
            long? categorieId = null;
            var nomCategorie = Cellule(ChampImport.Categorie);
            if (nomCategorie.Length > 0)
            {
                var cle = CleCategorie(nomCategorie);
                categorieId = listeCategories.FirstOrDefault(c => CleCategorie(c.Nom) == cle)?.Id;
                if (categorieId is null)
                {
                    var creee = await AjouterCategorieAsync(nomCategorie, ct);
                    if (!creee.Reussi || creee.Id is not { } idCategorie)
                    {
                        ignorees.Add((numero, SansPoint(creee.Message)));
                        continue;
                    }

                    listeCategories = (await categories.ListerAsync(ct)).ToList();
                    categorieId = idCategorie;
                    categoriesCreees.Add(listeCategories.First(c => c.Id == idCategorie).Nom);
                }
            }

            var existant = await equipements.ObtenirParCodeAsync(code, ct);
            var e = existant is null ? new Equipement { Code = code } : Copie(existant);

            // Seules les cases remplies changent l'équipement existant.
            void Texte(ChampImport champ, Action<string> poser)
            {
                if (Cellule(champ) is { Length: > 0 } t)
                {
                    poser(t);
                }
            }

            Texte(ChampImport.Nom, t => e.Libelle = t);
            Texte(ChampImport.Marque, t => e.Marque = t);
            Texte(ChampImport.Modele, t => e.Modele = t);
            Texte(ChampImport.Taille, t => e.Taille = t);
            Texte(ChampImport.NumeroSerie, t => e.NumeroSerie = t);
            Texte(ChampImport.MarqueDeuxiemeEtage, t => e.MarqueDeuxiemeEtage = t);
            Texte(ChampImport.ModeleDeuxiemeEtage, t => e.ModeleDeuxiemeEtage = t);
            Texte(ChampImport.NumeroDeuxiemeEtage, t => e.NumeroDeuxiemeEtage = t);
            Texte(ChampImport.Manometre, t => e.Manometre = t);
            Texte(ChampImport.Octopus, t => e.Octopus = t);
            Texte(ChampImport.NumeroOctopus, t => e.NumeroOctopus = t);
            Texte(ChampImport.Notes, t => e.Notes = t);
            e.CategorieId = categorieId ?? e.CategorieId;
            e.DateProchaineRevision = prochaine ?? e.DateProchaineRevision;
            e.DateRequalification = requalification ?? e.DateRequalification;
            e.Montage = montage ?? e.Montage;
            e.DirectSystem = directSystem ?? e.DirectSystem;
            e.Nitrox = nitrox ?? e.Nitrox;

            if (existant is null)
            {
                if (e.Libelle.Length == 0 || categorieId is null)
                {
                    ignorees.Add((numero, $"{code} : {(e.Libelle.Length == 0 ? "nom" : "catégorie")} manquant{(e.Libelle.Length == 0 ? "" : "e")}"));
                    continue;
                }

                var r = await CreerAsync(e, derniere, datesPasseesAcceptees: true, ct);
                if (r.Reussi)
                {
                    ajoutes++;
                    if (r.Id is { } idCree && await ReprendreCompteurAsync(idCree, code, utilisations, ct))
                    {
                        repris++;
                    }
                }
                else
                {
                    ignorees.Add((numero, $"{code} : {Minuscule(SansPoint(r.Message))}"));
                }

                continue;
            }

            var m = await ModifierAsync(e, derniere, ct);
            if (!m.Reussi)
            {
                ignorees.Add((numero, $"{code} : {Minuscule(SansPoint(m.Message))}"));
                continue;
            }

            var compteurRepris = await ReprendreCompteurAsync(existant.Id, code, utilisations, ct);
            repris += compteurRepris ? 1 : 0;
            if (m.Message == RienNaChange && !compteurRepris)
            {
                inchanges++;
            }
            else
            {
                misAJour++;
            }
        }

        if (lignes == 0)
        {
            return ResultatOperation.Echec("Aucun équipement trouvé : le tableau n'a pas de ligne sous les titres des colonnes.");
        }

        var message = $"{Libelles.Pluriel(ajoutes, "équipement ajouté", "équipements ajoutés")}, {Libelles.Pluriel(misAJour, "mis à jour", "mis à jour")}";
        if (inchanges > 0)
        {
            message += $", {Libelles.Pluriel(inchanges, "inchangé")}";
        }

        if (repris > 0)
        {
            message += $", {Libelles.Pluriel(repris, "compteur d'utilisation repris", "compteurs d'utilisation repris")}";
        }

        if (categoriesCreees.Count > 0)
        {
            message += $", {Libelles.Pluriel(categoriesCreees.Count, "catégorie créée", "catégories créées")} ({string.Join(", ", categoriesCreees)})";
        }

        if (ignorees.Count > 0)
        {
            message += $", {Libelles.Pluriel(ignorees.Count, "ligne ignorée", "lignes ignorées")} : "
                + string.Join(" ; ", ignorees.Take(5).Select(x => $"ligne {x.Ligne} ({x.Raison})"))
                + (ignorees.Count > 5 ? " ; …" : "");
        }

        await journal.AjouterAsync(new EntreeJournal
        {
            Horodatage = horloge.MaintenantUtc,
            Action = ActionsJournal.InventaireImporte,
            Details = $"Import de l'inventaire : {message}",
        }, ct);
        return ResultatOperation.Ok($"Import terminé : {message}.", ajoutes + misAJour);
    }

    /// <summary>
    /// Nombre d'utilisations d'une case : « 42 », « 42,0 » (nombre Excel) ou « 64 / 80 utilisations » (colonne de
    /// l'inventaire) ; null si vide, illisible ou au-delà de <see cref="UtilisationsRepriseMax"/>.
    /// </summary>
    public static int? LireUtilisations(string texte)
    {
        var m = Regex.Match(texte.Trim(), @"^(\d{1,6})(?:[.,]0+)?(?:\s*/\s*\d+)?(?:\s*utilisations?)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return m.Success && int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) is var n && n <= UtilisationsRepriseMax ? n : null;
    }

    /// <summary>
    /// Reprend le compteur d'un équipement importé (« il a déjà servi 42 fois depuis sa dernière révision ») : le compteur
    /// repart de ce nombre, puis chaque retour scanné s'ajoute. Rien n'est écrit si le compteur vaut déjà ce nombre.
    /// </summary>
    private async Task<bool> ReprendreCompteurAsync(long equipementId, string code, int? utilisations, CancellationToken ct)
    {
        if (utilisations is not { } valeur)
        {
            return false;
        }

        var avant = (await equipements.ListerCompteursAsync(equipementId, ct)).FirstOrDefault()?.Utilisations ?? 0;
        if (avant == valeur)
        {
            return false;
        }

        await equipements.EnregistrerRepriseAsync(new RepriseUtilisations(equipementId, valeur, horloge.MaintenantUtc), ct);
        await journal.AjouterAsync(new EntreeJournal
        {
            Horodatage = horloge.MaintenantUtc,
            Action = ActionsJournal.UtilisationsReprises,
            EquipementId = equipementId,
            Details = $"{code} : compteur d'utilisation repris à {Libelles.Pluriel(valeur, "utilisation")} (avant : {avant})",
        }, ct);
        return true;
    }

    private static string CleCategorie(string nom) => ServiceAdherents.Simplifier(nom).TrimEnd('s');

    private static string SansPoint(string message) => message.TrimEnd('.', ' ');

    private static string Minuscule(string texte) =>
        texte.Length > 1 && char.IsUpper(texte[0]) && !char.IsUpper(texte[1]) ? char.ToLower(texte[0], Libelles.Culture) + texte[1..] : texte;

    private static Equipement Copie(Equipement e) => new()
    {
        Id = e.Id,
        Code = e.Code,
        Libelle = e.Libelle,
        CategorieId = e.CategorieId,
        Taille = e.Taille,
        Etat = e.Etat,
        DateProchaineRevision = e.DateProchaineRevision,
        DateRequalification = e.DateRequalification,
        Notes = e.Notes,
        CreeLe = e.CreeLe,
        Marque = e.Marque,
        Modele = e.Modele,
        NumeroSerie = e.NumeroSerie,
        Montage = e.Montage,
        MarqueDeuxiemeEtage = e.MarqueDeuxiemeEtage,
        ModeleDeuxiemeEtage = e.ModeleDeuxiemeEtage,
        NumeroDeuxiemeEtage = e.NumeroDeuxiemeEtage,
        DirectSystem = e.DirectSystem,
        Manometre = e.Manometre,
        Octopus = e.Octopus,
        NumeroOctopus = e.NumeroOctopus,
        Nitrox = e.Nitrox,
    };
}
