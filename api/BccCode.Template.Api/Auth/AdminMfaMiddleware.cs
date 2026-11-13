using System.Security.Claims;
using BccCode.Template.Security;

namespace BccCode.Template.Api.Auth;

/// <summary>
/// An administrator's session must come from a sign-in with a second factor (standard 14, <see cref="MfaStepUp"/>). This also covers
/// a role granted mid-session: the next /api request answers 401, and the SPA's sign-in on 401 steps the person up.
/// </summary>
public static class AdminMfaMiddleware
{
    public const string AdminRoleKey = "Auth:AdminRole";

    public static readonly string ProblemType = Problems.TypeOf(Problems.Keys.MfaRequired);

    public static void UseTemplateAdminMfa(this WebApplication app)
    {
        var adminRole = app.Configuration[AdminRoleKey];
        app.Use(async (context, next) =>
        {
            if (RequiresMfa(context.Request, context.User, adminRole))
            {
                await Results.Problem(Problems.Create(StatusCodes.Status401Unauthorized, Problems.Keys.MfaRequired, "Sign in again with a second factor."))
                    .ExecuteAsync(context);
                return;
            }

            await next(context);
        });
    }

    /// <summary>Whether the role setting names a role the person holds. An empty setting names none: nobody is an administrator then.</summary>
    public static bool IsAdmin(ClaimsPrincipal user, string? adminRole)
    {
        return !string.IsNullOrWhiteSpace(adminRole) && user.HasClaim(AuthClaims.Role, adminRole);
    }

    public static bool RequiresMfa(HttpRequest request, ClaimsPrincipal user, string? adminRole)
    {
        if (!AuthConfiguration.IsApiRequest(request)) return false;
        if (user.Identity?.IsAuthenticated != true || user.HasClaim(AuthClaims.Mfa, "true")) return false;
        return IsAdmin(user, adminRole);
    }
}
