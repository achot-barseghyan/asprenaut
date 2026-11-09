using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>Moniteurs : listes de choix, cartes avec leurs sorties et le matériel dehors, ajout et modification.</summary>
public sealed class ServiceMoniteurs(IMoniteurRepository moniteurs, ISortieRepository sorties, IJournalRepository journal, IHorloge horloge)
{
    /// <summary>Nombre de sorties clôturées affichées dans la fiche.</summary>
    public const int NombreDernieresSorties = 3;

    public Task<IReadOnlyList<Moniteur>> ListerActifsAsync(CancellationToken ct = default) =>
        moniteurs.ListerAsync(actifsSeulement: true, ct);

    public Task<IReadOnlyList<Moniteur>> ListerTousAsync(CancellationToken ct = default) =>
        moniteurs.ListerAsync(actifsSeulement: false, ct);

    /// <summary>Moniteurs actifs habilités à faire les TIV.</summary>
    public async Task<IReadOnlyList<Moniteur>> ListerInspecteursTivAsync(CancellationToken ct = default) =>
        (await moniteurs.ListerAsync(actifsSeulement: true, ct)).Where(m => m.InspecteurTiv).ToList();

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

    /// <summary>Ajoute (Id = 0) ou modifie un moniteur. Prénom et nom obligatoires.</summary>
    public async Task<ResultatOperation> EnregistrerAsync(Moniteur saisie, CancellationToken ct = default)
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

        if (await moniteurs.ObtenirAsync(moniteur.Id, ct) is null)
        {
            return ResultatOperation.Echec("Moniteur introuvable.");
        }

        await moniteurs.MettreAJourAsync(moniteur, ct);
        await JournaliserAsync(ActionsJournal.MoniteurModifie, $"Moniteur {moniteur.NomComplet} modifié", ct);
        return ResultatOperation.Ok($"{moniteur.NomComplet} enregistré.", moniteur.Id);
    }

    private Task JournaliserAsync(string action, string details, CancellationToken ct) =>
        journal.AjouterAsync(new EntreeJournal { Horodatage = horloge.MaintenantUtc, Action = action, Details = details }, ct);
}
