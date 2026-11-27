using System.Security.Claims;
using BccCode.Template.Security;

namespace BccCode.Template.Api.Auth;

/// <summary>
/// Keeps only the claims the app reads before the session is written (standard 14, rule 3): a smaller ticket and less
/// personal data at rest. <c>sub</c>, <c>sid</c> and <c>iss</c> stay because back-channel logout and a revoke find sessions with them.
/// </summary>
public static class SessionClaims
{
    private static readonly HashSet<string> Kept =
    [
        AuthClaims.Subject, AuthClaims.Name, AuthClaims.Role, AuthClaims.SessionId, AuthClaims.Issuer,
        AuthClaims.PersonId, AuthClaims.Provider, AuthClaims.Mfa,
    ];

    public static ClaimsPrincipal Slim(ClaimsPrincipal principal)
    {
        var claims = principal.Claims.Where(c => Kept.Contains(c.Type)).Select(c => new Claim(c.Type, c.Value));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, principal.Identity?.AuthenticationType ?? "oidc", AuthClaims.Name, AuthClaims.Role));
    }

    /// <summary>BCC Signon's person id from its ID token, or null when the claim is missing or not a Guid.</summary>
    public static Guid? SignonPersonUid(ClaimsPrincipal principal)
    {
        return Guid.TryParse(principal.FindFirstValue(AuthClaims.SignonPersonUid), out var personUid) ? personUid : null;
    }

    /// <summary>
    /// The principal to sign in for a validated ID token of <paramref name="provider"/> (<see cref="AuthProviders"/>): the slim claims
    /// plus the issuer, <see cref="AuthClaims.Provider"/> and <see cref="AuthClaims.Mfa"/> when <c>amr</c> has <c>mfa</c>. The claims the
    /// app sets itself are dropped from the token first, so a provider cannot name the person, the provider or the second factor; and
    /// an Entra token's <c>role</c> claims are dropped, because Entra's roles are not read (ADR 0031). Null when the token has no
    /// subject, or no <c>personUid</c> for BCC Signon, so sign-in is refused. The person id is added afterwards (<see cref="AddPerson"/>).
    /// </summary>
    public static ClaimsPrincipal? FromIdToken(ClaimsPrincipal principal, string tokenIssuer, string provider)
    {
        var slim = Slim(principal);
        var identity = (ClaimsIdentity)slim.Identity!;
        if (!slim.HasClaim(c => c.Type == AuthClaims.Issuer)) identity.AddClaim(new Claim(AuthClaims.Issuer, tokenIssuer));
        string[] appOnly = provider == AuthProviders.Entra ? [AuthClaims.PersonId, AuthClaims.Provider, AuthClaims.Mfa, AuthClaims.Role] : [AuthClaims.PersonId, AuthClaims.Provider, AuthClaims.Mfa];
        foreach (var claim in identity.Claims.Where(c => appOnly.Contains(c.Type)).ToList())
            identity.RemoveClaim(claim);

        identity.AddClaim(new Claim(AuthClaims.Provider, provider));
        if (MfaStepUp.CompletedMfa(principal.FindAll(MfaStepUp.AmrClaim).Select(c => c.Value))) identity.AddClaim(new Claim(AuthClaims.Mfa, "true"));
        var complete = slim.HasClaim(c => c.Type == AuthClaims.Subject && c.Value.Length > 0)
            && (provider != AuthProviders.BccSignon || SignonPersonUid(principal) is not null);
        return complete ? slim : null;
    }

    /// <summary>Adds the person id and the roles stored for the person (a role the token already has is not added twice).</summary>
    public static void AddPerson(ClaimsPrincipal principal, Guid personId, IEnumerable<string> grantedRoles)
    {
        var identity = (ClaimsIdentity)principal.Identity!;
        identity.AddClaim(new Claim(AuthClaims.PersonId, personId.ToString()));
        foreach (var role in grantedRoles.Where(role => !identity.HasClaim(AuthClaims.Role, role)))
            identity.AddClaim(new Claim(AuthClaims.Role, role));
    }
}
