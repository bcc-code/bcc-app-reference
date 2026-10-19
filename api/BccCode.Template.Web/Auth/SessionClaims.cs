using System.Security.Claims;

namespace BccCode.Template.Web.Auth;

/// <summary>Claim names the app reads. The OIDC handler runs with MapInboundClaims = false, so these are the raw JWT names.</summary>
public static class TemplateClaims
{
    public const string Subject = "sub";
    public const string Name = "name";
    public const string Role = "role";
    public const string SessionId = "sid";
    public const string Issuer = "iss";
}

public static class UserExtensions
{
    /// <summary>
    /// The OIDC subject. It owns the user's data and is never logged or returned to the browser. The default and fallback
    /// policies require it, so a signed-in request always has one.
    /// </summary>
    public static string Subject(this ClaimsPrincipal user) =>
        user.FindFirst(TemplateClaims.Subject)?.Value ?? throw new InvalidOperationException("The signed-in user has no sub claim.");

    public static string DisplayName(this ClaimsPrincipal user) => user.FindFirst(TemplateClaims.Name)?.Value ?? string.Empty;

    public static IReadOnlyList<string> Roles(this ClaimsPrincipal user) => [.. user.FindAll(TemplateClaims.Role).Select(c => c.Value)];
}

/// <summary>
/// Keeps only the claims the app reads before the session is written (standard 14, rule 3): a smaller ticket and less
/// personal data at rest. <c>sid</c> and <c>iss</c> stay because back-channel logout finds sessions with them.
/// </summary>
public static class SessionClaims
{
    private static readonly HashSet<string> Kept =
        [TemplateClaims.Subject, TemplateClaims.Name, TemplateClaims.Role, TemplateClaims.SessionId, TemplateClaims.Issuer];

    public static ClaimsPrincipal Slim(ClaimsPrincipal principal, string? issuer)
    {
        var claims = principal.Claims.Where(c => Kept.Contains(c.Type)).Select(c => new Claim(c.Type, c.Value)).ToList();
        if (issuer is not null && claims.All(c => c.Type != TemplateClaims.Issuer))
            claims.Add(new Claim(TemplateClaims.Issuer, issuer));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, principal.Identity?.AuthenticationType ?? "oidc", TemplateClaims.Name, TemplateClaims.Role));
    }
}
