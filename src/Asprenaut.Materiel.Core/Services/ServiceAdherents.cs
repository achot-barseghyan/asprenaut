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
    IMoniteurRepository? classes = null)
{
    /// <summary>
    /// Formations proposées : les classes actives du club (Membres › Classes), plus <paramref name="garder"/> (formation actuelle
    /// de l'adhérent, même si sa classe a été désactivée). Triées par nom (Niveau 1, Niveau 2… Autonome).
    /// </summary>
    public async Task<IReadOnlyList<Moniteur>> ListerFormationsAsync(long? garder = null, CancellationToken ct = default) =>
        classes is null ? [] : (await classes.ListerAsync(actifsSeulement: false, ct))
            .Where(m => m.EstClasse && (m.Actif || m.Id == garder))
            .OrderBy(m => m.Nom, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>
    /// Relie la saisie à sa classe : par <see cref="Adherent.ClasseId"/>, sinon par le nom tapé (« N2 » → « Niveau 2 »). Remplit
    /// <see cref="Adherent.Niveau"/> avec le nom de la classe. Une classe désactivée n'est acceptée que si c'était déjà la sienne ;
    /// un ancien niveau sans classe reste accepté tant qu'il n'est pas changé. Null si tout va bien, sinon le refus.
    /// </summary>
    private async Task<ResultatOperation?> RelierFormationAsync(Adherent saisie, Adherent? ancien, CancellationToken ct)
    {
        var liste = classes is null ? [] : (await classes.ListerAsync(actifsSeulement: false, ct)).Where(m => m.EstClasse).ToList();
        Moniteur? classe = null;
        if (saisie.ClasseId is { } id)
        {
            classe = liste.FirstOrDefault(c => c.Id == id);
            if (classe is null)
            {
                return ResultatOperation.Echec("Cette formation n'existe plus : choisissez-en une autre.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(saisie.Niveau))
        {
            var nom = NiveauReconnu(saisie.Niveau, liste.Select(c => c.Nom).ToList());
            classe = liste.Where(c => c.Nom == nom).OrderBy(c => !c.Actif).FirstOrDefault();
            if (classe is null)
            {
                if (ancien is { ClasseId: null } && string.Equals(ancien.Niveau?.Trim(), saisie.Niveau.Trim(), StringComparison.Ordinal))
                {
                    return null; // Ancien niveau sans classe, inchangé.
                }

                return ResultatOperation.Echec("Choisissez la formation dans la liste (classes de Membres › Classes).");
            }
        }

        if (classe is { Actif: false } && classe.Id != ancien?.ClasseId)
        {
            return ResultatOperation.Echec($"La classe « {classe.Nom} » est désactivée : choisissez une autre formation.");
        }

        saisie.ClasseId = classe?.Id;
        saisie.Niveau = classe?.Nom;
        return null;
    }

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

    /// <summary>Majuscule au début de chaque mot (« jean-pierre le goff » → « Jean-Pierre Le Goff ») ; le reste est gardé tel quel.</summary>
    public static string MajusculesMots(string texte)
    {
        var lettres = texte.Trim().ToCharArray();
        for (var i = 0; i < lettres.Length; i++)
        {
            if (char.IsLetter(lettres[i]) && (i == 0 || lettres[i - 1] is ' ' or '-' or '\''))
            {
                lettres[i] = char.ToUpper(lettres[i], Libelles.Culture);
            }
        }

        return new string(lettres);
    }

    /// <summary>
    /// Téléphone tapé à la française mis en forme par paires (« 0612345678 » → « 06 12 34 56 78 ») ; un numéro avec
    /// autre chose que des chiffres, espaces, points ou tirets (« +33… ») est gardé tel quel.
    /// </summary>
    public static string FormaterTelephone(string saisie)
    {
        if (saisie.Any(c => !char.IsDigit(c) && c is not (' ' or '.' or '-')))
        {
            return saisie;
        }

        var chiffres = new string(saisie.Where(char.IsDigit).Take(10).ToArray());
        return string.Join(' ', chiffres.Chunk(2).Select(p => new string(p)));
    }

    /// <summary>Initiales de l'avatar (« Camille », « Martin » → « CM ») ; vide si rien n'est tapé.</summary>
    public static string Initiales(string prenom, string nom) =>
        string.Concat(new[] { prenom, nom }.Select(t => t.Trim()).Where(t => t.Length > 0).Select(t => char.ToUpper(t[0], Libelles.Culture)));

    /// <summary>
    /// État du CACI pendant la saisie (formulaire de l'adhérent) : null si le CACI n'est pas contrôlé. Mêmes règles que
    /// <see cref="Adherent.CaciExpire"/> (le dernier jour reste valable) et <see cref="Adherent.CaciBientot"/> (<see cref="JoursAvantFinCaci"/>).
    /// </summary>
    public static EtatCaci? EtatCaciSaisi(bool controle, DateOnly? fin, DateOnly aujourdhui)
    {
        if (!controle)
        {
            return null;
        }

        if (fin is not { } date)
        {
            return new EtatCaci(NiveauCaci.ASaisir, "Date à saisir", null);
        }

        var jours = date.DayNumber - aujourdhui.DayNumber;
        return jours < 0 ? new EtatCaci(NiveauCaci.Expire, $"Expiré depuis le {date.ToString("dd/MM/yyyy", Libelles.Culture)}", "CACI expiré")
            : jours <= JoursAvantFinCaci ? new EtatCaci(NiveauCaci.Bientot, jours == 0 ? "Expire ce soir" : $"Expire dans {jours} j", "CACI bientôt")
            : new EtatCaci(NiveauCaci.Valide, "Valide", "CACI valide");
    }

    /// <summary>Dernières sorties nominatives montrées sur la fiche.</summary>
    public const int NombreSortiesFiche = 5;

    /// <summary>Un CACI qui expire dans ce nombre de jours est « à renouveler » (pastille jaune, filtre).</summary>
    public const int JoursAvantFinCaci = 30;

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
            .Select(a => situations.TryGetValue(a.Id, out var s)
                ? new FicheAdherent(a, s.NbDehors, s.NbManquants, a.CaciExpire(horloge.AujourdHui), a.CaciBientot(horloge.AujourdHui, JoursAvantFinCaci))
                : new FicheAdherent(a, 0, 0, a.CaciExpire(horloge.AujourdHui), a.CaciBientot(horloge.AujourdHui, JoursAvantFinCaci)))
            .OrderBy(f => !f.Adherent.Actif)
            .ToList();
        var filtrees = fiches.Where(f => filtre switch
        {
            FiltreAdherents.MaterielDehors => f.NbDehors > 0,
            FiltreAdherents.NonRendu => f.NbNonRendus > 0,
            FiltreAdherents.Nitrox => f.Adherent.Nitrox,
            FiltreAdherents.Desactives => !f.Adherent.Actif,
            FiltreAdherents.CaciARenouveler => f.CaciExpire || f.CaciBientot,
            _ => true,
        }).ToList();
        return new ListeAdherents(filtrees, fiches.Count, fiches.Count(f => f.NbDehors > 0), fiches.Count(f => f.NbNonRendus > 0),
            fiches.Count(f => f.Adherent.Nitrox), fiches.Count(f => !f.Adherent.Actif), fiches.Count(f => f.CaciExpire || f.CaciBientot));
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
            adherent.CaciExpire(horloge.AujourdHui),
            adherent.CaciBientot(horloge.AujourdHui, JoursAvantFinCaci));
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

        // Formation en cours : une classe du club.
        if (await RelierFormationAsync(saisie, tous.FirstOrDefault(a => a.Id == saisie.Id && saisie.Id != 0), ct) is { } refusFormation)
        {
            return refusFormation;
        }

        // CACI avant modification (pour le journal), relevé avant que la saisie ne soit nettoyée.
        var avant = tous.FirstOrDefault(a => a.Id == saisie.Id && saisie.Id != 0) is { } x ? (x.Caci, x.CaciExpiration) : (false, (DateOnly?)null);
        if (Normaliser(saisie, tous) is { } erreur)
        {
            return erreur;
        }

        if (saisie.Id == 0)
        {
            var id = await adherents.AjouterAsync(saisie, ct);
            await JournaliserAsync(ActionsJournal.AdherentCree, $"Adhérent ajouté : {saisie.NomComplet}{ChangementCaci(avant, saisie)}", ct);
            return ResultatOperation.Ok($"{saisie.NomComplet} ajouté.", id);
        }

        if (tous.All(a => a.Id != saisie.Id))
        {
            return ResultatOperation.Echec("Cet adhérent n'existe plus.");
        }

        await adherents.MettreAJourAsync(saisie, ct);
        await JournaliserAsync(ActionsJournal.AdherentModifie, $"Adhérent modifié : {saisie.NomComplet}{(saisie.Actif ? "" : " (inactif)")}{ChangementCaci(avant, saisie)}", ct);
        return ResultatOperation.Ok($"{saisie.NomComplet} enregistré.", saisie.Id);
    }

    /// <summary>Fin du texte du journal quand le CACI change : « · CACI jusqu'au 15/03/2027 », « · CACI retiré », sinon vide.</summary>
    public static string ChangementCaci((bool Caci, DateOnly? Expiration) avant, Adherent apres) =>
        apres.Caci && apres.CaciExpiration is { } fin && (!avant.Caci || avant.Expiration != fin) ? $" · CACI jusqu'au {Libelles.Date(fin)}"
        : !apres.Caci && avant.Caci ? " · CACI retiré"
        : "";

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

    // ---------------------------------------------------------------- Passer d'adhérent à moniteur, et l'inverse

    /// <summary>
    /// L'adhérent devient moniteur : un moniteur du même prénom et nom (majuscules et accents ignorés) est réactivé, sinon
    /// il est créé (prénom, nom, téléphone ; niveau d'encadrement à compléter). L'adhérent est désactivé : son historique
    /// et le matériel qu'il a encore restent à son nom. Action Moniteurs. L'Id du résultat est celui du moniteur.
    /// </summary>
    public async Task<ResultatOperation> PasserMoniteurAsync(long adherentId, CancellationToken ct = default)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        if (classes is null)
        {
            return ResultatOperation.Echec("Les moniteurs ne sont pas disponibles.");
        }

        if (await adherents.ObtenirAsync(adherentId, ct) is not { } a)
        {
            return ResultatOperation.Echec("Cet adhérent n'existe plus.");
        }

        var moniteur = (await classes.ListerAsync(actifsSeulement: false, ct))
            .Where(m => !m.EstClasse && MemeNom(m.Prenom, m.Nom, a.Prenom, a.Nom))
            .OrderByDescending(m => m.Actif)
            .FirstOrDefault();
        long moniteurId;
        if (moniteur is null)
        {
            moniteurId = await classes.AjouterAsync(new Moniteur
            {
                Genre = GenreResponsable.Moniteur,
                Prenom = a.Prenom,
                Nom = a.Nom,
                Telephone = a.Telephone ?? "",
                Actif = true,
            }, ct);
        }
        else
        {
            moniteurId = moniteur.Id;
            if (!moniteur.Actif || (moniteur.Telephone.Length == 0 && a.Telephone is { Length: > 0 }))
            {
                moniteur.Actif = true;
                if (moniteur.Telephone.Length == 0)
                {
                    moniteur.Telephone = a.Telephone ?? "";
                }

                await classes.MettreAJourAsync(moniteur, ct);
            }
        }

        if (a.Actif)
        {
            a.Actif = false;
            await adherents.MettreAJourAsync(a, ct);
        }

        await JournaliserAsync(ActionsJournal.AdherentDevenuMoniteur, $"{a.NomComplet} : adhérent devenu moniteur", ct);
        return ResultatOperation.Ok($"{a.NomComplet} est maintenant moniteur. Complétez son niveau dans sa fiche.", moniteurId);
    }

    /// <summary>
    /// Le moniteur redevient simple adhérent : un adhérent du même prénom et nom est réactivé, sinon il est créé (prénom, nom,
    /// téléphone). Le moniteur est désactivé (plus proposé comme responsable ; ses sorties le gardent). Pas pour une classe.
    /// Action Moniteurs. L'Id du résultat est celui de l'adhérent.
    /// </summary>
    public async Task<ResultatOperation> PasserAdherentAsync(long moniteurId, CancellationToken ct = default)
    {
        if (Autorisation() is { Reussi: false } refus)
        {
            return refus;
        }

        if (classes is null || await classes.ObtenirAsync(moniteurId, ct) is not { } m)
        {
            return ResultatOperation.Echec("Ce moniteur n'existe plus.");
        }

        if (m.EstClasse)
        {
            return ResultatOperation.Echec("Une classe ne peut pas devenir un adhérent.");
        }

        var tous = await adherents.ListerAsync(actifsSeulement: false, ct);
        var adherent = tous.Where(x => MemeNom(x.Prenom, x.Nom, m.Prenom, m.Nom)).OrderByDescending(x => x.Actif).FirstOrDefault();
        long adherentId;
        if (adherent is null)
        {
            var nouveau = new Adherent { Prenom = m.Prenom, Nom = m.Nom, Telephone = m.Telephone };
            if (Normaliser(nouveau, tous) is { } erreur)
            {
                return erreur;
            }

            adherentId = await adherents.AjouterAsync(nouveau, ct);
        }
        else
        {
            adherentId = adherent.Id;
            if (!adherent.Actif || (adherent.Telephone is not { Length: > 0 } && m.Telephone.Length is > 0 and <= Adherent.LongueurMaxTelephone))
            {
                adherent.Actif = true;
                if (adherent.Telephone is not { Length: > 0 } && m.Telephone.Length <= Adherent.LongueurMaxTelephone)
                {
                    adherent.Telephone = Vide(m.Telephone);
                }

                await adherents.MettreAJourAsync(adherent, ct);
            }
        }

        if (m.Actif)
        {
            m.Actif = false;
            await classes.MettreAJourAsync(m, ct);
        }

        await JournaliserAsync(ActionsJournal.MoniteurDevenuAdherent, $"{m.NomComplet} : moniteur devenu adhérent", ct);
        return ResultatOperation.Ok($"{m.NomComplet} est maintenant un simple adhérent.", adherentId);
    }

    private static bool MemeNom(string prenom1, string nom1, string prenom2, string nom2) =>
        Simplifier(prenom1) == Simplifier(prenom2) && Simplifier(nom1) == Simplifier(nom2);

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
    /// <param name="Caci">Colonne « CACI valable jusqu'au » : null si absente, vide ou illisible (le CACI n'est alors pas changé).</param>
    /// <param name="CaciIllisible">Case CACI remplie mais pas une date (signalé dans le message de l'import).</param>
    public sealed record LigneImport(int Numero, Adherent? Adherent, string? Erreur, bool? Nitrox = null, DateOnly? Caci = null, bool CaciIllisible = false);

    // En-têtes reconnus (sans accents ni majuscules), dans l'ordre : nom, prénom, niveau, licence, téléphone.
    private static readonly string[][] EnTetes =
    [
        ["nom", "nom de famille", "nom adherent"],
        ["prenom"],
        ["formation en cours", "formation", "classe", "groupe", "niveau", "niveau de plongee", "niveau en cours", "brevet"],
        ["licence", "n licence", "no licence", "numero de licence", "num licence", "licence ffessm", "n de licence"],
        ["telephone", "tel", "portable", "mobile", "telephone portable"],
        ["nitrox", "certifie nitrox", "certification nitrox", "brevet nitrox"],
        ["caci valable jusqu'au", "caci valable jusqu au", "caci", "caci jusqu'au", "date caci", "fin caci", "fin du caci", "expiration caci", "validite caci",
            "certificat medical", "date certificat medical", "fin certificat medical"],
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
        new("Formation en cours", "Classe suivie (ex. Niveau 2 = prépare le niveau 2) : choisissez dans la liste (flèche de la case)", 20, false, niveaux),
        new("Licence", "Numéro de licence FFESSM (sert à reconnaître l'adhérent à l'import suivant)", 18, false),
        new("Téléphone", "Ex. : 06 12 34 56 78", 18, false),
        new("Nitrox", "Oui si certifié nitrox, sinon Non", 12, false, ["Oui", "Non"]),
        new("CACI valable jusqu'au", "Dernier jour de validité du certificat (jj/mm/aaaa). Vide : CACI inchangé", 20, false),
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
        "5. Formation en cours et Nitrox : choisissez dans la liste (flèche à droite de la case). La formation est la classe suivie (Niveau 2 = prépare le niveau 2). Ne tapez pas d'autre formation : elle serait ignorée. Vide = inchangé.",
        "6. CACI valable jusqu'au : date de fin du certificat, au format jj/mm/aaaa (ex. 15/09/2027). Passée cette date, l'adhérent ne sort plus de matériel. Vide = inchangé.",
        "7. Enregistrez en .xlsx et rapportez le fichier au responsable matériel : Membres › Adhérents › Importer la liste Excel.",
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
            var cellules = lignes[i].Select(c => Simplifier(c).Replace(".", "", StringComparison.Ordinal).Replace("°", "", StringComparison.Ordinal).Replace('’', '\'').Trim()).ToList();
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

            // CACI : une date = CACI suivi jusqu'à ce jour ; vide = inchangé ; autre chose = signalé et inchangé.
            var celluleCaci = colonnes[6] >= 0 ? Cellule(ligne, colonnes[6]) : null;
            var caci = string.IsNullOrWhiteSpace(celluleCaci) ? null : LireDate(celluleCaci);
            a.Caci = caci is not null;
            a.CaciExpiration = caci;
            resultat.Add(Normaliser(a, []) is { } erreur
                ? new LigneImport(i + 1, null, erreur.Message)
                : new LigneImport(i + 1, a, null, nitrox, caci, !string.IsNullOrWhiteSpace(celluleCaci) && caci is null));
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
        var formations = await ListerFormationsAsync(ct: ct);
        int ajoutes = 0, misAJour = 0;
        var ignorees = lues.Where(l => l.Adherent is null).Select(l => l.Numero).ToList();
        var licencesVues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var niveauxInconnus = new List<int>();
        var cacisIllisibles = lues.Where(l => l.CaciIllisible).Select(l => l.Numero).ToList();
        foreach (var lu in lues.Where(l => l.Adherent is not null).Select(l => (l.Numero, Adherent: l.Adherent!, l.Nitrox, l.Caci)))
        {
            var nouveau = lu.Adherent;

            // Formation ramenée aux classes actives (« N2 » → « Niveau 2 ») ; inconnue : laissée vide (l'ancienne est gardée), et signalée.
            if (nouveau.Niveau is { } niveauLu)
            {
                var nom = NiveauReconnu(niveauLu, formations.Select(f => f.Nom).ToList());
                var classe = formations.FirstOrDefault(f => f.Nom == nom);
                nouveau.ClasseId = classe?.Id;
                nouveau.Niveau = classe?.Nom;
                if (classe is null)
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
            if (nouveau.ClasseId is not null)
            {
                existant.ClasseId = nouveau.ClasseId;
                existant.Niveau = nouveau.Niveau;
            }

            existant.Licence = nouveau.Licence ?? existant.Licence;
            existant.Telephone = nouveau.Telephone ?? existant.Telephone;
            existant.Nitrox = lu.Nitrox ?? existant.Nitrox;
            if (lu.Caci is { } finCaci)
            {
                existant.Caci = true;
                existant.CaciExpiration = finCaci;
            }

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
            message += $", {Libelles.Pluriel(niveauxInconnus.Count, "formation inconnue laissée vide", "formations inconnues laissées vides")} " +
                $"(ligne{(niveauxInconnus.Count > 1 ? "s" : "")} {string.Join(", ", niveauxInconnus.Take(10))}{(niveauxInconnus.Count > 10 ? "…" : "")})";
        }

        if (cacisIllisibles.Count > 0)
        {
            message += $", {Libelles.Pluriel(cacisIllisibles.Count, "date de CACI illisible laissée de côté", "dates de CACI illisibles laissées de côté")} " +
                $"(ligne{(cacisIllisibles.Count > 1 ? "s" : "")} {string.Join(", ", cacisIllisibles.Take(10))}{(cacisIllisibles.Count > 10 ? "…" : "")})";
        }

        await JournaliserAsync(ActionsJournal.AdherentsImportes, $"Import de la liste des adhérents : {message}", ct);
        return ResultatOperation.Ok($"Import terminé : {message}.", ajoutes + misAJour);
    }

    private static readonly string[] FormatsDate = ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy"];

    /// <summary>
    /// Date d'une cellule : « 15/09/2027 », « 15/9/27 », « 2027-09-15 », ou nombre de jours d'Excel (cellule au format date,
    /// 46645 = 15/09/2027) ; null si ce n'est pas une date.
    /// </summary>
    public static DateOnly? LireDate(string? cellule)
    {
        var texte = (cellule ?? "").Trim();
        if (texte.Length == 0)
        {
            return null;
        }

        // Cellule date d'Excel : nombre de jours depuis le 30/12/1899 (années 1955 à 2119).
        if (double.TryParse(texte, NumberStyles.Float, CultureInfo.InvariantCulture, out var serie) && serie is >= 20000 and <= 80000)
        {
            return DateOnly.FromDateTime(DateTime.FromOADate(Math.Floor(serie)));
        }

        // « 15/09/2027 00:00:00 » (CSV exporté) : la date seule.
        var date = texte.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return DateOnly.TryParseExact(date, FormatsDate, Libelles.Culture, DateTimeStyles.None, out var d) ? d : null;
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
