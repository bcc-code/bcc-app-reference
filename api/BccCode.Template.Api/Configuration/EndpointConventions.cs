using BccCode.Template.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BccCode.Template.Api.Configuration;

/// <summary>Rules every Wolverine endpoint gets by convention, so a new endpoint is safe without anyone remembering an attribute.</summary>
public static class EndpointConventions
{
    private const string ADMIN_PREFIX = "/api/admin";

    /// <summary>Call from <c>MapWolverineEndpoints</c>: OpenAPI names and default-deny authorization.</summary>
    public static void UseTemplateConventions(this WolverineHttpOptions options)
    {
        options.ConfigureEndpoints(chain =>
        {
            OpenApiNaming.Apply(chain);
            ApplyAuthorization(chain);
        });
    }

    /// <summary>
    /// The policy follows from where the endpoint lives: <c>/api/admin/**</c> is admin-only, everything else needs a signed-in
    /// user. An endpoint with its own <c>[Authorize]</c> keeps it; <c>[AllowAnonymous]</c> is checked by <see cref="AnonymousRouteGuard"/>.
    /// </summary>
    public static string ExpectedPolicy(string route)
    {
        return route.Equals(ADMIN_PREFIX, StringComparison.OrdinalIgnoreCase) || route.StartsWith(ADMIN_PREFIX + "/", StringComparison.OrdinalIgnoreCase)
            ? TemplatePolicies.Admin
            : string.Empty;
    }

    private static void ApplyAuthorization(HttpChain chain)
    {
        var declared = chain.Method.Method.GetCustomAttributes(true)
            .Concat(chain.Method.HandlerType.GetCustomAttributes(true))
            .Any(a => a is IAuthorizeData or IAllowAnonymous);
        if (declared) return;

        var policy = ExpectedPolicy(chain.RoutePattern!.RawText ?? string.Empty);
        if (policy.Length == 0) chain.RequireAuthorization();
        else chain.RequireAuthorization(policy);
    }
}
