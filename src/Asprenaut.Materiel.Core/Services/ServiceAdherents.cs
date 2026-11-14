using System.Globalization;
using System.Text;
using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>
/// Adhérents du club (page « Membres du club », onglet Adhérents) : liste et filtres, fiche (matériel dehors, non rendu,
/// dernières sorties nominatives), ajout, modification, import de la liste Excel. Consultation libre ; ajouter, modifier
/// et importer demandent le mode gestion (action Moniteurs). Un adhérent n'est jamais supprimé : il devient inactif.
/// </summary>
public sealed class ServiceAdherents(
    IAdherentRepository adherents,
    IJournalRepository journal,
    IHorloge horloge,
    ServiceGestion? gestion = null,
    Reglages? reglages = null)
{
    /// <summary>Niveaux proposés (réglables dans Paramètres › Configuration du club).</summary>
    public IReadOnlyList<string> Niveaux => reglages?.NiveauxAdherents is { Count: > 0 } liste ? liste : Reglages.NiveauxParDefaut;

    /// <summary>
    /// Niveau de la liste qui correspond à la saisie, sans tenir compte des majuscules, accents, espaces, points et tirets ;
    /// « N2 », « niv 2 », « niveau2 » → « Niveau 2 ». Null si vide ou inconnu.
    /// </summary>
    public static string? NiveauReconnu(string? saisie, IReadOnlyList<string> niveaux)
    {
        static string Cle(string s) => new(Simplifier(s).Where(char.IsLetterOrDigit).ToArray());
        var cle = Cle(saisie ?? "");
        if (cle.Length == 0)
        {
            return null;
        }

        if (niveaux.FirstOrDefault(n => Cle(n) == cle) is { } exact)
        {
            return exact;
        }

        // Abréviations courantes : « n2 », « niv2 », « niveau2 ».
        var abrege = System.Text.RegularExpressions.Regex.Match(cle, "^(n|niv|niveau)([1-4])$");
        return abrege.Success ? niveaux.FirstOrDefault(n => Cle(n) == $"niveau{abrege.Groups[2].Value}") : null;
    }

    /// <summary>Dernières sorties nominatives montrées sur la fiche.</summary>
    public const int NombreSortiesFiche = 5;

    // ---------------------------------------------------------------- Règles du départ

    /// <summary>
    /// Pourquoi cet adhérent ne peut pas prendre cet équipement au départ, ou null s'il le peut :
    /// désactivé (ne sort plus de matériel), CACI coché et expiré, ou équipement nitrox sans certification nitrox.
    /// </summary>
    public static string? RefusPour(Adherent a, bool equipementNitrox, string code, DateOnly aujourdhui) =>
        !a.Actif ? $"{a.NomComplet} est désactivé : il ne peut plus sortir de matériel."
        : a.CaciExpire(aujourdhui) ? $"Le CACI de {a.NomComplet} a expiré le {Libelles.Date(a.CaciExpiration!.Value)} : pas de matériel tant qu'il n'est pas renouvelé."
        : equipementNitrox && !a.Nitrox ? $"{code} est un équipement nitrox : {a.NomComplet} n'a pas la certification nitrox."
        : null;

    /// <summary>
    /// Équipement de la même catégorie (majuscules ignorées) que l'adhérent a déjà dehors dans cette sortie, ou null :
    /// un adhérent ne prend qu'un détendeur, un bloc, un gilet… L'équipement lui-même ne compte pas (correction d'une attribution),
    /// ni un équipement déjà rendu.
    /// </summary>
    public static LigneSortie? DejaPris(long adherentId, LigneSortie ligne, IEnumerable<LigneSortie> lignesSortie) =>
        lignesSortie.FirstOrDefault(l => l.AdherentId == adherentId && l.EquipementId != ligne.EquipementId
            && l.Statut == StatutSortieEquipement.Dehors && string.Equals(l.Categorie.Trim(), ligne.Categorie.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Pourquoi cet adhérent ne peut pas prendre cet équipement dans cette sortie, ou null : désactivé, CACI expiré, nitrox sans
    /// certification, ou déjà un équipement de la même catégorie (<see cref="DejaPris"/>, si la sortie le demande : <see cref="Sortie.UnParCategorie"/>).
    /// </summary>
    public static string? RefusPour(Adherent a, LigneSortie ligne, IEnumerable<LigneSortie> lignesSortie, DateOnly aujourdhui, bool unParCategorie = true) =>
        RefusPour(a, ligne.Nitrox, ligne.Code, aujourdhui)
        ?? (unParCategorie && DejaPris(a.Id, ligne, lignesSortie) is { } deja
            ? $"{a.NomComplet} a déjà {deja.Code} ({deja.Categorie}) dans cette sortie : un seul équipement par catégorie."
            : null);

    /// <summary>Motif court affiché sous le nom dans le panneau du départ (« CACI expiré », « Pas certifié nitrox », « A déjà A12 »), ou null.</summary>
    public static string? MotifCourt(Adherent a, LigneSortie ligne, IEnumerable<LigneSortie> lignesSortie, DateOnly aujourdhui, bool unParCategorie = true) =>
        !a.Actif ? "Désactivé"
        : a.CaciExpire(aujourdhui) ? "CACI expiré"
        : ligne.Nitrox && !a.Nitrox ? "Pas certifié nitrox"
        : unParCategorie && DejaPris(a.Id, ligne, lignesSortie) is { } deja ? $"A déjà {deja.Code}"
        : null;

    /// <summary>Date du jour (contrôle du CACI dans le panneau du départ).</summary>
    public DateOnly AujourdHui => horloge.AujourdHui;

    // ---------------------------------------------------------------- Lecture

    /// <summary>Adhérents actifs (départ d'une sortie nominative), triés par nom, filtrés par nom ou prénom.</summary>
    public async Task<IReadOnlyList<Adherent>> ListerPourDepartAsync(string? recherche = null, CancellationToken ct = default) =>
        Filtrer(await adherents.ListerAsync(actifsSeulement: true, ct), recherche);

    /// <summary>Recherche sans tenir compte des majuscules ni des accents, sur le nom, le prénom ou les deux (« julie mar »).</summary>
    public static IReadOnlyList<Adherent> Filtrer(IEnumerable<Adherent> liste, string? recherche)
    {
        var mots = Simplifier(recherche ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return liste
            .Where(a => mots.All(m => Simplifier($"{a.Prenom} {a.Nom}").Contains(m, StringComparison.Ordinal)))
            .OrderBy(a => a.Nom, StringComparer.Create(Libelles.Culture, true))
            .ThenBy(a => a.Prenom, StringComparer.Create(Libelles.Culture, true))
            .ToList();
    }

    /// <summary>
    /// Onglet Adhérents : tous, les désactivés à la fin (pour pouvoir les réactiver). Filtre et recherche, avec le nombre
    /// d'adhérents de chaque filtre (puces « Tous 8 », « Matériel dehors 2 », « Non rendu 1 »).
    /// </summary>
    public async Task<ListeAdherents> ListerAsync(string? recherche, FiltreAdherents filtre, CancellationToken ct = default) =>
        await ListerAsync(recherche, filtre, niveau: null, ct);

    /// <summary>Choix « sans niveau » du filtre par niveau.</summary>
    public const string SansNiveau = "";

    /// <summary>
    /// Comme <see cref="ListerAsync(string?, FiltreAdherents, CancellationToken)"/>, limité à un niveau (null = tous,
    /// <see cref="SansNiveau"/> = niveau non renseigné). Les nombres des puces tiennent compte de la recherche et du niveau.
    /// </summary>
    public async Task<ListeAdherents> ListerAsync(string? recherche, FiltreAdherents filtre, string? niveau, CancellationToken ct = default)
    {
        var situations = (await adherents.ListerSituationsAsync(ct)).ToDictionary(s => s.AdherentId);
        var fiches = Filtrer(await adherents.ListerAsync(actifsSeulement: false, ct), recherche)
            .Where(a => niveau is null || (niveau.Length == 0 ? string.IsNullOrWhiteSpace(a.Niveau) : string.Equals(a.Niveau?.Trim(), niveau, StringComparison.OrdinalIgnoreCase)))
            .Select(a => situations.TryGetValue(a.Id, out var s) ? new FicheAdherent(a, s.NbDehors, s.NbManquants, a.CaciExpire(horloge.AujourdHui)) : new FicheAdherent(a, 0, 0, a.CaciExpire(horloge.AujourdHui)))
            .OrderBy(f => !f.Adherent.Actif)
            .ToList();
        var filtrees = fiches.Where(f => filtre switch
        {
            FiltreAdherents.MaterielDehors => f.NbDehors > 0,
            FiltreAdherents.NonRendu => f.NbNonRendus > 0,
            FiltreAdherents.Nitrox => f.Adherent.Nitrox,
            FiltreAdherents.Desactives => !f.Adherent.Actif,
            _ => true,
        }).ToList();
        return new ListeAdherents(filtrees, fiches.Count, fiches.Count(f => f.NbDehors > 0), fiches.Count(f => f.NbNonRendus > 0),
            fiches.Count(f => f.Adherent.Nitrox), fiches.Count(f => !f.Adherent.Actif));
    }

    /// <summary>Fiche : matériel qu'il a en ce moment, matériel non rendu (manquant à la clôture), dernières sorties nominatives.</summary>
    public async Task<DetailAdherent?> ObtenirFicheAsync(long id, CancellationToken ct = default)
    {
        if (await adherents.ObtenirAsync(id, ct) is not { } adherent)
        {
            return null;
        }

        var passages = await adherents.ListerPassagesAsync(id, ct);
        var sorties = passages.GroupBy(p => p.SortieId)
            .Select(g =>
            {
                var p = g.First();
                return new SortieAdherent(p.SortieId, p.DateSortie, p.Intitule, p.TypeNom is { Length: > 0 } nom ? nom : Libelles.De(p.TypeSortie),
                    g.Count(), g.Count(x => x.Statut == StatutSortieEquipement.Manquant), p.StatutSortie != StatutSortie.Cloturee);
            })
            .OrderByDescending(s => s.Date).ThenByDescending(s => s.SortieId)
            .Take(NombreSortiesFiche)
            .ToList();
        return new DetailAdherent(adherent,
            passages.Where(p => p.Statut == StatutSortieEquipement.Dehors).ToList(),
            passages.Where(p => p.Statut == StatutSortieEquipement.Manquant).ToList(),
            sorties,
            adherent.CaciExpire(horloge.AujourdHui));
    }

    // ---------------------------------------------------------------- Ajout et modification

    private ResultatOperation Autorisation() => gestion?.Exiger(ActionProtegee.Moniteurs) ?? ResultatOperation.Ok("");

    /// <summary>
    /// Ajoute (Id = 0) ou modifie un adhérent. Nom et prénom obligatoires ; licence unique si elle est renseignée.
    /// Le rendre inactif le retire de la liste du départ, sans toucher à son historique.
    /// </summary>
    public async Task<ResultatOperation> EnregistrerAsync(Adherent saisie, CancellationToken ct = default)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        var tous = await adherents.ListerAsync(actifsSeulement: false, ct);

        // Niveau pris dans la liste ; un ancien niveau hors liste reste accepté tant qu'il n'est pas changé.
        if (!string.IsNullOrWhiteSpace(saisie.Niveau))
        {
            var ancien = tous.FirstOrDefault(a => a.Id == saisie.Id && saisie.Id != 0)?.Niveau;
            if (NiveauReconnu(saisie.Niveau, Niveaux) is { } niveau)
            {
                saisie.Niveau = niveau;
            }
            else if (!string.Equals(ancien?.Trim(), saisie.Niveau.Trim(), StringComparison.Ordinal))
            {
                return ResultatOperation.Echec("Choisissez le niveau dans la liste.");
            }
        }

        if (Normaliser(saisie, tous) is { } erreur)
        {
            return erreur;
        }

        if (saisie.Id == 0)
        {
            var id = await adherents.AjouterAsync(saisie, ct);
            await JournaliserAsync(ActionsJournal.AdherentCree, $"Adhérent ajouté : {saisie.NomComplet}", ct);
            return ResultatOperation.Ok($"{saisie.NomComplet} ajouté.", id);
        }

        if (tous.All(a => a.Id != saisie.Id))
        {
            return ResultatOperation.Echec("Cet adhérent n'existe plus.");
        }

        await adherents.MettreAJourAsync(saisie, ct);
        await JournaliserAsync(ActionsJournal.AdherentModifie, $"Adhérent modifié : {saisie.NomComplet}{(saisie.Actif ? "" : " (inactif)")}", ct);
        return ResultatOperation.Ok($"{saisie.NomComplet} enregistré.", saisie.Id);
    }

    /// <summary>
    /// Désactive (ne peut plus sortir de matériel, n'est plus proposé au départ ; son historique reste) ou réactive un adhérent.
    /// Le matériel qu'il a déjà reste à son nom jusqu'au retour.
    /// </summary>
    public async Task<ResultatOperation> ChangerActifAsync(long id, bool actif, CancellationToken ct = default)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        if (await adherents.ObtenirAsync(id, ct) is not { } a)
        {
            return ResultatOperation.Echec("Cet adhérent n'existe plus.");
        }

        if (a.Actif == actif)
        {
            return ResultatOperation.Ok(actif ? $"{a.NomComplet} est déjà actif." : $"{a.NomComplet} est déjà désactivé.", id);
        }

        a.Actif = actif;
        await adherents.MettreAJourAsync(a, ct);
        await JournaliserAsync(ActionsJournal.AdherentModifie, $"Adhérent {(actif ? "réactivé" : "désactivé")} : {a.NomComplet}", ct);
        return ResultatOperation.Ok(actif
            ? $"{a.NomComplet} est réactivé : il peut de nouveau sortir du matériel."
            : $"{a.NomComplet} est désactivé : il ne peut plus sortir de matériel.", id);
    }

    /// <summary>Nettoie la saisie (espaces, licence en majuscules, champs vides → null) et la vérifie.</summary>
    private static ResultatOperation? Normaliser(Adherent a, IReadOnlyList<Adherent> tous)
    {
        a.Nom = (a.Nom ?? "").Trim();
        a.Prenom = (a.Prenom ?? "").Trim();
        a.Niveau = Vide(a.Niveau);
        a.Licence = Vide(a.Licence)?.ToUpper(Libelles.Culture);
        a.Telephone = Vide(a.Telephone);
        if (a.Nom.Length == 0 || a.Prenom.Length == 0)
        {
            return ResultatOperation.Echec("Indiquez le nom et le prénom.");
        }

        if (a.Nom.Length > Adherent.LongueurMaxNom || a.Prenom.Length > Adherent.LongueurMaxNom)
        {
            return ResultatOperation.Echec($"Nom ou prénom trop long ({Adherent.LongueurMaxNom} caractères au plus).");
        }

        if (a.Niveau?.Length > Adherent.LongueurMaxNiveau || a.Licence?.Length > Adherent.LongueurMaxLicence || a.Telephone?.Length > Adherent.LongueurMaxTelephone)
        {
            return ResultatOperation.Echec("Niveau, licence ou téléphone trop long (30 caractères au plus).");
        }

        // CACI : case cochée = date d'expiration obligatoire (une date déjà passée est acceptée : il ne sortira plus de matériel).
        if (!a.Caci)
        {
            a.CaciExpiration = null;
        }
        else if (a.CaciExpiration is null)
        {
            return ResultatOperation.Echec("Indiquez la date d'expiration du CACI.");
        }

        return a.Licence is { } licence && tous.FirstOrDefault(x => x.Id != a.Id && string.Equals(x.Licence, licence, StringComparison.OrdinalIgnoreCase)) is { } autre
            ? ResultatOperation.Echec($"La licence {licence} est déjà celle de {autre.NomComplet}.")
            : null;
    }

    private static string? Vide(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    // ---------------------------------------------------------------- Import de la liste Excel

    /// <summary>Une ligne lue dans le fichier : l'adhérent, ou l'erreur qui la fait ignorer.</summary>
    /// <param name="Nitrox">Colonne « Nitrox » (oui / non) : null si le fichier ne l'a pas (le statut n'est alors pas changé).</param>
    public sealed record LigneImport(int Numero, Adherent? Adherent, string? Erreur, bool? Nitrox = null);

    // En-têtes reconnus (sans accents ni majuscules), dans l'ordre : nom, prénom, niveau, licence, téléphone.
    private static readonly string[][] EnTetes =
    [
        ["nom", "nom de famille", "nom adherent"],
        ["prenom"],
        ["niveau", "niveau de plongee", "brevet"],
        ["licence", "n licence", "no licence", "numero de licence", "num licence", "licence ffessm", "n de licence"],
        ["telephone", "tel", "portable", "mobile", "telephone portable"],
        ["nitrox", "certifie nitrox", "certification nitrox", "brevet nitrox"],
    ];

    /// <summary>Une colonne du modèle Excel à remplir par le club : en-tête (reconnu par l'import), aide, largeur, obligatoire.</summary>
    public sealed record ColonneModele(string EnTete, string Aide, int Largeur, bool Obligatoire, IReadOnlyList<string>? Choix = null);

    /// <summary>
    /// Colonnes du modèle « Nouveaux adhérents » (bouton « Modèle Excel ») : exactement celles que <see cref="LireTableau"/>
    /// reconnaît, dans le même ordre. Niveau : liste des niveaux du club ; Nitrox : liste Oui / Non.
    /// </summary>
    public static IReadOnlyList<ColonneModele> ColonnesModele(IReadOnlyList<string> niveaux) =>
    [
        new("Nom", "Nom de famille", 22, true),
        new("Prénom", "Prénom", 18, true),
        new("Niveau", "Choisissez dans la liste (flèche de la case)", 16, false, niveaux),
        new("Licence", "Numéro de licence FFESSM (sert à reconnaître l'adhérent à l'import suivant)", 18, false),
        new("Téléphone", "Ex. : 06 12 34 56 78", 18, false),
        new("Nitrox", "Oui si certifié nitrox, sinon Non", 12, false, ["Oui", "Non"]),
    ];

    /// <summary>Nom proposé pour le modèle : « Adhérents à remplir - Asprenaut.xlsx ».</summary>
    public static string NomFichierModele(string? nomClub) =>
        $"Adhérents à remplir - {(string.IsNullOrWhiteSpace(nomClub) ? "club" : nomClub.Trim())}.xlsx";

    /// <summary>Mode d'emploi écrit dans la seconde feuille du modèle (une ligne par paragraphe).</summary>
    public static IReadOnlyList<string> ModeEmploiModele { get; } =
    [
        "Comment remplir ce fichier",
        "1. Une ligne par adhérent, dans la feuille « Adhérents », à partir de la ligne 2.",
        "2. Nom et Prénom sont obligatoires. Les autres colonnes peuvent rester vides.",
        "3. Ne changez pas la première ligne (les titres des colonnes) : l'application s'en sert pour lire le fichier.",
        "4. Licence : si elle est remplie, l'adhérent est reconnu à l'import suivant et mis à jour au lieu d'être ajouté en double.",
        "5. Niveau et Nitrox : choisissez dans la liste (flèche à droite de la case). Ne tapez pas d'autre niveau : il serait ignoré. Vide = inchangé.",
        "6. Enregistrez en .xlsx et rapportez le fichier au responsable matériel : Membres › Adhérents › Importer la liste Excel.",
        "Personne n'est retiré par l'import : les adhérents absents du fichier restent dans l'application.",
    ];

    /// <summary>
    /// Lit le tableau (feuille Excel ou CSV déjà découpé en cellules) : la première ligne qui contient « Nom » et « Prénom »
    /// est l'en-tête ; les colonnes Niveau, Licence et Téléphone sont facultatives. Les lignes vides sont ignorées.
    /// </summary>
    public static IReadOnlyList<LigneImport> LireTableau(IReadOnlyList<IReadOnlyList<string>> lignes)
    {
        var entete = -1;
        var colonnes = new int[EnTetes.Length];
        for (var i = 0; i < lignes.Count && entete < 0; i++)
        {
            var cellules = lignes[i].Select(c => Simplifier(c).Replace(".", "", StringComparison.Ordinal).Replace("°", "", StringComparison.Ordinal).Trim()).ToList();
            for (var c = 0; c < EnTetes.Length; c++)
            {
                colonnes[c] = cellules.FindIndex(x => EnTetes[c].Contains(x));
            }

            if (colonnes[0] >= 0 && colonnes[1] >= 0)
            {
                entete = i;
            }
        }

        if (entete < 0)
        {
            return [];
        }

        string? Cellule(IReadOnlyList<string> ligne, int colonne) => colonne >= 0 && colonne < ligne.Count ? ligne[colonne].Trim() : null;
        var resultat = new List<LigneImport>();
        for (var i = entete + 1; i < lignes.Count; i++)
        {
            var ligne = lignes[i];
            if (ligne.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var a = new Adherent
            {
                Nom = Cellule(ligne, colonnes[0]) ?? "",
                Prenom = Cellule(ligne, colonnes[1]) ?? "",
                Niveau = Cellule(ligne, colonnes[2]),
                Licence = Cellule(ligne, colonnes[3]),
                Telephone = Cellule(ligne, colonnes[4]),
            };
            // Cellule vide (colonne présente, ex. le modèle à remplir) : statut inchangé, comme une colonne absente.
            var celluleNitrox = colonnes[5] >= 0 ? Cellule(ligne, colonnes[5]) : null;
            var nitrox = string.IsNullOrWhiteSpace(celluleNitrox) ? (bool?)null : OuiNon(celluleNitrox);
            a.Nitrox = nitrox ?? false;
            resultat.Add(Normaliser(a, []) is { } erreur ? new LigneImport(i + 1, null, erreur.Message) : new LigneImport(i + 1, a, null, nitrox));
        }

        return resultat;
    }

    /// <summary>
    /// Importe la liste du club : un adhérent de même licence est mis à jour (et redevient actif), un adhérent sans licence
    /// est retrouvé par son nom et son prénom ; les autres sont ajoutés. Personne n'est supprimé ni rendu inactif.
    /// </summary>
    public async Task<ResultatOperation> ImporterAsync(IReadOnlyList<IReadOnlyList<string>> tableau, CancellationToken ct = default)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        var lues = LireTableau(tableau);
        if (lues.Count == 0)
        {
            return ResultatOperation.Echec("Aucun adhérent trouvé : la première ligne du tableau doit contenir les colonnes « Nom » et « Prénom ».");
        }

        var existants = (await adherents.ListerAsync(actifsSeulement: false, ct)).ToList();
        int ajoutes = 0, misAJour = 0;
        var ignorees = lues.Where(l => l.Adherent is null).Select(l => l.Numero).ToList();
        var licencesVues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var niveauxInconnus = new List<int>();
        foreach (var lu in lues.Where(l => l.Adherent is not null).Select(l => (l.Numero, Adherent: l.Adherent!, l.Nitrox)))
        {
            var nouveau = lu.Adherent;

            // Niveau ramené à la liste (« N2 » → « Niveau 2 ») ; inconnu : laissé vide (l'ancien est gardé), et signalé.
            if (nouveau.Niveau is { } niveauLu)
            {
                nouveau.Niveau = NiveauReconnu(niveauLu, Niveaux);
                if (nouveau.Niveau is null)
                {
                    niveauxInconnus.Add(lu.Numero);
                }
            }

            if (nouveau.Licence is { } licence && !licencesVues.Add(licence))
            {
                ignorees.Add(lu.Numero); // Même licence deux fois dans le fichier : la première ligne compte.
                continue;
            }

            var existant = nouveau.Licence is { } l
                ? existants.FirstOrDefault(a => string.Equals(a.Licence, l, StringComparison.OrdinalIgnoreCase))
                  ?? existants.FirstOrDefault(a => a.Licence is null && MemePersonne(a, nouveau))
                : existants.FirstOrDefault(a => MemePersonne(a, nouveau));
            if (existant is null)
            {
                nouveau.Id = await adherents.AjouterAsync(nouveau, ct);
                existants.Add(nouveau);
                ajoutes++;
                continue;
            }

            existant.Nom = nouveau.Nom;
            existant.Prenom = nouveau.Prenom;
            existant.Niveau = nouveau.Niveau ?? existant.Niveau;
            existant.Licence = nouveau.Licence ?? existant.Licence;
            existant.Telephone = nouveau.Telephone ?? existant.Telephone;
            existant.Nitrox = lu.Nitrox ?? existant.Nitrox;
            existant.Actif = true;
            await adherents.MettreAJourAsync(existant, ct);
            misAJour++;
        }

        var message = $"{Libelles.Pluriel(ajoutes, "adhérent ajouté", "adhérents ajoutés")}, {Libelles.Pluriel(misAJour, "mis à jour", "mis à jour")}";
        if (ignorees.Count > 0)
        {
            message += $", {Libelles.Pluriel(ignorees.Count, "ligne ignorée", "lignes ignorées")} (ligne{(ignorees.Count > 1 ? "s" : "")} {string.Join(", ", ignorees.Order().Take(10))}{(ignorees.Count > 10 ? "…" : "")})";
        }

        if (niveauxInconnus.Count > 0)
        {
            message += $", {Libelles.Pluriel(niveauxInconnus.Count, "niveau inconnu laissé vide", "niveaux inconnus laissés vides")} " +
                $"(ligne{(niveauxInconnus.Count > 1 ? "s" : "")} {string.Join(", ", niveauxInconnus.Take(10))}{(niveauxInconnus.Count > 10 ? "…" : "")})";
        }

        await JournaliserAsync(ActionsJournal.AdherentsImportes, $"Import de la liste des adhérents : {message}", ct);
        return ResultatOperation.Ok($"Import terminé : {message}.", ajoutes + misAJour);
    }

    /// <summary>« oui », « x », « 1 », « vrai »… → vrai ; cellule vide ou autre chose → faux.</summary>
    private static bool OuiNon(string? cellule) =>
        Simplifier(cellule ?? "") is "oui" or "o" or "x" or "1" or "vrai" or "yes" or "nitrox" or "certifie";

    private static bool MemePersonne(Adherent a, Adherent b) =>
        Simplifier(a.Nom) == Simplifier(b.Nom) && Simplifier(a.Prenom) == Simplifier(b.Prenom);

    /// <summary>Minuscules sans accents ni espaces doublés : « Léa  DUBOIS » → « lea dubois ».</summary>
    public static string Simplifier(string texte)
    {
        var decompose = texte.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decompose.Length);
        foreach (var c in decompose)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return string.Join(' ', sb.ToString().Normalize(NormalizationForm.FormC).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private Task JournaliserAsync(string action, string details, CancellationToken ct) =>
        journal.AjouterAsync(new EntreeJournal { Horodatage = horloge.MaintenantUtc, Action = action, Details = details }, ct);
}
