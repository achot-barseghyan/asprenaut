using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Saisie;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>Inventaire : consultation et création des équipements.</summary>
public sealed class ServiceInventaire(
    IEquipementRepository equipements,
    ICategorieRepository categories,
    IJournalRepository journal,
    IHorloge horloge,
    Reglages reglages)
{
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
    public async Task<ResultatOperation> CreerAsync(Equipement nouveau, CancellationToken ct = default)
    {
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

        var aujourdHui = horloge.AujourdHui;
        if (nouveau.DateProchaineRevision < aujourdHui || nouveau.DateRequalification < aujourdHui)
        {
            return ResultatOperation.Echec("Une date de prochain contrôle est déjà passée. Vérifiez-la.");
        }

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
        await journal.AjouterAsync(new EntreeJournal
        {
            Horodatage = horloge.MaintenantUtc,
            Action = ActionsJournal.EquipementCree,
            Details = $"{code} « {nouveau.Libelle} » créé",
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
