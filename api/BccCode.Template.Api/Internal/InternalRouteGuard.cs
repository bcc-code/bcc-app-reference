using Microsoft.AspNetCore.Authorization;

namespace BccCode.Template.Api.Internal;

/// <summary>
/// Startup fails when a route under <c>/internal</c> does not carry the internal policy its path calls for, or allows
/// anonymous access. Without its own policy a route would get the user fallback policy, which no caller of <c>/internal</c>
/// can satisfy; a copied <c>[AllowAnonymous]</c> would open it to the internet.
/// </summary>
public static class InternalRouteGuard
{
    public static IReadOnlyList<string> FindViolations(IEnumerable<Endpoint> endpoints)
    {
        return endpoints.OfType<RouteEndpoint>()
            .Where(e => (e.RoutePattern.RawText ?? string.Empty).StartsWith(InternalEndpoints.Prefix + "/", StringComparison.OrdinalIgnoreCase))
            .Select(e => (Route: e.RoutePattern.RawText!, Endpoint: e))
            .Where(x => !HasExpectedPolicy(x.Route, x.Endpoint))
            .Select(x => x.Route)
            .ToList();
    }

    private static bool HasExpectedPolicy(string route, RouteEndpoint endpoint)
    {
        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null) return false;
        var expected = InternalEndpoints.ExpectedPolicy(route);
        return expected is not null && endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy == expected);
    }

    public static void AssertInternalRoutes(this WebApplication app)
    {
        var violations = FindViolations(((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints));
        if (violations.Count > 0)
            throw new InvalidOperationException($"These routes under {InternalEndpoints.Prefix} lack the internal policy their path requires: {string.Join(", ", violations)}");
    }
}
