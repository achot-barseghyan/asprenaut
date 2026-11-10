using System.Text;

namespace Asprenaut.Materiel.Core.Saisie;

/// <summary>
/// Fichiers CSV lisibles par Excel en français : séparateur « ; », champs entre guillemets si besoin.
/// Le fichier est à écrire en UTF-8 avec BOM (sinon Excel lit mal les accents).
/// </summary>
public static class Csv
{
    public static string Ecrire(IEnumerable<string> entetes, IEnumerable<IEnumerable<string?>> lignes)
    {
        var csv = new StringBuilder();
        csv.AppendJoin(';', entetes.Select(Champ)).AppendLine();
        foreach (var ligne in lignes)
        {
            csv.AppendJoin(';', ligne.Select(Champ)).AppendLine();
        }

        return csv.ToString();
    }

    /// <summary>
    /// Un champ : entre guillemets s'il contient « ; », un guillemet ou un retour à la ligne. Un texte qui commencerait
    /// par = + - @ serait pris par Excel pour une formule : il est précédé d'une apostrophe (sauf les nombres négatifs).
    /// </summary>
    public static string Champ(string? valeur)
    {
        var v = valeur ?? "";
        if (v.Length > 0 && v[0] is '=' or '+' or '-' or '@' && !double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            v = "'" + v;
        }

        return v.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{v.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : v;
    }
}
