using BccCode.Template.Api.Configuration;
using BccCode.Template.Security;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace BccCode.Template.Api.Auth;

/// <summary>
/// Which identity providers the app signs in with, decided by configuration alone (ADR 0031). BCC Signon is on when
/// <c>Oidc:Authority</c> and <c>Oidc:ClientId</c> are set, Entra ID when <c>Entra:TenantId</c> and <c>Entra:ClientId</c> are set. Registered
/// as a singleton, so the sign-in routes and the cookie scheme read the same answer the schemes were registered with.
/// </summary>
public sealed record EnabledProviders(bool Signon, bool Entra)
{
    public const string EntraScheme = "Entra";

    public const string SignonAuthorityKey = "Oidc:Authority";
    public const string SignonClientIdKey = "Oidc:ClientId";
    public const string SignonClientSecretKey = "Oidc:ClientSecret";
    public const string EntraTenantKey = "Entra:TenantId";
    public const string EntraClientIdKey = "Entra:ClientId";
    public const string EntraClientSecretKey = "Entra:ClientSecret";

    /// <summary>Only for the local mock and tests; the host refuses it elsewhere (<see cref="AuthConfiguration.ValidateProviders"/>).</summary>
    public const string EntraAuthorityKey = "Entra:Authority";

    private static readonly string[] SignonKeys = [SignonAuthorityKey, SignonClientIdKey, SignonClientSecretKey];
    private static readonly string[] EntraKeys = [EntraTenantKey, EntraClientIdKey, EntraClientSecretKey];

    public int Count => (Signon ? 1 : 0) + (Entra ? 1 : 0);

    public bool Both => Signon && Entra;

    /// <summary>The provider keys that are on, in the order the sign-in page lists them.</summary>
    public IReadOnlyList<string> Keys
    {
        get
        {
            List<string> keys = [];
            if (Signon) keys.Add(AuthProviders.BccSignon);
            if (Entra) keys.Add(AuthProviders.Entra);
            return keys;
        }
    }

    public bool Has(string? provider)
    {
        return provider switch
        {
            AuthProviders.BccSignon => Signon,
            AuthProviders.Entra => Entra,
            _ => false,
        };
    }

    /// <summary>The scheme of a known provider key; null for any other key.</summary>
    public static string? SchemeOf(string? provider)
    {
        return provider switch
        {
            AuthProviders.BccSignon => OpenIdConnectDefaults.AuthenticationScheme,
            AuthProviders.Entra => EntraScheme,
            _ => null,
        };
    }

    public static bool IsSet(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>
    /// Development and Test turn a provider on by its authority (Signon) or tenant and client (Entra) and need no secret, because the
    /// local mock registers public PKCE clients. Elsewhere a provider is on with its authority and client id; the startup check
    /// (<see cref="AuthConfiguration.ValidateProviders"/>) has already refused a half configured one.
    /// </summary>
    public static EnabledProviders From(IConfiguration configuration, IHostEnvironment environment)
    {
        var lenient = environment.IsDevelopmentOrTest();
        var signon = IsSet(configuration[SignonAuthorityKey]) && (lenient || IsSet(configuration[SignonClientIdKey]));
        var entra = IsSet(configuration[EntraTenantKey]) && IsSet(configuration[EntraClientIdKey]);
        return new EnabledProviders(signon, entra);
    }

    /// <summary>Names the settings that are missing for the providers that have any setting, or all of them when there is none.</summary>
    internal static IReadOnlyList<string> Problems(IConfiguration configuration)
    {
        List<string> problems = [];
        foreach (var (name, keys) in new[] { ("BCC Signon", SignonKeys), ("Entra ID", EntraKeys) })
        {
            var missing = keys.Where(key => !IsSet(configuration[key])).ToList();
            if (missing.Count > 0 && missing.Count < keys.Length)
                problems.Add($"{string.Join(", ", missing)} must be set outside Development and Test when {name} sign-in is configured.");
        }

        if (IsSet(configuration[EntraAuthorityKey]))
            problems.Add($"{EntraAuthorityKey} may only be set in Development and Test: outside them the authority is the tenant's.");

        if (!SignonKeys.Concat(EntraKeys).Any(key => IsSet(configuration[key])))
            problems.Add($"No sign-in provider is configured: set {string.Join(", ", SignonKeys)} (BCC Signon) or {string.Join(", ", EntraKeys)} (Entra ID) outside Development and Test.");
        return problems;
    }
}
