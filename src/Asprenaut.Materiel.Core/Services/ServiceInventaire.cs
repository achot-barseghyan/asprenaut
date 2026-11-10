using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Saisie;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>Inventaire : consultation, création, modification et export des équipements.</summary>
public sealed class ServiceInventaire(
    IEquipementRepository equipements,
    ICategorieRepository categories,
    IJournalRepository journal,
    IHorloge horloge,
    Reglages reglages,
    ServiceGestion? gestion = null,
    ISortieRepository? sorties = null,
    IRevisionRepository? revisions = null)
{
    public Task<Equipement?> ObtenirAsync(long id, CancellationToken ct = default) => equipements.ObtenirAsync(id, ct);

    /// <summary>
    /// Modifie un équipement (nom, code, catégorie, taille, dates, notes, état). Mêmes contrôles qu'à la création,
    /// sauf les dates passées (une révision en retard est un état réel). Passer hors service ou réformé : refusé s'il est
    /// dehors ; état inchangeable s'il est en atelier (c'est son retour d'atelier qui le remet en service).
    /// </summary>
    public async Task<ResultatOperation> ModifierAsync(Equipement modifie, DateOnly? derniereRevision = null, CancellationToken ct = default)
    {
        if (gestion?.Exiger(ActionProtegee.Materiel) is { Reussi: false } refus)
        {
            return refus;
        }

        if (await equipements.ObtenirAsync(modifie.Id, ct) is not { } actuel)
        {
            return ResultatOperation.Echec("Équipement introuvable.");
        }

        var code = CodeScanne.Nettoyer(modifie.Code, reglages.PrefixeEtiquette);
        if (code.Length == 0)
        {
            return ResultatOperation.Echec("Indiquez un code.");
        }

        if (string.IsNullOrWhiteSpace(modifie.Libelle))
        {
            return ResultatOperation.Echec("Indiquez un nom pour l'équipement.");
        }

        var categorie = (await categories.ListerAsync(ct)).FirstOrDefault(c => c.Id == modifie.CategorieId);
        if (categorie is null)
        {
            return ResultatOperation.Echec("Choisissez une catégorie.");
        }

        if (code != actuel.Code && await equipements.ObtenirParCodeAsync(code, ct) is { } autre && autre.Id != actuel.Id)
        {
            return ResultatOperation.Echec($"Le code {code} existe déjà.");
        }

        if (modifie.Etat != actuel.Etat)
        {
            if (!Enum.IsDefined(modifie.Etat))
            {
                return ResultatOperation.Echec("État inconnu.");
            }

            if (revisions is not null && await revisions.ObtenirPassageEnCoursAsync(actuel.Id, ct) is not null)
            {
                return ResultatOperation.Echec($"{actuel.Code} est en atelier : enregistrez son retour dans Révisions.");
            }

            if (modifie.Etat != EtatEquipement.EnService && sorties is not null && await sorties.ObtenirAffectationDehorsAsync(actuel.Id, ct) is not null)
            {
                return ResultatOperation.Echec($"{actuel.Code} est dehors : attendez son retour avant de le mettre hors service.");
            }
        }

        // Dernière révision saisie (registre papier) : contrôle enregistré, prochaine révision recalculée.
        var (prochaine, nouvelleRevision, erreurRevision) = await DerniereRevisionAsync(actuel.Id, categorie.Nom, derniereRevision, modifie.DateProchaineRevision, ct);
        if (erreurRevision is not null)
        {
            return erreurRevision;
        }

        var changements = new List<string>();
        void Noter(string quoi, object? avant, object? apres)
        {
            if (!Equals(avant, apres))
            {
                changements.Add(quoi);
            }
        }

        var taille = string.IsNullOrWhiteSpace(modifie.Taille) ? null : modifie.Taille.Trim();
        var requalification = ServiceRevisions.EstBloc(categorie.Nom) ? modifie.DateRequalification : null;
        var fiche = Fiche(modifie, EstDetendeur(categorie.Nom));
        Noter("code", actuel.Code, code);
        Noter("nom", actuel.Libelle, modifie.Libelle.Trim());
        Noter("catégorie", actuel.CategorieId, modifie.CategorieId);
        Noter("taille", actuel.Taille, taille);
        Noter("révision", actuel.DateProchaineRevision, prochaine);
        Noter("requalification", actuel.DateRequalification, requalification);
        Noter("notes", actuel.Notes, (modifie.Notes ?? "").Trim());
        Noter("fiche", ResumeFiche(actuel), ResumeFiche(fiche));
        Noter($"état : {Libelles.De(modifie.Etat).ToLower(Libelles.Culture)}", actuel.Etat, modifie.Etat);
        if (nouvelleRevision is { } dateRevision)
        {
            changements.Add($"révisé le {Libelles.Date(dateRevision)}");
        }

        if (changements.Count == 0)
        {
            return ResultatOperation.Ok("Rien n'a changé.", actuel.Id);
        }

        var ancienCode = actuel.Code;
        actuel.Code = code;
        actuel.Libelle = modifie.Libelle.Trim();
        actuel.CategorieId = modifie.CategorieId;
        actuel.Taille = taille;
        actuel.DateProchaineRevision = prochaine;
        actuel.DateRequalification = requalification;
        actuel.Notes = (modifie.Notes ?? "").Trim();
        actuel.Etat = modifie.Etat;
        CopierFiche(fiche, actuel);
        await equipements.MettreAJourAsync(actuel, ct);
        await EnregistrerRevisionAsync(actuel.Id, nouvelleRevision, ct);
        await journal.AjouterAsync(new EntreeJournal
        {
            Horodatage = horloge.MaintenantUtc,
            Action = ActionsJournal.EquipementModifie,
            Details = $"{ancienCode}{(ancienCode != code ? $" → {code}" : "")} modifié ({string.Join(", ", changements)})",
            EquipementId = actuel.Id,
        }, ct);
        return ResultatOperation.Ok($"{code} · {actuel.Libelle} modifié.", actuel.Id);
    }

    // ---------------------------------------------------------------- Fiche matériel

    /// <summary>Catégorie « Détendeurs » : montage, deuxième étage, D.S, manomètre et octopus.</summary>
    public static bool EstDetendeur(string? categorie)
    {
        var c = (categorie ?? "").Trim();
        return c.StartsWith("Détendeur", StringComparison.OrdinalIgnoreCase) || c.StartsWith("Detendeur", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Catégorie « Gilets » (stabs) : marque, modèle, taille, numéro de série.</summary>
    public static bool EstGilet(string? categorie) =>
        (categorie ?? "").Trim().StartsWith("Gilet", StringComparison.OrdinalIgnoreCase)
        || (categorie ?? "").Trim().StartsWith("Stab", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Identification marquée sur le matériel, comme dans les registres du club :
    /// « (Stab) S01 taille XL Asprenaut Chalon 14 71-0025 », « (Detendeur) A01 Asprenaut Chalon 14 71-0025 ».
    /// </summary>
    public string Identification(string categorie, string code, string? taille)
    {
        var genre = EstGilet(categorie) ? "Stab"
            : EstDetendeur(categorie) ? "Detendeur"
            : categorie.Trim().TrimEnd('s', 'S');
        var morceaux = new List<string> { $"({genre}) {code}" };
        if (!string.IsNullOrWhiteSpace(taille) && !EstDetendeur(categorie))
        {
            morceaux.Add($"taille {taille.Trim()}");
        }

        morceaux.Add(reglages.NomClub.Trim());
        morceaux.Add(reglages.NumeroClub.Trim());
        return string.Join(" ", morceaux.Where(m => m.Length > 0));
    }

    /// <summary>Copie nettoyée de la fiche : textes sans espaces superflus, champs du détendeur vidés pour les autres catégories.</summary>
    private static Equipement Fiche(Equipement e, bool detendeur)
    {
        static string T(string? s) => (s ?? "").Trim();
        return new Equipement
        {
            Marque = T(e.Marque),
            Modele = T(e.Modele),
            NumeroSerie = T(e.NumeroSerie),
            Montage = detendeur && e.Montage is { } m && Enum.IsDefined(m) ? m : null,
            MarqueDeuxiemeEtage = detendeur ? T(e.MarqueDeuxiemeEtage) : "",
            ModeleDeuxiemeEtage = detendeur ? T(e.ModeleDeuxiemeEtage) : "",
            NumeroDeuxiemeEtage = detendeur ? T(e.NumeroDeuxiemeEtage) : "",
            DirectSystem = detendeur ? e.DirectSystem : null,
            Manometre = detendeur ? T(e.Manometre) : "",
            Octopus = detendeur ? T(e.Octopus) : "",
            NumeroOctopus = detendeur ? T(e.NumeroOctopus) : "",
        };
    }

    private static void CopierFiche(Equipement source, Equipement cible)
    {
        cible.Marque = source.Marque;
        cible.Modele = source.Modele;
        cible.NumeroSerie = source.NumeroSerie;
        cible.Montage = source.Montage;
        cible.MarqueDeuxiemeEtage = source.MarqueDeuxiemeEtage;
        cible.ModeleDeuxiemeEtage = source.ModeleDeuxiemeEtage;
        cible.NumeroDeuxiemeEtage = source.NumeroDeuxiemeEtage;
        cible.DirectSystem = source.DirectSystem;
        cible.Manometre = source.Manometre;
        cible.Octopus = source.Octopus;
        cible.NumeroOctopus = source.NumeroOctopus;
    }

    private static string ResumeFiche(Equipement e) => string.Join("|",
        e.Marque, e.Modele, e.NumeroSerie, e.Montage, e.MarqueDeuxiemeEtage, e.ModeleDeuxiemeEtage, e.NumeroDeuxiemeEtage,
        e.DirectSystem, e.Manometre, e.Octopus, e.NumeroOctopus);

    /// <summary>
    /// Date de la dernière révision saisie dans la fiche (hors blocs, qui passent par la TIV) : pas dans le futur,
    /// pas avant une révision déjà enregistrée. Elle fixe la prochaine révision (+ délai de la catégorie : 2 ans pour
    /// un détendeur) ; sans délai (gilets), la date tapée est gardée. Retourne (prochaine révision, révision à enregistrer, erreur).
    /// </summary>
    private async Task<(DateOnly? Prochaine, DateOnly? AEnregistrer, ResultatOperation? Erreur)> DerniereRevisionAsync(
        long? equipementId, string categorie, DateOnly? derniereRevision, DateOnly? prochaineTapee, CancellationToken ct)
    {
        if (derniereRevision is not { } date || ServiceRevisions.EstBloc(categorie))
        {
            return (prochaineTapee, null, null);
        }

        if (date > horloge.AujourdHui)
        {
            return (null, null, ResultatOperation.Echec("La date de la dernière révision ne peut pas être dans le futur."));
        }

        var derniere = equipementId is { } id && revisions is not null
            ? (await revisions.ListerControlesAsync(id, ct)).Where(c => c.Type == TypeControle.Revision).Select(c => (DateOnly?)c.DateControle).Max()
            : null;
        if (derniere is { } connue && date < connue)
        {
            return (null, null, ResultatOperation.Echec($"Une révision plus récente est déjà enregistrée (le {Libelles.Date(connue)})."));
        }

        var mois = ServiceRevisions.MoisRevision(reglages, categorie);
        var prochaine = mois > 0 ? date.AddMonths(mois) : prochaineTapee;
        return (prochaine, date == derniere ? null : date, null);
    }

    /// <summary>La révision saisie devient un contrôle « révision conforme » (historique, registre, compteur d'utilisation).</summary>
    private async Task EnregistrerRevisionAsync(long equipementId, DateOnly? date, CancellationToken ct)
    {
        if (date is { } d && revisions is not null)
        {
            await revisions.AjouterControleAsync(new Controle
            {
                EquipementId = equipementId,
                Type = TypeControle.Revision,
                DateControle = d,
                Resultat = ResultatControle.Conforme,
                Notes = "Saisie dans l'inventaire",
                SaisiLe = horloge.MaintenantUtc,
            }, ct);
        }
    }

    /// <summary>Export CSV des lignes affichées (inventaire filtré ou complet), avec la fiche matériel.</summary>
    public string ExporterCsv(IEnumerable<LigneInventaire> lignes) => Csv.Ecrire(
        ["Code", "Équipement", "Catégorie", "Marque", "Modèle", "Taille", "N° série", "État", "Statut", "Sortie en cours",
         "Date de la dernière révision", "Prochaine révision / TIV", "Prochaine requalification",
         "Étrier / DIN", "Marque deuxième étage", "Deuxième étage", "N° deuxième étage", "D.S", "Mano", "Octopus", "N° octopus", "Identification"],
        lignes.Select(l => new[]
        {
            l.Code,
            l.Libelle,
            l.Categorie,
            l.Marque,
            l.Modele,
            l.Taille ?? "",
            l.NumeroSerie,
            Libelles.De(l.Etat),
            Libelles.De(l.Statut),
            l.SortieEnCoursIntitule ?? "",
            l.DerniereRevision is { } d ? Libelles.Date(d) : "",
            l.DateProchaineRevision is { } r ? Libelles.Date(r) : "",
            l.DateRequalification is { } q ? Libelles.Date(q) : "",
            l.Montage is { } m ? Libelles.De(m) : "",
            l.MarqueDeuxiemeEtage,
            l.ModeleDeuxiemeEtage,
            l.NumeroDeuxiemeEtage,
            l.DirectSystem is { } ds ? (ds ? "Oui" : "Non") : "",
            l.Manometre,
            l.Octopus,
            l.NumeroOctopus,
            Identification(l.Categorie, l.Code, l.Taille),
        }));

    /// <summary>Nom de fichier proposé : inventaire_2026-11-10.csv.</summary>
    public string NomFichierExport() => $"inventaire_{horloge.AujourdHui:yyyy-MM-dd}.csv";
    /// <summary>
    /// Révision dépassée, proche (dans <see cref="Reglages.JoursAvantRevision"/> jours) ou à jour.
    /// Sert au badge jaune / rouge de l'inventaire.
    /// </summary>
    public EtatRevision EtatDeRevision(DateOnly? prochaineRevision)
    {
        if (prochaineRevision is not { } date)
        {
            return EtatRevision.AJour;
        }

        var aujourdHui = horloge.AujourdHui;
        if (date < aujourdHui)
        {
            return EtatRevision.Depassee;
        }

        return date <= aujourdHui.AddDays(reglages.JoursAvantRevision) ? EtatRevision.Proche : EtatRevision.AJour;
    }

    public Task<IReadOnlyList<Categorie>> ListerCategoriesAsync(CancellationToken ct = default) =>
        categories.ListerAsync(ct);

    public Task<IReadOnlyList<LigneInventaire>> ListerAsync(string? recherche, long? categorieId, CancellationToken ct = default) =>
        equipements.ListerInventaireAsync(string.IsNullOrWhiteSpace(recherche) ? null : recherche.Trim(), categorieId, ct);

    /// <summary>
    /// Code proposé pour un nouvel équipement de cette catégorie : le préfixe déjà utilisé dans la catégorie
    /// et le numéro suivant (BLOC-034 → BLOC-035). Catégorie vide : préfixe tiré de son nom (BLOC-001).
    /// </summary>
    public async Task<string> ProposerCodeAsync(long categorieId, CancellationToken ct = default)
    {
        var codes = (await equipements.ListerInventaireAsync(null, categorieId, ct))
            .Select(l => DecouperCode(l.Code))
            .Where(c => c is not null)
            .Select(c => c!.Value)
            .ToList();
        var tous = (await equipements.ListerInventaireAsync(null, null, ct)).Select(l => l.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

        string prefixe;
        int suivant;
        int chiffres;
        if (codes.Count > 0)
        {
            prefixe = codes.GroupBy(c => c.Prefixe).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).First().Key;
            var memes = codes.Where(c => c.Prefixe == prefixe).ToList();
            suivant = memes.Max(c => c.Numero) + 1;
            chiffres = Math.Max(3, memes.Max(c => c.Chiffres));
        }
        else
        {
            var nom = (await categories.ListerAsync(ct)).FirstOrDefault(c => c.Id == categorieId)?.Nom ?? "";
            prefixe = PrefixeDeCategorie(nom);
            suivant = 1;
            chiffres = 3;
        }

        // Au cas où le numéro serait déjà pris dans une autre catégorie.
        string code;
        do
        {
            code = $"{prefixe}-{suivant.ToString(new string('0', chiffres), System.Globalization.CultureInfo.InvariantCulture)}";
            suivant++;
        }
        while (tous.Contains(code));

        return code;
    }

    /// <summary>
    /// Catégorie probable d'un code scanné inconnu : celle des équipements qui ont le même préfixe (GIL-010 → Gilets),
    /// sinon celle dont le préfixe par défaut correspond, sinon « Autre » (ou null s'il n'y en a pas).
    /// </summary>
    public async Task<long?> DevinerCategorieAsync(string codeBrut, CancellationToken ct = default)
    {
        var code = CodeScanne.Nettoyer(codeBrut, reglages.PrefixeEtiquette);
        var prefixe = DecouperCode(code)?.Prefixe ?? (code.Contains('-') ? code[..code.LastIndexOf('-')] : code);
        var liste = await categories.ListerAsync(ct);
        var memePrefixe = (await equipements.ListerInventaireAsync(null, null, ct))
            .Where(l => DecouperCode(l.Code)?.Prefixe == prefixe)
            .GroupBy(l => l.CategorieId)
            .OrderByDescending(g => g.Count())
            .Select(g => (long?)g.Key)
            .FirstOrDefault();
        return memePrefixe
            ?? liste.FirstOrDefault(c => prefixe.Length > 0 && PrefixeDeCategorie(c.Nom) == prefixe)?.Id
            ?? liste.FirstOrDefault(c => string.Equals(c.Nom, "Autre", StringComparison.OrdinalIgnoreCase))?.Id;
    }

    /// <summary>Préfixe par défaut d'une catégorie : « Blocs » → BLOC, « Détendeurs » → DET, sinon 4 lettres sans accent.</summary>
    public static string PrefixeDeCategorie(string nom)
    {
        var simple = new string(nom.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => char.IsLetter(c) && System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray()).ToUpperInvariant();
        return simple switch
        {
            var s when s.StartsWith("BLOC", StringComparison.Ordinal) => "BLOC",
            var s when s.StartsWith("GILET", StringComparison.Ordinal) => "GIL",
            var s when s.StartsWith("DETENDEUR", StringComparison.Ordinal) => "DET",
            var s when s.StartsWith("COMBI", StringComparison.Ordinal) => "COMB",
            var s when s.StartsWith("PALME", StringComparison.Ordinal) => "PALM",
            var s when s.StartsWith("MASQUE", StringComparison.Ordinal) => "MASQ",
            var s when s.StartsWith("ORDI", StringComparison.Ordinal) => "ORDI",
            "" => "EQ",
            var s => s[..Math.Min(4, s.Length)],
        };
    }

    /// <summary>
    /// Crée un équipement (depuis l'inventaire, ou après le scan d'un code inconnu). Code unique et nettoyé,
    /// nom et catégorie obligatoires ; la requalification ne concerne que les blocs.
    /// </summary>
    public async Task<ResultatOperation> CreerAsync(Equipement nouveau, DateOnly? derniereRevision = null, CancellationToken ct = default)
    {
        if (gestion?.Exiger(ActionProtegee.Materiel) is { Reussi: false } refus)
        {
            return refus;
        }

        var code = CodeScanne.Nettoyer(nouveau.Code, reglages.PrefixeEtiquette);
        if (code.Length == 0)
        {
            return ResultatOperation.Echec("Indiquez un code.");
        }

        if (string.IsNullOrWhiteSpace(nouveau.Libelle))
        {
            return ResultatOperation.Echec("Indiquez un nom pour l'équipement.");
        }

        var categorie = (await categories.ListerAsync(ct)).FirstOrDefault(c => c.Id == nouveau.CategorieId);
        if (categorie is null)
        {
            return ResultatOperation.Echec("Choisissez une catégorie.");
        }

        if (await equipements.ObtenirParCodeAsync(code, ct) is not null)
        {
            return ResultatOperation.Echec($"Le code {code} existe déjà.");
        }

        // Dernière révision connue (registre papier) : elle fixe la prochaine, même si celle-ci est déjà passée.
        var (prochaine, revisionAEnregistrer, erreurRevision) = await DerniereRevisionAsync(null, categorie.Nom, derniereRevision, nouveau.DateProchaineRevision, ct);
        if (erreurRevision is not null)
        {
            return erreurRevision;
        }

        var aujourdHui = horloge.AujourdHui;
        if ((revisionAEnregistrer is null && nouveau.DateProchaineRevision < aujourdHui) || nouveau.DateRequalification < aujourdHui)
        {
            return ResultatOperation.Echec("Une date de prochain contrôle est déjà passée. Vérifiez-la.");
        }

        CopierFiche(Fiche(nouveau, EstDetendeur(categorie.Nom)), nouveau);
        nouveau.DateProchaineRevision = prochaine;
        nouveau.Code = code;
        nouveau.Libelle = nouveau.Libelle.Trim();
        nouveau.Taille = string.IsNullOrWhiteSpace(nouveau.Taille) ? null : nouveau.Taille.Trim();
        nouveau.Notes = (nouveau.Notes ?? "").Trim();
        nouveau.Etat = EtatEquipement.EnService;
        if (!ServiceRevisions.EstBloc(categorie.Nom))
        {
            nouveau.DateRequalification = null;
        }

        nouveau.CreeLe = horloge.MaintenantUtc;
        var id = await equipements.AjouterAsync(nouveau, ct);
        await EnregistrerRevisionAsync(id, revisionAEnregistrer, ct);
        await journal.AjouterAsync(new EntreeJournal
        {
            Horodatage = horloge.MaintenantUtc,
            Action = ActionsJournal.EquipementCree,
            Details = $"{code} « {nouveau.Libelle} » créé",
            EquipementId = id,
        }, ct);
        return ResultatOperation.Ok($"{code} · {nouveau.Libelle} ajouté à l'inventaire.", id);
    }

    /// <summary>« BLOC-031 » → (BLOC, 31, 3 chiffres) ; null si le code n'a pas cette forme.</summary>
    private static (string Prefixe, int Numero, int Chiffres)? DecouperCode(string code)
    {
        var tiret = code.LastIndexOf('-');
        if (tiret <= 0 || tiret == code.Length - 1)
        {
            return null;
        }

        var numero = code[(tiret + 1)..];
        return numero.All(char.IsAsciiDigit) && int.TryParse(numero, out var n)
            ? (code[..tiret].ToUpperInvariant(), n, numero.Length)
            : null;
    }
}
