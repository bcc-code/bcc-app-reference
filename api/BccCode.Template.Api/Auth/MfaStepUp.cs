using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace BccCode.Template.Api.Auth;

/// <summary>
/// An administrator signs in with a second factor (standard 14). The first leg signs in as usual; for an administrator whose token
/// has no <c>mfa</c> in <c>amr</c> no session is written and the browser is sent back to the identity provider with the
/// <see cref="Scope"/> scope, which makes it ask for the second factor. The same pattern as bcc-ateam, with the admin role in the
/// place of its staff roles.
/// </summary>
public static class MfaStepUp
{
    /// <summary>Kept in the protected OIDC state, so the browser cannot set it.</summary>
    public const string Marker = "mfa_stepup";

    // why: follows bcc-ateam. BCC Signon's support for acr_values is not verified, so the second factor is asked for with a scope
    // (docs/tech-debt/mfa-step-up-uses-a-scope-not-acr-values.md).
    public const string Scope = "mfa";

    public const string AmrClaim = "amr";

    public const string RequiredPath = "/auth/mfa-required";

    public static bool ShouldStepUp(bool isAdmin, bool alreadySteppedUp)
    {
        return isAdmin && !alreadySteppedUp;
    }

    public static bool CompletedMfa(IEnumerable<string> amr)
    {
        return amr.Contains(Scope, StringComparer.Ordinal);
    }

    public static bool IsStepUpLeg(AuthenticationProperties? properties)
    {
        return properties?.Items.TryGetValue(Marker, out var value) == true && value == "1";
    }

    /// <summary>Adds the <c>mfa</c> scope to the authorize request on the step-up leg only.</summary>
    public static void AddScope(RedirectContext context)
    {
        if (!IsStepUpLeg(context.Properties)) return;
        var scopes = (context.ProtocolMessage.Scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!scopes.Contains(Scope)) scopes.Add(Scope);
        context.ProtocolMessage.Scope = string.Join(' ', scopes);
    }

    /// <summary>
    /// Runs after <see cref="SessionClaims.FromIdToken"/> has set the principal. Either lets the sign-in go on, challenges an
    /// administrator again with the marker, or refuses a step-up token without <c>mfa</c> in <c>amr</c>.
    /// </summary>
    public static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var amr = context.SecurityToken.Claims.Where(c => c.Type == AmrClaim).Select(c => c.Value).ToList();
        if (IsStepUpLeg(context.Properties))
        {
            if (CompletedMfa(amr))
            {
                // The session ticket does not need the marker.
                context.Properties!.Items.Remove(Marker);
                return;
            }

            // No session and no cookie: the person has to set up a second factor first.
            context.HandleResponse();
            context.Response.Redirect(RequiredPath);
            return;
        }

        var adminRole = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()[AdminMfaMiddleware.AdminRoleKey];
        // A token that already has mfa (the identity provider session had a second factor) needs no second leg.
        if (!ShouldStepUp(AdminMfaMiddleware.IsAdmin(context.Principal!, adminRole), alreadySteppedUp: CompletedMfa(amr))) return;

        var properties = new AuthenticationProperties { RedirectUri = context.Properties?.RedirectUri ?? "/" };
        properties.Items[Marker] = "1";
        context.HandleResponse();
        await context.HttpContext.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, properties);
    }
}
