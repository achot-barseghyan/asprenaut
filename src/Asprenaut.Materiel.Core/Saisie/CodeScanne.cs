using System.Text;
using System.Text.RegularExpressions;

namespace Asprenaut.Materiel.Core.Saisie;

/// <summary>Ce qu'on lit sur une étiquette : le code, et le type écrit entre parenthèses sur les médailles du club.</summary>
public sealed record LectureEtiquette(string Code, string? Type);

/// <summary>Normalisation des codes lus par le pistolet scanner ou tapés au clavier.</summary>
public static partial class CodeScanne
{
    /// <summary>Préfixe éventuel imprimé dans les QR codes du club.</summary>
    public const string Prefixe = "ASP:";

    /// <summary>Exemple réel de médaille, pour les écrans d'aide.</summary>
    public const string ExempleMedaille = "(Detendeur) A24 Asprenaut Chalon 14 71-0025";

    // Médaille du club : « (Type) CODE Asprenaut Chalon 14 71-0025 » → le code est le mot qui suit le type.
    [GeneratedRegex(@"^\s*\((?<type>[^)]+)\)\s*(?<code>[A-Za-z]+-?\d+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex Medaille();

    /// <summary>
    /// Code et type d'une étiquette. Médaille « (Detendeur) A24 Asprenaut Chalon 14 71-0025 » → (A24, Detendeur) ;
    /// sinon le texte entier nettoyé (voir <see cref="Nettoyer"/>), sans type — pour taper A24 à la main.
    /// </summary>
    public static LectureEtiquette Lire(string? brut, string? prefixe = Prefixe)
    {
        if (string.IsNullOrEmpty(brut))
        {
            return new LectureEtiquette("", null);
        }

        var m = Medaille().Match(brut);
        if (m.Success)
        {
            var type = m.Groups["type"].Value.Trim();
            return new LectureEtiquette(m.Groups["code"].Value.ToUpperInvariant(), type.Length > 0 ? type : null);
        }

        var sb = new StringBuilder(brut.Length);
        foreach (var c in brut)
        {
            if (!char.IsWhiteSpace(c) && !char.IsControl(c))
            {
                sb.Append(char.ToUpperInvariant(c));
            }
        }

        var code = sb.ToString();
        var p = NormaliserPrefixe(prefixe);
        if (p.Length > 0 && code.StartsWith(p, StringComparison.Ordinal))
        {
            code = code[p.Length..];
        }

        return new LectureEtiquette(code, null);
    }

    /// <summary>
    /// Code d'une étiquette : celui de la médaille (« (Detendeur) A24 Asprenaut… » → A24), sinon le texte sans espaces
    /// ni caractères de contrôle, sans le préfixe (par défaut "ASP:"), en majuscules : " asp: bloc-031 " → "BLOC-031".
    /// Un préfixe vide ne retire rien.
    /// </summary>
    public static string Nettoyer(string? brut, string? prefixe = Prefixe) => Lire(brut, prefixe).Code;

    /// <summary>Préfixe sans espaces, en majuscules ("asp: " → "ASP:").</summary>
    public static string NormaliserPrefixe(string? prefixe) =>
        string.Concat((prefixe ?? "").Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c))).ToUpperInvariant();
}

/// <summary>
/// Anti-rebond : ignore le même code reçu deux fois dans un court délai
/// (double lecture du pistolet). Le temps est fourni par l'appelant (millisecondes).
/// </summary>
public sealed class AntiRebond(int delaiMs = 800)
{
    private string? _dernierCode;
    private long _dernierInstantMs;

    public int DelaiMs { get; } = delaiMs;

    /// <summary>Retourne vrai si le code doit être traité, faux s'il est un doublon rapproché.</summary>
    public bool Accepter(string code, long instantMs)
    {
        var doublon = _dernierCode == code && instantMs - _dernierInstantMs < DelaiMs;
        _dernierCode = code;
        _dernierInstantMs = instantMs;
        return !doublon;
    }
}
