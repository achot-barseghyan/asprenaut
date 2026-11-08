using System.Data;
using System.Globalization;
using Dapper;

namespace Asprenaut.Materiel.Data.Connexion;

/// <summary>
/// Configuration globale de Dapper : colonnes snake_case → propriétés PascalCase,
/// dates en texte ISO 8601.
/// Note : en paramètre, Dapper ignore les TypeHandler pour les énumérations et pour DateTime
/// (il écrirait un entier et "2026-11-06 18:30:00"). Les dépôts passent donc explicitement
/// <c>enum.ToString()</c> et <c>FormatIso.Ecrire(date)</c>. En lecture, les handlers
/// ci-dessous et la conversion texte → énumération de Dapper fonctionnent.
/// </summary>
internal static class ConfigurationDapper
{
    private static int _initialise;

    public static void Initialiser()
    {
        if (Interlocked.Exchange(ref _initialise, 1) == 1)
        {
            return;
        }

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new GestionnaireHorodatage());
        SqlMapper.AddTypeHandler(new GestionnaireDate());
    }
}

/// <summary>Formats de stockage des dates.</summary>
public static class FormatIso
{
    /// <summary>Horodatage UTC : 2026-11-06T18:30:00.000Z</summary>
    public const string Horodatage = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    /// <summary>Date seule : 2026-11-06</summary>
    public const string Date = "yyyy-MM-dd";

    public static string Ecrire(DateTime valeur)
    {
        var utc = valeur.Kind switch
        {
            DateTimeKind.Utc => valeur,
            DateTimeKind.Local => valeur.ToUniversalTime(),
            _ => DateTime.SpecifyKind(valeur, DateTimeKind.Utc),
        };
        return utc.ToString(Horodatage, CultureInfo.InvariantCulture);
    }

    public static string? Ecrire(DateTime? valeur) => valeur is { } v ? Ecrire(v) : null;

    public static DateTime LireHorodatage(string texte) =>
        DateTime.Parse(texte, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    public static string Ecrire(DateOnly valeur) => valeur.ToString(Date, CultureInfo.InvariantCulture);

    public static string? Ecrire(DateOnly? valeur) => valeur is { } v ? Ecrire(v) : null;

    public static DateOnly LireDate(string texte) => DateOnly.ParseExact(texte, Date, CultureInfo.InvariantCulture);
}

internal sealed class GestionnaireHorodatage : SqlMapper.TypeHandler<DateTime>
{
    public override void SetValue(IDbDataParameter parameter, DateTime value)
    {
        parameter.DbType = DbType.String;
        parameter.Value = FormatIso.Ecrire(value);
    }

    public override DateTime Parse(object value) => FormatIso.LireHorodatage((string)value);
}

internal sealed class GestionnaireDate : SqlMapper.TypeHandler<DateOnly>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.DbType = DbType.String;
        parameter.Value = FormatIso.Ecrire(value);
    }

    public override DateOnly Parse(object value) => FormatIso.LireDate((string)value);
}
