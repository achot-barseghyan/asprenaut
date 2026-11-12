using System.Globalization;
using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Saisie;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>
/// Export de toutes les données en CSV (un fichier par table, lisible dans Excel) : matériel, moniteurs, sorties,
/// matériel des sorties, contrôles, atelier, séances TIV, journal. Dates en français, états en clair.
/// Libre en mode consultation, comme une sauvegarde.
/// </summary>
public sealed class ServiceExport(IExportRepository depot, IHorloge horloge)
{
    /// <summary>Dossier créé dans le dossier choisi : « Export Asprenaut 2026-11-10 ».</summary>
    public string NomDossier() => $"Export Asprenaut {horloge.AujourdHui:yyyy-MM-dd}";

    public async Task<IReadOnlyList<FichierExport>> ExporterAsync(CancellationToken ct = default)
    {
        var tables = await depot.ListerTablesAsync(ct);
        return tables.Select(t => new FichierExport(
            t.Fichier,
            Csv.Ecrire(t.Colonnes.Select(c => c.Nom), t.Lignes.Select(l => t.Colonnes.Select((c, i) => Formater(i < l.Length ? l[i] : null, c.Genre)))),
            t.Lignes.Count)).ToList();
    }

    /// <summary>Valeur brute de la base → texte lisible. Une valeur inattendue est laissée telle quelle.</summary>
    public static string Formater(string? brut, GenreColonne genre)
    {
        if (string.IsNullOrEmpty(brut))
        {
            return "";
        }

        return genre switch
        {
            GenreColonne.Date when DateOnly.TryParseExact(brut, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) => Libelles.Date(d),
            GenreColonne.Horodatage when DateTime.TryParse(brut, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var h) =>
                DateTime.SpecifyKind(h, DateTimeKind.Utc).ToLocalTime().ToString("dd/MM/yyyy HH:mm", Libelles.Culture),
            GenreColonne.OuiNon => brut == "1" ? "Oui" : brut == "0" ? "Non" : brut,
            GenreColonne.EtatEquipement when Enum.TryParse<EtatEquipement>(brut, out var e) => Libelles.De(e),
            GenreColonne.GraviteSignalement when Enum.TryParse<GraviteSignalement>(brut, out var g) => ServiceSignalements.Libelle(g),
            GenreColonne.TypeSortie when Enum.TryParse<TypeSortie>(brut, out var t) => Libelles.De(t),
            GenreColonne.StatutSortie when Enum.TryParse<StatutSortie>(brut, out var s) => Libelles.De(s),
            GenreColonne.StatutSortieEquipement when Enum.TryParse<StatutSortieEquipement>(brut, out var se) => se switch
            {
                StatutSortieEquipement.Dehors => "Dehors",
                StatutSortieEquipement.Rendu => "Rendu",
                _ => "Manquant",
            },
            GenreColonne.TypeControle when Enum.TryParse<TypeControle>(brut, out var tc) => tc switch
            {
                TypeControle.Tiv => "TIV",
                TypeControle.Requalification => "Requalification",
                _ => "Révision",
            },
            GenreColonne.ResultatControle when Enum.TryParse<ResultatControle>(brut, out var r) => ServiceRevisions.LibelleResultat(r),
            GenreColonne.Montage when Enum.TryParse<MontageDetendeur>(brut, out var m) => Libelles.De(m),
            _ => brut,
        };
    }
}
