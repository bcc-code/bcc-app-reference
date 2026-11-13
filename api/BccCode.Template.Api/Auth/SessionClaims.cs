using System.Security.Claims;
using BccCode.Template.Security;

namespace BccCode.Template.Api.Auth;

/// <summary>
/// Keeps only the claims the app reads before the session is written (standard 14, rule 3): a smaller ticket and less
/// personal data at rest. <c>sub</c>, <c>sid</c> and <c>iss</c> stay because back-channel logout finds sessions with them.
/// </summary>
public static class SessionClaims
{
    private static readonly HashSet<string> Kept =
        [AuthClaims.Subject, AuthClaims.PersonUid, AuthClaims.Name, AuthClaims.Role, AuthClaims.SessionId, AuthClaims.Issuer, AuthClaims.Mfa];

    public static ClaimsPrincipal Slim(ClaimsPrincipal principal)
    {
        var claims = principal.Claims.Where(c => Kept.Contains(c.Type)).Select(c => new Claim(c.Type, c.Value));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, principal.Identity?.AuthenticationType ?? "oidc", AuthClaims.Name, AuthClaims.Role));
    }

    /// <summary>
    /// The principal to sign in for a validated ID token: the slim claims plus the issuer (the session store and back-channel
    /// logout find sessions by it) and <see cref="AuthClaims.Mfa"/> when <c>amr</c> has <c>mfa</c>. Null when the token has no
    /// person uid or subject, so sign-in is refused.
    /// </summary>
    public static ClaimsPrincipal? FromIdToken(ClaimsPrincipal principal, string tokenIssuer)
    {
        var slim = Slim(principal);
        var identity = (ClaimsIdentity)slim.Identity!;
        if (!slim.HasClaim(c => c.Type == AuthClaims.Issuer)) identity.AddClaim(new Claim(AuthClaims.Issuer, tokenIssuer));
        // Only the app sets template:mfa, from amr; a claim of that name from the identity provider is dropped.
        foreach (var claim in identity.FindAll(AuthClaims.Mfa).ToList())
            identity.RemoveClaim(claim);

        if (MfaStepUp.CompletedMfa(principal.FindAll(MfaStepUp.AmrClaim).Select(c => c.Value))) identity.AddClaim(new Claim(AuthClaims.Mfa, "true"));
        var complete = slim.HasClaim(c => c.Type == AuthClaims.PersonUid && c.Value.Length > 0)
            && slim.HasClaim(c => c.Type == AuthClaims.Subject && c.Value.Length > 0);
        return complete ? slim : null;
    }
}
