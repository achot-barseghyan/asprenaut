using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>Moniteurs et classes (responsables de sortie) : listes de choix, cartes avec leurs sorties et le matériel dehors, ajout et modification.</summary>
public sealed class ServiceMoniteurs(IMoniteurRepository moniteurs, ISortieRepository sorties, IJournalRepository journal, IHorloge horloge, ServiceGestion? gestion = null)
{
    /// <summary>Nombre de sorties clôturées affichées dans la fiche.</summary>
    public const int NombreDernieresSorties = 3;

    public Task<IReadOnlyList<Moniteur>> ListerActifsAsync(CancellationToken ct = default) =>
        moniteurs.ListerAsync(actifsSeulement: true, ct);

    /// <summary>Responsables actifs d'un genre : les moniteurs, ou les classes.</summary>
    public async Task<IReadOnlyList<Moniteur>> ListerActifsAsync(GenreResponsable genre, CancellationToken ct = default) =>
        (await moniteurs.ListerAsync(actifsSeulement: true, ct)).Where(m => m.Genre == genre).ToList();

    public Task<IReadOnlyList<Moniteur>> ListerTousAsync(CancellationToken ct = default) =>
        moniteurs.ListerAsync(actifsSeulement: false, ct);

    /// <summary>Habilités à faire les TIV (jamais une classe), enseignants ou non : la case TIV suffit (migration 030).</summary>
    public async Task<IReadOnlyList<Moniteur>> ListerInspecteursTivAsync(CancellationToken ct = default) =>
        (await moniteurs.ListerAsync(actifsSeulement: false, ct)).Where(m => !m.EstClasse && m.InspecteurTiv).ToList();

    /// <summary>Longueur maximale du nom d'une classe et de son abréviation.</summary>
    public const int LongueurMaxClasse = 30;
    public const int LongueurMaxAbrege = 4;

    /// <summary>Longueur maximale du numéro de licence d'un moniteur (comme celle d'un adhérent).</summary>
    public const int LongueurMaxLicence = Adherent.LongueurMaxLicence;

