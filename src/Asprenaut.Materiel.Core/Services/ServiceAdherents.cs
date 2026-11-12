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
    ServiceGestion? gestion = null)
{
    /// <summary>Dernières sorties nominatives montrées sur la fiche.</summary>
    public const int NombreSortiesFiche = 5;

    // ---------------------------------------------------------------- Règles du départ

    /// <summary>
    /// Pourquoi cet adhérent ne peut pas prendre cet équipement au départ, ou null s'il le peut :
    /// désactivé (ne sort plus de matériel), ou équipement nitrox sans certification nitrox.
    /// </summary>
    public static string? RefusPour(Adherent a, bool equipementNitrox, string code) =>
        !a.Actif ? $"{a.NomComplet} est désactivé : il ne peut plus sortir de matériel."
        : equipementNitrox && !a.Nitrox ? $"{code} est un équipement nitrox : {a.NomComplet} n'a pas la certification nitrox."
        : null;

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
    public async Task<ListeAdherents> ListerAsync(string? recherche, FiltreAdherents filtre, CancellationToken ct = default)
    {
        var situations = (await adherents.ListerSituationsAsync(ct)).ToDictionary(s => s.AdherentId);
        var fiches = Filtrer(await adherents.ListerAsync(actifsSeulement: false, ct), recherche)
            .Select(a => situations.TryGetValue(a.Id, out var s) ? new FicheAdherent(a, s.NbDehors, s.NbManquants) : new FicheAdherent(a, 0, 0))
            .OrderBy(f => !f.Adherent.Actif)
            .ToList();
        var filtrees = fiches.Where(f => filtre switch
        {
            FiltreAdherents.MaterielDehors => f.NbDehors > 0,
            FiltreAdherents.NonRendu => f.NbNonRendus > 0,
            _ => true,
        }).ToList();
        return new ListeAdherents(filtrees, fiches.Count, fiches.Count(f => f.NbDehors > 0), fiches.Count(f => f.NbNonRendus > 0));
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
            sorties);
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
            var nitrox = colonnes[5] >= 0 ? OuiNon(Cellule(ligne, colonnes[5])) : (bool?)null;
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
        foreach (var lu in lues.Where(l => l.Adherent is not null).Select(l => (l.Numero, Adherent: l.Adherent!, l.Nitrox)))
        {
            var nouveau = lu.Adherent;
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
