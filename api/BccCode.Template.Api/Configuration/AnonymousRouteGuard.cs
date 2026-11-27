using Microsoft.AspNetCore.Authorization;

namespace BccCode.Template.Api.Configuration;

/// <summary>
/// Startup fails when an endpoint opts out of authorization without being on this list, so copying <c>[AllowAnonymous]</c>
/// cannot open a route by accident. Adding a route here is the deliberate act of accepting that anyone on the internet can
/// call it: it must protect itself another way (a signed token, a capability token, or no data at all) and say so where it is mapped.
/// </summary>
public static class AnonymousRouteGuard
{
    public static readonly IReadOnlyList<string> Allowed =
    [
        "/auth/login",               // starts the sign-in; the return URL is checked as local
        "/auth/login/{provider}",    // starts the sign-in of one provider; the return URL is checked as local, an unknown provider is 404
        "/auth/sign-in",             // fixed links to the providers, before any session exists; the return URL is checked as local
        "/auth/mfa-required",        // fixed text: where a step-up without a second factor ends, before any session exists
        "/auth/backchannel-logout",  // the signed logout token is the credential
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

    /// <param name="app">The application whose endpoints are checked.</param>
    /// <param name="alsoAllowed">Routes that exist only in some modes, such as the development OpenAPI document or the Vite proxy.</param>
    public static void AssertAnonymousRoutes(this WebApplication app, params string[] alsoAllowed)
    {
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints);
        var violations = FindViolations(endpoints, [.. Allowed, .. alsoAllowed]);
        if (violations.Count > 0)
            throw new InvalidOperationException($"These routes allow anonymous access but are not on the allowlist: {string.Join(", ", violations)}");
    }
}
