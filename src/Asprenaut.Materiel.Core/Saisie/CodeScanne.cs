using System.Text;

namespace Asprenaut.Materiel.Core.Saisie;

/// <summary>Normalisation des codes lus par le pistolet scanner ou tapés au clavier.</summary>
public static class CodeScanne
{
    /// <summary>Préfixe éventuel imprimé dans les QR codes du club.</summary>
    public const string Prefixe = "ASP:";

    /// <summary>
    /// Supprime espaces et caractères de contrôle, retire le préfixe (par défaut "ASP:"), passe en majuscules.
    /// " asp: bloc-031 " → "BLOC-031". Un préfixe vide ne retire rien.
    /// </summary>
    public static string Nettoyer(string? brut, string? prefixe = Prefixe)
    {
        if (string.IsNullOrEmpty(brut))
        {
            return "";
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

        return code;
    }

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
