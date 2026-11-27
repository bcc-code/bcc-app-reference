using System.Security.Claims;

namespace BccCode.Template.Security;

/// <summary>
/// The claim types the session carries. The OIDC handlers run with MapInboundClaims = false, so the ones from a token are the
/// raw JWT names.
/// </summary>
public static class AuthClaims
{
    /// <summary>BCC Signon's person id, in its ID token only. The app copies the value into <see cref="PersonId"/> and keeps no claim of this name.</summary>
    public const string SignonPersonUid = "https://login.bcc.no/claims/personUid";
    public const string Subject = "sub";
    public const string Name = "name";
    public const string Role = "role";
    public const string SessionId = "sid";
    public const string Issuer = "iss";

    /// <summary>Entra's object id and tenant id, read from the ID token at sign-in only.</summary>
    public const string ObjectId = "oid";
    public const string TenantId = "tid";

    /// <summary>
    /// Set by the app, never taken from an identity provider: the person id (a Guid) that keys all data. For BCC Signon it is the
    /// <c>personUid</c>; for Entra it is the id the app issued the first time the subject signed in (ADR 0031).
    /// </summary>
    public const string PersonId = "template:person";

    /// <summary>Set by the app: which provider the session came from (<see cref="AuthProviders"/>), so logout signs out of the right one.</summary>
    public const string Provider = "template:provider";

    /// <summary>
    /// Set by the app, never taken from the identity provider: "true" when the sign-in had a second factor (<c>amr</c> has
    /// <c>mfa</c>). An administrator's session needs it.
    /// </summary>
    public const string Mfa = "template:mfa";
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The signed-in person's id. It keys the user's data. The caller's own id is never logged or returned to the caller's browser;
    /// administrators see other people's ids on the admin people API. The default and fallback policies require the claim, so a
    /// signed-in request always has one.
    /// </summary>
    public static Guid PersonId(this ClaimsPrincipal user)
    {
        return user.TryPersonId(out var personId)
            ? personId
            : throw new InvalidOperationException($"The signed-in user has no valid {AuthClaims.PersonId} claim.");
    }

    public static bool TryPersonId(this ClaimsPrincipal user, out Guid personId)
    {
        return Guid.TryParse(user.FindFirstValue(AuthClaims.PersonId), out personId);
    }

    public static string DisplayName(this ClaimsPrincipal user)
    {
        return user.FindFirstValue(AuthClaims.Name) ?? string.Empty;
    }

    public static IReadOnlyList<string> Roles(this ClaimsPrincipal user)
    {
        return [.. user.FindAll(AuthClaims.Role).Select(c => c.Value)];
    }
}

/// <summary>The identity providers an app can sign in with. The keys are used in configuration, URLs, the database and the provider claim.</summary>
public static class AuthProviders
{
    public const string BccSignon = "bcc-signon";
    public const string Entra = "entra";

    public static bool IsKnown(string? provider)
    {
        return provider is BccSignon or Entra;
    }
}
