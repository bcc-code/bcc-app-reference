using System.Security.Claims;

namespace BccCode.Template.Security;

/// <summary>
/// The claim types the session carries (BCC Signon). The OIDC handler runs with MapInboundClaims = false, so these are the
/// raw JWT names.
/// </summary>
public static class AuthClaims
{
    public const string PersonUid = "https://login.bcc.no/claims/personUid";
    public const string Subject = "sub";
    public const string Name = "name";
    public const string Role = "role";
    public const string SessionId = "sid";
    public const string Issuer = "iss";

    /// <summary>
    /// Set by the app, never taken from the identity provider: "true" when the sign-in had a second factor (<c>amr</c> has
    /// <c>mfa</c>). An administrator's session needs it.
    /// </summary>
    public const string Mfa = "template:mfa";
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The signed-in person's uid. It keys the user's data and is never logged or returned to the browser. The default and
    /// fallback policies require the claim, so a signed-in request always has one.
    /// </summary>
    public static Guid PersonUid(this ClaimsPrincipal user)
    {
        return user.TryPersonUid(out var personUid)
            ? personUid
            : throw new InvalidOperationException($"The signed-in user has no valid {AuthClaims.PersonUid} claim.");
    }

    public static bool TryPersonUid(this ClaimsPrincipal user, out Guid personUid)
    {
        return Guid.TryParse(user.FindFirstValue(AuthClaims.PersonUid), out personUid);
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
