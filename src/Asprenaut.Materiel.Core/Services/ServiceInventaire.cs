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
        var fiche = Fiche(modifie, categorie.Nom);
        Noter("code", actuel.Code, code);
        Noter("nom", actuel.Libelle, modifie.Libelle.Trim());
        Noter("catégorie", actuel.CategorieId, modifie.CategorieId);
        Noter("taille", actuel.Taille, taille);
        Noter("révision", actuel.DateProchaineRevision, prochaine);
        Noter("requalification", actuel.DateRequalification, requalification);
        Noter("notes", actuel.Notes, (modifie.Notes ?? "").Trim());
        Noter("fiche", ResumeFiche(actuel), ResumeFiche(fiche));
        Noter(fiche.Nitrox ? "nitrox" : "plus nitrox", actuel.Nitrox, fiche.Nitrox);
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

    /// <summary>Seuls les blocs et les détendeurs peuvent être « nitrox » (case de la fiche ; vidée pour les autres catégories).</summary>
    public static bool PeutEtreNitrox(string? categorie) => EstDetendeur(categorie) || ServiceRevisions.EstBloc(categorie ?? "");

    /// <summary>Catégorie « Détendeurs » : montage, deuxième étage, D.S, manomètre et octopus.</summary>
    public static bool EstDetendeur(string? categorie)
    {
        var c = (categorie ?? "").Trim();
        return c.StartsWith("Détendeur", StringComparison.OrdinalIgnoreCase) || c.StartsWith("Detendeur", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Composant des interventions qui visent tout l'équipement (aucun composant).</summary>
    public const string ComposantComplet = "Équipement complet";

    /// <summary>
    /// Composants suivis d'un équipement (fiche de suivi, interventions) : pour un détendeur, le premier étage puis le deuxième
    /// étage, l'octopus et le manomètre s'ils sont renseignés dans la fiche ; aucun pour les autres équipements.
    /// </summary>
    public static IReadOnlyList<string> ComposantsDe(Equipement e, string? categorie)
    {
        if (!EstDetendeur(categorie))
        {
            return [];
        }

        var composants = new List<string> { "Premier étage" };
        if (Renseigne(e.MarqueDeuxiemeEtage, e.ModeleDeuxiemeEtage, e.NumeroDeuxiemeEtage))
        {
            composants.Add("Deuxième étage");
        }

        if (Renseigne(e.Octopus, e.NumeroOctopus))
        {
            composants.Add("Octopus");
        }

        if (Renseigne(e.Manometre))
        {
            composants.Add("Manomètre");
        }

        return composants;

        static bool Renseigne(params string[] valeurs) => valeurs.Any(v => !string.IsNullOrWhiteSpace(v));
    }

    /// <summary>Composants qu'on peut ajouter à un détendeur (le premier étage est l'équipement lui-même).</summary>
    public static readonly IReadOnlyList<string> ComposantsAjoutables = ["Deuxième étage", "Octopus", "Manomètre"];

    /// <summary>« Composition actuelle » d'un détendeur : les quatre composants, présents ou non, avec leur modèle.</summary>
    public static IReadOnlyList<EtatComposant> Composition(Equipement e)
    {
        var presents = ComposantsDe(e, "Détendeurs");
        static string Joindre(params string[] p) => string.Join(" ", p.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
        return
        [
            new("Premier étage", true, Joindre(e.Modele).Length > 0 ? Joindre(e.Modele) : Joindre(e.Marque)),
            new("Deuxième étage", presents.Contains("Deuxième étage"), Joindre(e.ModeleDeuxiemeEtage).Length > 0 ? Joindre(e.ModeleDeuxiemeEtage) : Joindre(e.MarqueDeuxiemeEtage)),
            new("Octopus", presents.Contains("Octopus"), Joindre(e.Octopus)),
            new("Manomètre", presents.Contains("Manomètre"), Joindre(e.Manometre)),
        ];
    }

    /// <summary>Intervention notée dans l'historique quand on ajoute un composant : « Montage octopus ».</summary>
    public static string NatureMontage(string role) => $"Montage {role.ToLower(Libelles.Culture)}";

    /// <summary>
    /// Modèles déjà connus dans l'inventaire pour ce composant, les plus fréquents d'abord (6 au plus) : deuxièmes étages et
    /// octopus pour un deuxième étage ou un octopus (ce sont les mêmes pièces), marques de manomètre pour un manomètre.
    /// </summary>
    public async Task<IReadOnlyList<string>> ModelesConnusAsync(string role, CancellationToken ct = default)
    {
        var lignes = await equipements.ListerInventaireAsync(null, null, ct);
        var modeles = role == "Manomètre"
            ? lignes.Select(l => l.Manometre)
            : lignes.SelectMany(l => new[] { l.ModeleDeuxiemeEtage, l.Octopus });
        return modeles.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim())
            .GroupBy(m => m, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First()).Take(6).ToList();
    }

    /// <summary>Lit un mois « 03/2019 » (ou « 3/2019 ») : premier jour du mois ; null si vide ; erreur si illisible.</summary>
    public static (DateOnly? Date, bool Valide) LireMoisAnnee(string? texte)
    {
        var t = (texte ?? "").Trim();
        if (t.Length == 0)
        {
            return (null, true);
        }

        var morceaux = t.Split('/', '-', '.', ' ');
        return morceaux.Length == 2
            && int.TryParse(morceaux[0], out var mois) && mois is >= 1 and <= 12
            && int.TryParse(morceaux[1], out var annee) && annee is >= 1950 and <= 2200
            ? (new DateOnly(annee, mois, 1), true)
            : (null, false);
    }

    /// <summary>
    /// Ajoute un composant à un détendeur (fiche de suivi, action protégée Matériel) : deuxième étage, octopus ou manomètre
    /// absent jusque-là ; modèle obligatoire (marque pour un manomètre) ; fabrication « MM/AAAA » et mise en service ni dans le
    /// futur ni la mise en service avant la fabrication. Le modèle et le numéro vont dans la fiche matériel, le reste dans la
    /// table composant. Option : intervention « Montage octopus » (conforme, sur ce composant) datée de la mise en service.
    /// </summary>
    public async Task<ResultatOperation> AjouterComposantAsync(long equipementId, NouveauComposant n, CancellationToken ct = default)
    {
        if (gestion?.Exiger(ActionProtegee.Materiel) is { Reussi: false } refus)
        {
            return refus;
        }

        if (await equipements.ObtenirAsync(equipementId, ct) is not { } e)
        {
            return ResultatOperation.Echec("Équipement introuvable.");
        }

        var categorie = (await categories.ListerAsync(ct)).FirstOrDefault(c => c.Id == e.CategorieId)?.Nom;
        if (!EstDetendeur(categorie))
        {
            return ResultatOperation.Echec("Seul un détendeur a des composants à ajouter.");
        }

        if (e.Etat == EtatEquipement.Reforme)
        {
            return ResultatOperation.Echec($"{e.Code} est réformé.");
        }

        var role = ComposantsAjoutables.FirstOrDefault(r => string.Equals(r, (n.Role ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        if (role is null)
        {
            return ResultatOperation.Echec("Choisissez le composant à ajouter.");
        }

        if (ComposantsDe(e, categorie).Contains(role))
        {
            return ResultatOperation.Echec($"{e.Code} a déjà un {role.ToLower(Libelles.Culture)}.");
        }

        var modele = (n.Modele ?? "").Trim();
        if (modele.Length == 0)
        {
            return ResultatOperation.Echec(role == "Manomètre" ? "Indiquez la marque du manomètre." : "Indiquez le modèle.");
        }

        var (fabrication, lisible) = LireMoisAnnee(n.Fabrication);
        if (!lisible)
        {
            return ResultatOperation.Echec("Date de fabrication : écrivez le mois et l'année, comme 03/2019.");
        }

        var aujourdHui = horloge.AujourdHui;
        if (fabrication > aujourdHui)
        {
            return ResultatOperation.Echec("La date de fabrication ne peut pas être dans le futur.");
        }

        if (n.MiseEnService > aujourdHui)
        {
            return ResultatOperation.Echec("La mise en service ne peut pas être dans le futur.");
        }

        if (n.MiseEnService is { } service && fabrication is { } fab && service < fab)
        {
            return ResultatOperation.Echec("La mise en service ne peut pas précéder la fabrication.");
        }

        var numero = (n.NumeroSerie ?? "").Trim();
        switch (role)
        {
            case "Deuxième étage":
                e.ModeleDeuxiemeEtage = modele;
                e.NumeroDeuxiemeEtage = numero;
                break;
            case "Octopus":
                e.Octopus = modele;
                e.NumeroOctopus = numero;
                break;
            default:
                e.Manometre = modele;
                break;
        }

        await equipements.MettreAJourAsync(e, ct);
        await equipements.EnregistrerComposantAsync(new DetailComposant
        {
            EquipementId = e.Id,
            Role = role,
            NumeroSerie = role == "Manomètre" ? numero : "",
            Fabrication = fabrication,
            MiseEnService = n.MiseEnService,
            Origine = n.Origine,
        }, ct);

        var origine = n.Origine == OrigineComposant.Neuve ? "pièce neuve" : "récupéré sur un autre détendeur";
        await journal.AjouterAsync(new EntreeJournal
        {
            Horodatage = horloge.MaintenantUtc,
            Action = ActionsJournal.ComposantAjoute,
            Details = $"{e.Code} : {role.ToLower(Libelles.Culture)} {modele} ajouté ({origine})",
            EquipementId = e.Id,
        }, ct);

        if (n.NoterDansHistorique && revisions is not null)
        {
            var details = new List<string> { n.Origine == OrigineComposant.Neuve ? "Pièce neuve" : "Récupéré sur un autre détendeur" };
            if (fabrication is { } f)
            {
                details.Add($"fabriqué en {f.ToString("MM/yyyy", Libelles.Culture)}");
            }

            await revisions.AjouterControleAsync(new Controle
            {
                EquipementId = e.Id,
                Type = TypeControle.Revision,
                DateControle = n.MiseEnService ?? aujourdHui,
                Resultat = ResultatControle.Conforme,
                SaisiLe = horloge.MaintenantUtc,
                Nature = NatureMontage(role),
                Composant = role,
                Notes = string.Join(", ", details),
            }, ct);
        }

        return ResultatOperation.Ok($"{role} {modele} ajouté à {e.Code}.", e.Id);
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
    private static Equipement Fiche(Equipement e, string categorie)
    {
        static string T(string? s) => (s ?? "").Trim();
        var detendeur = EstDetendeur(categorie);
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
            Nitrox = PeutEtreNitrox(categorie) && e.Nitrox,
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
        cible.Nitrox = source.Nitrox;
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

    /// <summary>
    /// Ajoute une catégorie (depuis la fiche d'un équipement). Nom obligatoire, unique sans tenir compte des majuscules,
    /// 30 caractères au plus. Action protégée (Matériel).
    /// </summary>
    public async Task<ResultatOperation> AjouterCategorieAsync(string nom, CancellationToken ct = default)
    {
        if (gestion?.Exiger(ActionProtegee.Materiel) is { Reussi: false } refus)
        {
            return refus;
        }

        var n = (nom ?? "").Trim();
        if (n.Length == 0)
        {
            return ResultatOperation.Echec("Indiquez le nom de la catégorie.");
        }

        if (n.Length > Categorie.LongueurMaxNom)
        {
            return ResultatOperation.Echec($"Nom trop long ({Categorie.LongueurMaxNom} caractères au plus).");
        }

        var tous = await categories.ListerAsync(ct);
        if (tous.Any(c => string.Equals(c.Nom.Trim(), n, StringComparison.CurrentCultureIgnoreCase)))
        {
            return ResultatOperation.Echec($"La catégorie « {n} » existe déjà.");
        }

        var id = await categories.AjouterAsync(new Categorie { Nom = n }, ct);
        return ResultatOperation.Ok($"Catégorie « {n} » ajoutée.", id);
    }

    /// <summary>
    /// Supprime une catégorie que plus aucun équipement ni manuel n'utilise (jamais le matériel existant : on le réaffecte
    /// d'abord). Il en reste toujours au moins une. Action protégée (Matériel).
    /// </summary>
    public async Task<ResultatOperation> SupprimerCategorieAsync(long id, CancellationToken ct = default)
    {
        if (gestion?.Exiger(ActionProtegee.Materiel) is { Reussi: false } refus)
        {
            return refus;
        }

        var tous = await categories.ListerAsync(ct);
        if (tous.FirstOrDefault(c => c.Id == id) is not { } categorie)
        {
            return ResultatOperation.Echec("Cette catégorie n'existe plus.");
        }

        if (tous.Count <= 1)
        {
            return ResultatOperation.Echec("Gardez au moins une catégorie pour créer du matériel.");
        }

        if (await categories.CompterUtilisationsAsync(id, ct) > 0)
        {
            return ResultatOperation.Echec($"« {categorie.Nom} » est utilisée par du matériel ou des manuels : elle ne peut pas être supprimée.");
        }

        // La requête re-vérifie : un équipement créé entre-temps avec cette catégorie empêche la suppression.
        return await categories.SupprimerAsync(id, ct)
            ? ResultatOperation.Ok($"Catégorie « {categorie.Nom} » supprimée.")
            : ResultatOperation.Echec($"« {categorie.Nom} » vient d'être utilisée : elle ne peut pas être supprimée.");
    }

    public Task<IReadOnlyList<LigneInventaire>> ListerAsync(string? recherche, long? categorieId, CancellationToken ct = default) =>
        equipements.ListerInventaireAsync(string.IsNullOrWhiteSpace(recherche) ? null : recherche.Trim(), categorieId, ct);

    /// <summary>
    /// Code proposé pour un nouvel équipement de cette catégorie : le préfixe déjà utilisé dans la catégorie
    /// et le numéro suivant, dans la même forme (BLOC-034 → BLOC-035, A24 → A25). Catégorie vide : forme par défaut
    /// (A01 pour les détendeurs, S01 pour les gilets, BLOC-001…).
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
        string separateur;
        int suivant;
        int chiffres;
        if (codes.Count > 0)
        {
            prefixe = codes.GroupBy(c => c.Prefixe).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).First().Key;
            var memes = codes.Where(c => c.Prefixe == prefixe).ToList();
            separateur = memes.GroupBy(c => c.Separateur).OrderByDescending(g => g.Count()).First().Key;
            suivant = memes.Max(c => c.Numero) + 1;
            chiffres = memes.Max(c => c.Chiffres);
        }
        else
        {
            var nom = (await categories.ListerAsync(ct)).FirstOrDefault(c => c.Id == categorieId)?.Nom ?? "";
            prefixe = PrefixeDeCategorie(nom);
            var club = prefixe.Length == 1; // Étiquettes du club : A01, E01, S01.
            separateur = club ? "" : "-";
            suivant = 1;
            chiffres = club ? 2 : 3;
        }

        // Au cas où le numéro serait déjà pris dans une autre catégorie.
        string code;
        do
        {
            code = $"{prefixe}{separateur}{suivant.ToString(new string('0', chiffres), System.Globalization.CultureInfo.InvariantCulture)}";
            suivant++;
        }
        while (tous.Contains(code));

        return code;
    }

    /// <summary>
    /// Catégorie probable d'un code scanné inconnu : celle du type écrit sur la médaille (« (Detendeur) A24 … »),
    /// sinon celle des équipements qui ont le même préfixe (S10 → Gilets),
    /// sinon celle dont un préfixe connu correspond (A, E → Détendeurs ; S → Gilets ; BLOC → Blocs), sinon « Autre »
    /// (ou null s'il n'y en a pas).
    /// </summary>
    public async Task<long?> DevinerCategorieAsync(string codeBrut, string? typeLu = null, CancellationToken ct = default)
    {
        var (code, typeMedaille) = CodeScanne.Lire(codeBrut, reglages.PrefixeEtiquette);
        var type = string.IsNullOrWhiteSpace(typeLu) ? typeMedaille : typeLu;
        var liste = await categories.ListerAsync(ct);

        // Médaille « (Detendeur) A24 … » : le type écrit dessus désigne la catégorie (Detendeur → Détendeurs, Stab → Gilets).
        if (type is not null && liste.FirstOrDefault(c => PrefixeDeCategorie(c.Nom) == PrefixeDeCategorie(type)) is { } parType)
        {
            return parType.Id;
        }

        var prefixe = DecouperCode(code)?.Prefixe ?? (code.Contains('-') ? code[..code.LastIndexOf('-')] : code);
        var memePrefixe = (await equipements.ListerInventaireAsync(null, null, ct))
            .Where(l => DecouperCode(l.Code)?.Prefixe == prefixe)
            .GroupBy(l => l.CategorieId)
            .OrderByDescending(g => g.Count())
            .Select(g => (long?)g.Key)
            .FirstOrDefault();
        return memePrefixe
            ?? liste.FirstOrDefault(c => prefixe.Length > 0 && PrefixesDeCategorie(c.Nom).Contains(prefixe))?.Id
            ?? liste.FirstOrDefault(c => string.Equals(c.Nom, "Autre", StringComparison.OrdinalIgnoreCase))?.Id;
    }

    /// <summary>
    /// Nom proposé pour un code scanné inconnu, d'après les étiquettes du club : A24 → « Détendeur adulte »,
    /// E03 → « Détendeur enfant », S12 → « Gilet stabilisateur » ; sinon vide.
    /// </summary>
    public static string NomPropose(string code) => DecouperCode(code)?.Prefixe switch
    {
        "A" => "Détendeur adulte",
        "E" => "Détendeur enfant",
        "S" => "Gilet stabilisateur",
        _ => "",
    };

    /// <summary>
    /// Préfixe par défaut d'une catégorie : « Détendeurs » → A (adulte), « Gilets » → S (stab), comme les étiquettes
    /// du club ; « Blocs » → BLOC ; sinon 4 lettres sans accent.
    /// </summary>
    public static string PrefixeDeCategorie(string nom) => PrefixesDeCategorie(nom)[0];

    /// <summary>Préfixes reconnus pour une catégorie (le premier est celui proposé) ; DET et GIL restent reconnus.</summary>
    private static string[] PrefixesDeCategorie(string nom)
    {
        var simple = new string(nom.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => char.IsLetter(c) && System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray()).ToUpperInvariant();
        return simple switch
        {
            var s when s.StartsWith("BLOC", StringComparison.Ordinal) => ["BLOC"],
            var s when s.StartsWith("GILET", StringComparison.Ordinal) => ["S", "GIL"],
            var s when s.StartsWith("STAB", StringComparison.Ordinal) => ["S", "GIL"],
            var s when s.StartsWith("DETENDEUR", StringComparison.Ordinal) => ["A", "E", "DET"],
            var s when s.StartsWith("COMBI", StringComparison.Ordinal) => ["COMB"],
            var s when s.StartsWith("PALME", StringComparison.Ordinal) => ["PALM"],
            var s when s.StartsWith("MASQUE", StringComparison.Ordinal) => ["MASQ"],
            var s when s.StartsWith("ORDI", StringComparison.Ordinal) => ["ORDI"],
            "" => ["EQ"],
            var s => [s[..Math.Min(4, s.Length)]],
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

        CopierFiche(Fiche(nouveau, categorie.Nom), nouveau);
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

    /// <summary>
    /// « BLOC-031 » → (BLOC, « - », 31, 3 chiffres) ; « A24 » → (A, « », 24, 2 chiffres) : lettres, tiret facultatif,
    /// puis le numéro. Null si le code n'a pas cette forme.
    /// </summary>
    private static (string Prefixe, string Separateur, int Numero, int Chiffres)? DecouperCode(string code)
    {
        var debut = code.Length;
        while (debut > 0 && char.IsAsciiDigit(code[debut - 1]))
        {
            debut--;
        }

        var numero = code[debut..];
        var fin = debut > 0 && code[debut - 1] == '-' ? debut - 1 : debut;
        var prefixe = code[..fin].ToUpperInvariant();
        if (numero.Length == 0 || numero.Length > 9 || prefixe.Length == 0 || !prefixe.Any(char.IsLetter))
        {
            return null;
        }

        return (prefixe, fin == debut ? "" : "-", int.Parse(numero, System.Globalization.CultureInfo.InvariantCulture), numero.Length);
    }
}
