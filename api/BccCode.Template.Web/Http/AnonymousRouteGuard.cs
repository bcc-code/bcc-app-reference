using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BccCode.Template.Web.Http;

/// <summary>
/// Startup fails when an endpoint opts out of authorization without being on this list, so copying <c>[AllowAnonymous]</c>
/// cannot open a route by accident. Adding a route here is the deliberate act of accepting that anyone on the internet can
/// call it: it must protect itself another way (a signed token, a capability token, or no data at all) and say so where it is mapped.
/// </summary>
public static class AnonymousRouteGuard
{
    public static readonly IReadOnlyList<string> Allowed =
    [
        "/user/login",               // starts the sign-in; the return URL is checked as local
        "/user/antiforgery-token",   // issues a token only to a signed-in user
        "/user/backchannel-logout",  // the signed logout token is the credential
        "/health",                   // probes: no personal data
        "/alive",
    ];

    public static IReadOnlyList<string> FindViolations(IEnumerable<Endpoint> endpoints, IEnumerable<string> allowed)
    {
        var allowlist = allowed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => e.RoutePattern.RawText ?? e.DisplayName ?? "(unnamed)")
            .Where(route => !allowlist.Contains(route))
            .ToList();
    }

    /// <param name="alsoAllowed">Routes that exist only in some modes, such as the development OpenAPI document or the Vite proxy.</param>
    public static void AssertAnonymousRoutes(this WebApplication app, params string[] alsoAllowed)
    {
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints);
        var violations = FindViolations(endpoints, [.. Allowed, .. alsoAllowed]);
        if (violations.Count > 0)
            throw new InvalidOperationException($"These routes allow anonymous access but are not on the allowlist: {string.Join(", ", violations)}");
    }
}
