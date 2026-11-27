using BccCode.Template.Security;

namespace BccCode.Template.Api.Auth;

/// <summary>An <c>Auth:BootstrapAdmins</c> entry: the provider key and the id the provider knows the person by (Signon <c>personUid</c>, Entra <c>oid</c>).</summary>
public sealed record BootstrapAdmin(string Provider, string Id);

/// <summary>
/// <c>Auth:BootstrapAdmins</c>: one string of <c>&lt;provider&gt;:&lt;id&gt;</c> entries separated by <c>,</c> or <c>;</c>. It is one setting so
/// that Secret Manager can hold it: the ids belong to real people and are personal data, so they are not committed (standard 18.15).
/// Whitespace around an entry is ignored and an empty entry is skipped.
/// </summary>
public static class BootstrapAdmins
{
    public const string Key = "Auth:BootstrapAdmins";

    /// <summary>The well-formed entries. A malformed one is left out here; <see cref="Validate"/> refuses it at startup.</summary>
    public static IReadOnlyList<BootstrapAdmin> Parse(string? setting)
    {
        return [.. Entries(setting).Select(TryParseEntry).OfType<BootstrapAdmin>()];
    }

    /// <summary>The problem with the setting, or null. It names the setting and the position of the entry, never a value.</summary>
    public static string? Validate(string? setting)
    {
        var position = 0;
        foreach (var entry in Entries(setting))
        {
            position++;
            if (TryParseEntry(entry) is null)
                return $"{Key} entry {position} is not <provider>:<id> with the provider one of {AuthProviders.BccSignon} or {AuthProviders.Entra}.";
        }

        return null;
    }

    private static IEnumerable<string> Entries(string? setting)
    {
        return (setting ?? string.Empty).Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static BootstrapAdmin? TryParseEntry(string entry)
    {
        var colon = entry.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0) return null;
        var (provider, id) = (entry[..colon].Trim(), entry[(colon + 1)..].Trim());
        return AuthProviders.IsKnown(provider) && id.Length > 0 ? new BootstrapAdmin(provider, id) : null;
    }
}
