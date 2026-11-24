using System.Text.RegularExpressions;

namespace BccCode.Template;

/// <summary>
/// The Postgres schema the app's tables live in (ADR 0027). A PR preview runs in its own schema, everything else in public.
/// </summary>
public static partial class PersistenceSchema
{
    public const string Key = "Persistence:Schema";
    public const string Default = "public";

    public static string Read(IConfiguration configuration)
    {
        var value = configuration[Key];
        if (string.IsNullOrWhiteSpace(value))
            return Default;
        if (!ValidName().IsMatch(value))
            throw new InvalidOperationException($"{Key} must match ^[a-z_][a-z0-9_]{{0,62}}$, but is '{value}'.");
        return value;
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex ValidName();
}