    /// <summary>
    /// Abréviation proposée pour une classe : « Niveau 2 » → « N2 », « Initiateur » → « INIT » (un mot : 4 lettres),
    /// « Plongée enfants » → « PE » (initiales, nombres gardés : « Groupe 12 » → « G12 »), accents retirés.
    /// </summary>
    public static string AbregeDe(string nom)
    {
        var propre = SansAccents(nom).Trim();
        var mots = propre.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (mots.Length == 0)
        {
            return "";
        }

        string abrege;
        if (System.Text.RegularExpressions.Regex.Match(propre, @"^niveau\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) is { Success: true } niveau)
        {
            abrege = $"N{niveau.Groups[1].Value}";
        }
        else if (mots.Length == 1)
        {
            abrege = mots[0];
        }
        else
        {
            abrege = string.Concat(mots.Select(m => m.All(char.IsDigit) ? m : m[..1]));
        }

        abrege = new string(abrege.Where(char.IsLetterOrDigit).ToArray()).ToUpper(Libelles.Culture);
        return abrege[..Math.Min(LongueurMaxAbrege, abrege.Length)];
    }

    private static string SansAccents(string texte) =>
        new(texte.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());

    /// <summary>Niveaux d'encadrement proposés en puces dans la fiche d'un moniteur (code, qualité) ; un autre niveau peut être tapé.</summary>
    public static IReadOnlyList<(string Code, string Qualite)> NiveauxEncadrement { get; } =
        [("E1", "Initiateur"), ("E2", "Initiateur"), ("E3", "Moniteur"), ("E4", "Moniteur"), ("MF1", "Fédéral"), ("MF2", "Fédéral")];

    /// <summary>Longueur maximale d'un niveau tapé à la main (« BEES 1 »).</summary>
    public const int LongueurMaxAutreNiveau = 12;

    /// <summary>Le niveau proposé en puce qui correspond au texte (majuscules et espaces ignorés), ou null : autre niveau.</summary>
    public static string? NiveauPropose(string? niveau)
    {
        var cle = (niveau ?? "").Replace(" ", "", StringComparison.Ordinal);
        return NiveauxEncadrement.Select(n => n.Code).FirstOrDefault(c => string.Equals(c, cle, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Noms de classe proposés à l'ajout, sans ceux qui existent déjà (majuscules ignorées).</summary>
    public static IReadOnlyList<string> ExemplesClasses(IEnumerable<string> existantes)
    {
        var deja = new HashSet<string>(existantes, StringComparer.OrdinalIgnoreCase);
        return new[] { "Niveau 1", "Niveau 2", "Niveau 3", "Initiateur", "Enfants", "Nitrox" }.Where(e => !deja.Contains(e)).ToList();
    }

    /// <summary>Couleur proposée pour une nouvelle classe : la première que n'a encore aucune classe (sinon la première).</summary>
    public static string CouleurProposee(IEnumerable<Moniteur> classes)
    {
        var prises = classes.Where(c => c.EstClasse).Select(c => c.CouleurEffective).ToHashSet();
        return Moniteur.CouleursClasse.FirstOrDefault(c => !prises.Contains(c)) ?? Moniteur.CouleursClasse[0];
    }

    /// <summary>La saison du club commence le 1er septembre.</summary>
    public static DateOnly DebutSaison(DateOnly jour) =>
        jour.Month >= 9 ? new DateOnly(jour.Year, 9, 1) : new DateOnly(jour.Year - 1, 9, 1);

    /// <summary>Une carte par moniteur (actifs d'abord) : sorties de la saison et équipements encore dehors.</summary>
    public async Task<IReadOnlyList<FicheMoniteur>> ListerFichesAsync(CancellationToken ct = default)
    {
        var tous = await moniteurs.ListerAsync(actifsSeulement: false, ct);
        var enCours = await sorties.ListerResumesAsync(cloturees: false, ct);
        var cloturees = await sorties.ListerResumesAsync(cloturees: true, ct);
        var debut = DebutSaison(horloge.AujourdHui);
        return tous
            .Select(m => new FicheMoniteur(
                m,
                enCours.Concat(cloturees).Count(s => s.MoniteurId == m.Id && s.DateSortie >= debut),
                enCours.Where(s => s.MoniteurId == m.Id).Sum(s => s.NbDehors)))
            .OrderByDescending(f => f.Moniteur.Actif)
            .ThenBy(f => f.Moniteur.NomComplet, StringComparer.Create(Libelles.Culture, true))
            .ToList();
    }

    /// <summary>
    /// Onglet Moniteurs : recherche (prénom, nom, niveau, téléphone, licence ; majuscules et accents ignorés) puis filtre
    /// (actifs, inspecteurs TIV actifs, matériel dehors, inactifs), avec le nombre de chaque filtre après la recherche.
    /// </summary>
    public static ListeMoniteurs Filtrer(IEnumerable<FicheMoniteur> fiches, string? recherche, FiltreMoniteurs filtre)
    {
        var mots = ServiceAdherents.Simplifier(recherche ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var trouves = fiches.Where(f =>
        {
            var m = f.Moniteur;
            var texte = ServiceAdherents.Simplifier($"{m.Prenom} {m.Nom} {m.Niveau} {m.Abrege} {m.Telephone} {m.Licence}");
            var chiffres = new string(m.Telephone.Where(char.IsDigit).ToArray());
            return mots.All(mot => texte.Contains(mot, StringComparison.Ordinal) || (mot.All(char.IsDigit) && chiffres.Contains(mot, StringComparison.Ordinal)));
        }).ToList();
        var filtrees = trouves.Where(f => filtre switch
        {
            FiltreMoniteurs.Actifs => f.Moniteur.Actif,
            FiltreMoniteurs.InspecteursTiv => f.Moniteur.Actif && f.Moniteur.InspecteurTiv,
            FiltreMoniteurs.MaterielDehors => f.NbDehors > 0,
            FiltreMoniteurs.Inactifs => !f.Moniteur.Actif,
            _ => true,
        }).ToList();
        return new ListeMoniteurs(filtrees, trouves.Count, trouves.Count(f => f.Moniteur.Actif),
            trouves.Count(f => f.Moniteur.Actif && f.Moniteur.InspecteurTiv), trouves.Count(f => f.NbDehors > 0), trouves.Count(f => !f.Moniteur.Actif));
    }

    /// <summary>Fiche : matériel dehors par sortie en cours, dernières sorties clôturées.</summary>
    public async Task<DetailMoniteur?> ObtenirDetailAsync(long moniteurId, CancellationToken ct = default)
    {
        if (await moniteurs.ObtenirAsync(moniteurId, ct) is not { } moniteur)
        {
            return null;
        }

        var enCours = new List<SortieDehors>();
        foreach (var s in (await sorties.ListerResumesAsync(cloturees: false, ct)).Where(s => s.MoniteurId == moniteurId && s.NbDehors > 0))
        {
            var dehors = (await sorties.ListerLignesAsync(s.Id, ct)).Where(l => l.Statut == StatutSortieEquipement.Dehors).ToList();
            enCours.Add(new SortieDehors(s, dehors));
        }

        var dernieres = (await sorties.ListerResumesAsync(cloturees: true, ct))
            .Where(s => s.MoniteurId == moniteurId)
            .OrderByDescending(s => s.DateSortie)
            .Take(NombreDernieresSorties)
            .ToList();
        return new DetailMoniteur(moniteur, enCours, dernieres);
    }

    /// <summary>
    /// Ajoute (Id = 0) ou modifie un responsable. Moniteur : prénom et nom obligatoires. Classe (<see cref="Moniteur.Genre"/>) :
    /// nom obligatoire et unique parmi les classes, abréviation proposée si vide ; ni téléphone, ni niveau, ni TIV.
    /// Le genre d'un responsable déjà enregistré ne change pas.
    /// </summary>
    public async Task<ResultatOperation> EnregistrerAsync(Moniteur saisie, CancellationToken ct = default)
    {
        if (gestion?.Exiger(ActionProtegee.Moniteurs) is { Reussi: false } refus)
        {
            return refus;
        }

        Moniteur? existant = null;
        if (saisie.Id != 0 && (existant = await moniteurs.ObtenirAsync(saisie.Id, ct)) is null)
        {
            return ResultatOperation.Echec(saisie.EstClasse ? "Classe introuvable." : "Enseignant introuvable.");
        }

        var genre = existant?.Genre ?? saisie.Genre;
        return genre == GenreResponsable.Classe
            ? await EnregistrerClasseAsync(saisie, ct)
            : await EnregistrerMoniteurAsync(saisie, ct);
    }

    private async Task<ResultatOperation> EnregistrerMoniteurAsync(Moniteur saisie, CancellationToken ct)
    {
        var prenom = saisie.Prenom.Trim();
        var nom = saisie.Nom.Trim();
        if (prenom.Length == 0 || nom.Length == 0)
        {
            return ResultatOperation.Echec("Indiquez le prénom et le nom.");
        }

        // Licence : majuscules, unique parmi les moniteurs (vide = non renseignée).
        var licence = saisie.Licence.Trim().ToUpper(Libelles.Culture);
        if (licence.Length > LongueurMaxLicence)
        {
            return ResultatOperation.Echec($"Licence trop longue : {LongueurMaxLicence} caractères au plus.");
        }

        if (licence.Length > 0 && (await moniteurs.ListerAsync(actifsSeulement: false, ct))
                .FirstOrDefault(x => !x.EstClasse && x.Id != saisie.Id && string.Equals(x.Licence, licence, StringComparison.OrdinalIgnoreCase)) is { } autre)
        {
            return ResultatOperation.Echec($"La licence {licence} est déjà celle de {autre.NomComplet}.");
        }

        var moniteur = new Moniteur
        {
            Id = saisie.Id,
            Genre = GenreResponsable.Moniteur,
            Prenom = prenom,
            Nom = nom,
            Niveau = string.IsNullOrWhiteSpace(saisie.Niveau) ? null : saisie.Niveau.Trim(),
            Telephone = saisie.Telephone.Trim(),
            Licence = licence,
            InspecteurTiv = saisie.InspecteurTiv,
            Actif = saisie.Actif,
        };

        if (moniteur.Id == 0)
        {
            var id = await moniteurs.AjouterAsync(moniteur, ct);
            await JournaliserAsync(ActionsJournal.MoniteurCree, $"Enseignant {moniteur.NomComplet} ajouté", ct);
            return ResultatOperation.Ok($"{moniteur.NomComplet} ajouté.", id);
        }

        await moniteurs.MettreAJourAsync(moniteur, ct);
        await JournaliserAsync(ActionsJournal.MoniteurModifie, $"Enseignant {moniteur.NomComplet} modifié", ct);
        return ResultatOperation.Ok($"{moniteur.NomComplet} enregistré.", moniteur.Id);
    }

    private async Task<ResultatOperation> EnregistrerClasseAsync(Moniteur saisie, CancellationToken ct)
    {
        // Le nom d'une classe peut arriver dans Nom ou dans Prenom : on garde le texte saisi, en un seul champ.
        var nom = string.Join(' ', $"{saisie.Prenom} {saisie.Nom}".Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (nom.Length == 0)
        {
            return ResultatOperation.Echec("Indiquez le nom de la classe.");
        }

        if (nom.Length > LongueurMaxClasse)
        {
            return ResultatOperation.Echec($"Nom trop long : {LongueurMaxClasse} caractères au plus.");
        }

        var abrege = saisie.Abrege.Trim().ToUpper(Libelles.Culture);
        if (abrege.Length == 0)
        {
            abrege = AbregeDe(nom);
        }

        if (abrege.Length > LongueurMaxAbrege)
        {
            return ResultatOperation.Echec($"Abréviation trop longue : {LongueurMaxAbrege} caractères au plus.");
        }

        var tous = await moniteurs.ListerAsync(actifsSeulement: false, ct);
        if (tous.Any(m => m.EstClasse && m.Id != saisie.Id && string.Equals(m.NomComplet, nom, StringComparison.OrdinalIgnoreCase)))
        {
            return ResultatOperation.Echec($"La classe « {nom} » existe déjà.");
        }

        var classe = new Moniteur
        {
            Id = saisie.Id,
            Genre = GenreResponsable.Classe,
            Nom = nom,
            Abrege = abrege,
            Couleur = saisie.Couleur is { } couleur && Moniteur.CouleursClasse.Contains(couleur) ? couleur : null,
            Actif = saisie.Actif,
        };

        if (classe.Id == 0)
        {
            var id = await moniteurs.AjouterAsync(classe, ct);
            await JournaliserAsync(ActionsJournal.ClasseCreee, $"Classe {nom} ajoutée", ct);
            return ResultatOperation.Ok($"{nom} ajoutée.", id);
        }

        await moniteurs.MettreAJourAsync(classe, ct);
        await JournaliserAsync(ActionsJournal.ClasseModifiee, $"Classe {nom} modifiée", ct);
        return ResultatOperation.Ok($"{nom} enregistrée.", classe.Id);
    }

    private Task JournaliserAsync(string action, string details, CancellationToken ct) =>
        journal.AjouterAsync(new EntreeJournal { Horodatage = horloge.MaintenantUtc, Action = action, Details = details }, ct);
}
