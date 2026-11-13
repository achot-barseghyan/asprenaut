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

    /// <summary>Moniteurs actifs habilités à faire les TIV (jamais une classe).</summary>
    public async Task<IReadOnlyList<Moniteur>> ListerInspecteursTivAsync(CancellationToken ct = default) =>
        (await moniteurs.ListerAsync(actifsSeulement: true, ct)).Where(m => !m.EstClasse && m.InspecteurTiv).ToList();

    /// <summary>Longueur maximale du nom d'une classe et de son abréviation.</summary>
    public const int LongueurMaxClasse = 30;
    public const int LongueurMaxAbrege = 4;

    /// <summary>Abréviation proposée pour une classe : « Niveau 2 » → « N2 », « Initiateurs » → « IN », « Plongée enfants » → « PE ».</summary>
    public static string AbregeDe(string nom)
    {
        var mots = nom.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (mots.Length == 0)
        {
            return "";
        }

        var chiffres = new string(nom.Where(char.IsDigit).ToArray());
        var abrege = chiffres.Length > 0
            ? $"{mots[0][0]}{chiffres}"
            : mots.Length == 1 ? mots[0][..Math.Min(2, mots[0].Length)] : string.Concat(mots.Take(3).Select(m => m[0]));
        abrege = abrege.ToUpper(Libelles.Culture);
        return abrege[..Math.Min(LongueurMaxAbrege, abrege.Length)];
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
    /// Onglet Moniteurs : recherche (prénom, nom, niveau, téléphone ; majuscules et accents ignorés) puis filtre
    /// (actifs, inspecteurs TIV actifs, matériel dehors, inactifs), avec le nombre de chaque filtre après la recherche.
    /// </summary>
    public static ListeMoniteurs Filtrer(IEnumerable<FicheMoniteur> fiches, string? recherche, FiltreMoniteurs filtre)
    {
        var mots = ServiceAdherents.Simplifier(recherche ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var trouves = fiches.Where(f =>
        {
            var m = f.Moniteur;
            var texte = ServiceAdherents.Simplifier($"{m.Prenom} {m.Nom} {m.Niveau} {m.Abrege} {m.Telephone}");
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
            return ResultatOperation.Echec(saisie.EstClasse ? "Classe introuvable." : "Moniteur introuvable.");
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

        var moniteur = new Moniteur
        {
            Id = saisie.Id,
            Genre = GenreResponsable.Moniteur,
            Prenom = prenom,
            Nom = nom,
            Niveau = string.IsNullOrWhiteSpace(saisie.Niveau) ? null : saisie.Niveau.Trim(),
            Telephone = saisie.Telephone.Trim(),
            InspecteurTiv = saisie.InspecteurTiv,
            Actif = saisie.Actif,
        };

        if (moniteur.Id == 0)
        {
            var id = await moniteurs.AjouterAsync(moniteur, ct);
            await JournaliserAsync(ActionsJournal.MoniteurCree, $"Moniteur {moniteur.NomComplet} ajouté", ct);
            return ResultatOperation.Ok($"{moniteur.NomComplet} ajouté.", id);
        }

        await moniteurs.MettreAJourAsync(moniteur, ct);
        await JournaliserAsync(ActionsJournal.MoniteurModifie, $"Moniteur {moniteur.NomComplet} modifié", ct);
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
