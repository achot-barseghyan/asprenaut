using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>Saisie de la fenêtre « Nouveau modèle » / « Modifier le modèle ». <see cref="Utilisations"/> null = celles du type.</summary>
public sealed record SaisieModele(
    string Nom,
    DayOfWeek? Jour,
    long? TypeId,
    long? MoniteurId,
    string Lieu = "",
    string Intitule = "",
    int? Utilisations = null,
    bool UnParCategorie = true,
    string Notes = "");

/// <summary>
/// Un modèle avec ce qu'il donnera : son type et son responsable, la date de la prochaine sortie (son jour habituel, aujourd'hui
/// compris), l'intitulé (tapé ou proposé), les utilisations et, s'il ne peut pas être lancé, ce qui manque.
/// </summary>
public sealed record FicheModele(
    ModeleSortie Modele,
    TypeDeSortie? Type,
    Moniteur? Responsable,
    DateOnly Prochaine,
    bool Aujourdhui,
    string Intitule,
    int Utilisations,
    string? Probleme)
{
    /// <summary>Utilisations différentes de celles du type (réglées en mode gestion).</summary>
    public bool UtilisationsModifiees => Type is not null && Utilisations != Type.Utilisations;
}

/// <summary>
/// Modèles de sortie (écran Sorties : onglet « Modèles », « Démarrage rapide ») : les sorties qui reviennent toujours pareil,
/// créées ensuite en un clic. Mêmes règles qu'une nouvelle sortie : changer les utilisations du type ou couper « un équipement
/// par catégorie » demande le mode gestion, une fois, à l'enregistrement du modèle ; le lancement est ensuite libre.
/// </summary>
public sealed class ServiceModelesSortie(
    IModeleSortieRepository modeles,
    ITypeSortieRepository types,
    IMoniteurRepository moniteurs,
    ServiceSortie sorties,
    IHorloge horloge,
    ServiceGestion? gestion = null)
{
    /// <summary>Modèles proposés dans « Démarrage rapide » de l'onglet En cours.</summary>
    public const int NombreDemarrageRapide = 4;

    /// <summary>Prochain jour <paramref name="jour"/> à partir d'aujourd'hui compris ; sans jour fixe, aujourd'hui.</summary>
    public static DateOnly ProchaineDate(DayOfWeek? jour, DateOnly aujourdhui) =>
        jour is { } j ? aujourdhui.AddDays(((int)j - (int)aujourdhui.DayOfWeek + 7) % 7) : aujourdhui;

    /// <summary>« Lundi », « Mardi »…</summary>
    public static string NomJour(DayOfWeek jour)
    {
        var nom = Libelles.Culture.DateTimeFormat.GetDayName(jour);
        return char.ToUpper(nom[0], Libelles.Culture) + nom[1..];
    }

    /// <summary>Jours dans l'ordre de la semaine du club : lundi d'abord.</summary>
    public static IReadOnlyList<DayOfWeek> Semaine { get; } =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    /// <summary>Onglet « Modèles » : du lundi au dimanche, puis ceux sans jour fixe ; par nom.</summary>
    public async Task<IReadOnlyList<FicheModele>> ListerAsync(CancellationToken ct = default) =>
        (await FichesAsync(ct))
            .OrderBy(f => f.Modele.Jour is { } j ? ((int)j + 6) % 7 : 7)
            .ThenBy(f => f.Modele.Nom, StringComparer.Create(Libelles.Culture, true))
            .ToList();

    /// <summary>« Démarrage rapide » : les plus proches d'abord (celui du jour en tête), sans jour fixe à la fin.</summary>
    public async Task<IReadOnlyList<FicheModele>> DemarrageRapideAsync(int nombre = NombreDemarrageRapide, CancellationToken ct = default)
    {
        var aujourdhui = horloge.AujourdHui;
        return (await FichesAsync(ct))
            .OrderBy(f => f.Modele.Jour is { } j ? ((int)j - (int)aujourdhui.DayOfWeek + 7) % 7 : 8)
            .ThenBy(f => f.Modele.Nom, StringComparer.Create(Libelles.Culture, true))
            .Take(Math.Max(1, nombre))
            .ToList();
    }

    public async Task<FicheModele?> ObtenirAsync(long id, CancellationToken ct = default) =>
        (await FichesAsync(ct)).FirstOrDefault(f => f.Modele.Id == id);

    private async Task<IReadOnlyList<FicheModele>> FichesAsync(CancellationToken ct)
    {
        var liste = await modeles.ListerAsync(ct);
        var tousTypes = await types.ListerAsync(ct);
        var responsables = await moniteurs.ListerAsync(actifsSeulement: false, ct);
        var aujourdhui = horloge.AujourdHui;
        return liste.Select(m =>
        {
            var type = tousTypes.FirstOrDefault(t => t.Id == m.TypeId);
            var responsable = responsables.FirstOrDefault(r => r.Id == m.MoniteurId);
            var date = ProchaineDate(m.Jour, aujourdhui);
            var probleme = type is null ? "type à choisir"
                : !type.Actif ? $"le type {type.Nom} est masqué"
                : responsable is null ? "responsable à choisir"
                : !responsable.Actif ? $"{responsable.NomComplet} n'est plus {(responsable.EstClasse ? "active" : "actif")}"
                : null;
            var intitule = m.Intitule.Trim().Length > 0 ? m.Intitule.Trim() : ServiceSortie.IntitulePropose(type, date, responsable, m.Lieu);
            return new FicheModele(m, type, responsable, date, date == aujourdhui, intitule, m.Utilisations ?? type?.Utilisations ?? 1, probleme);
        }).ToList();
    }

    /// <summary>
    /// Ajoute (<paramref name="id"/> null) ou modifie un modèle. Nom obligatoire, unique sans tenir compte des majuscules,
    /// 40 caractères au plus ; type proposé et responsable actif (ceux déjà choisis restent acceptés) ; utilisations
    /// différentes du type ou règle « un par catégorie » coupée : mode gestion, si ce n'était pas déjà le réglage du modèle.
    /// </summary>
    public async Task<ResultatOperation> EnregistrerAsync(long? id, SaisieModele saisie, CancellationToken ct = default)
    {
        ModeleSortie? actuel = null;
        if (id is { } idModele && (actuel = await modeles.ObtenirAsync(idModele, ct)) is null)
        {
            return ResultatOperation.Echec("Modèle introuvable.");
        }

        var nom = saisie.Nom.Trim();
        if (nom.Length == 0)
        {
            return ResultatOperation.Echec("Donnez un nom au modèle.");
        }

        if (nom.Length > ModeleSortie.LongueurMaxNom)
        {
            return ResultatOperation.Echec($"Nom trop long : {ModeleSortie.LongueurMaxNom} caractères au plus.");
        }

        var existants = await modeles.ListerAsync(ct);
        if (existants.Any(m => m.Id != id && string.Equals(m.Nom.Trim(), nom, StringComparison.CurrentCultureIgnoreCase)))
        {
            return ResultatOperation.Echec("Un modèle porte déjà ce nom.");
        }

        var intitule = saisie.Intitule.Trim();
        var lieu = saisie.Lieu.Trim();
        var notes = saisie.Notes.Trim();
        if (intitule.Length > ModeleSortie.LongueurMaxIntitule)
        {
            return ResultatOperation.Echec($"Intitulé trop long : {ModeleSortie.LongueurMaxIntitule} caractères au plus.");
        }

        if (lieu.Length > ModeleSortie.LongueurMaxLieu)
        {
            return ResultatOperation.Echec($"Lieu trop long : {ModeleSortie.LongueurMaxLieu} caractères au plus.");
        }

        if (notes.Length > ModeleSortie.LongueurMaxNotes)
        {
            return ResultatOperation.Echec($"Notes trop longues : {ModeleSortie.LongueurMaxNotes} caractères au plus.");
        }

        var type = (await types.ListerAsync(ct)).FirstOrDefault(t => t.Id == saisie.TypeId && (t.Actif || t.Id == actuel?.TypeId));
        if (type is null)
        {
            return ResultatOperation.Echec("Choisissez un type de sortie.");
        }

        var responsable = saisie.MoniteurId is { } mid ? await moniteurs.ObtenirAsync(mid, ct) : null;
        if (responsable is null || (!responsable.Actif && responsable.Id != actuel?.MoniteurId))
        {
            return ResultatOperation.Echec("Choisissez le responsable : un enseignant ou une classe.");
        }

        var n = saisie.Utilisations ?? type.Utilisations;
        if (n is < TypeDeSortie.UtilisationsMin or > TypeDeSortie.UtilisationsMax)
        {
            return ResultatOperation.Echec($"Utilisations : entre {TypeDeSortie.UtilisationsMin} et {TypeDeSortie.UtilisationsMax} par sortie.");
        }

        // Celles du type : rien n'est retenu (le modèle suivra le type). Une autre valeur, nouvelle : mode gestion.
        int? utilisations = n == type.Utilisations ? null : n;
        var dejaReglees = actuel is not null && actuel.Utilisations == utilisations && actuel.TypeId == type.Id;
        if (utilisations is not null && !dejaReglees && gestion?.Exiger(ActionProtegee.CompteurSortie) is { Reussi: false } refusCompteur)
        {
            return refusCompteur;
        }

        // La règle ne concerne que les sorties nominatives ; la couper (si elle ne l'était pas) demande le mode gestion.
        var unParCategorie = !type.Nominatif || saisie.UnParCategorie;
        if (!unParCategorie && (actuel is null || actuel.UnParCategorie)
            && gestion?.Exiger(ActionProtegee.PlusieursParCategorie) is { Reussi: false } refusCategorie)
        {
            return refusCategorie;
        }

        var modele = actuel ?? new ModeleSortie();
        modele.Nom = nom;
        modele.Jour = saisie.Jour;
        modele.TypeId = type.Id;
        modele.MoniteurId = responsable.Id;
        modele.Lieu = lieu;
        modele.Intitule = intitule;
        modele.Utilisations = utilisations;
        modele.UnParCategorie = unParCategorie;
        modele.Notes = notes;
        if (actuel is null)
        {
            var nouveau = await modeles.AjouterAsync(modele, ct);
            return ResultatOperation.Ok($"Modèle « {nom} » créé.", nouveau);
        }

        await modeles.MettreAJourAsync(modele, ct);
        return ResultatOperation.Ok("Modèle mis à jour.", modele.Id);
    }

    /// <summary>Copie « Nom (copie) » (puis « (copie 2) »…), mêmes réglages.</summary>
    public async Task<ResultatOperation> DupliquerAsync(long id, CancellationToken ct = default)
    {
        if (await modeles.ObtenirAsync(id, ct) is not { } source)
        {
            return ResultatOperation.Echec("Modèle introuvable.");
        }

        var pris = (await modeles.ListerAsync(ct)).Select(m => m.Nom.Trim()).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        string nom;
        var i = 1;
        do
        {
            var suffixe = i == 1 ? " (copie)" : $" (copie {i})";
            var debut = source.Nom.Trim();
            nom = (debut.Length + suffixe.Length > ModeleSortie.LongueurMaxNom ? debut[..(ModeleSortie.LongueurMaxNom - suffixe.Length)].TrimEnd() : debut) + suffixe;
            i++;
        }
        while (pris.Contains(nom));

        var copie = new ModeleSortie
        {
            Nom = nom,
            Jour = source.Jour,
            TypeId = source.TypeId,
            MoniteurId = source.MoniteurId,
            Lieu = source.Lieu,
            Intitule = source.Intitule,
            Utilisations = source.Utilisations,
            UnParCategorie = source.UnParCategorie,
            Notes = source.Notes,
        };
        var nouveau = await modeles.AjouterAsync(copie, ct);
        return ResultatOperation.Ok($"Modèle « {nom} » créé.", nouveau);
    }

    public async Task<ResultatOperation> SupprimerAsync(long id, CancellationToken ct = default)
    {
        if (await modeles.ObtenirAsync(id, ct) is not { } modele)
        {
            return ResultatOperation.Echec("Modèle introuvable.");
        }

        await modeles.SupprimerAsync(id, ct);
        return ResultatOperation.Ok($"Modèle « {modele.Nom} » supprimé.", id);
    }

    /// <summary>
    /// « Créer la sortie » : sortie en préparation datée du prochain jour du modèle (aujourd'hui compris), avec son type,
    /// son responsable, son lieu, son intitulé (ou celui proposé), ses utilisations, sa règle et ses notes. Les sorties
    /// d'un modèle ne sont pas liées à lui : le modifier ensuite ne les touche pas.
    /// </summary>
    public async Task<ResultatOperation> CreerSortieAsync(long id, CancellationToken ct = default)
    {
        if (await ObtenirAsync(id, ct) is not { } fiche)
        {
            return ResultatOperation.Echec("Modèle introuvable.");
        }

        if (fiche.Probleme is not null ||fiche.Type is null || fiche.Responsable is null)
        {
            return ResultatOperation.Echec($"Modèle « {fiche.Modele.Nom} » : {fiche.Probleme ?? "à compléter"}. Modifiez-le.");
        }

        var m = fiche.Modele;
        var demande = new NouvelleSortie(
            fiche.Prochaine,
            fiche.Type.Pictogramme,
            fiche.Intitule,
            fiche.Responsable.Id,
            m.Lieu,
            m.Notes,
            fiche.Type.Id,
            fiche.Utilisations,
            m.UnParCategorie);
        var resultat = await sorties.CreerAsync(demande, reglagesApprouves: true, ct);
        return resultat.Reussi ? ResultatOperation.Ok($"« {fiche.Intitule} » créée.", resultat.Id) : resultat;
    }
}
