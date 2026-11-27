using System.Security.Claims;
using BccCode.Template.Security;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace BccCode.Template.Api.Auth;

/// <summary>
/// What each scheme does once its ID token is validated and before the session is written (ADR 0031): build the slim principal, record
/// the sign-in (the person id and the stored roles), and decide on the second factor.
/// </summary>
public static class ProviderSignIn
{
    public static async Task SignonTokenValidatedAsync(TokenValidatedContext context)
    {
        var token = context.Principal!;
        if (SessionClaims.FromIdToken(token, context.SecurityToken.Issuer, AuthProviders.BccSignon) is not { } principal
            || SessionClaims.SignonPersonUid(token) is not { } personUid)
        {
            context.Fail($"The ID token has no {AuthClaims.SignonPersonUid} or {AuthClaims.Subject} claim.");
            return;
        }

        var signedIn = await RecordAsync(context, principal, AuthProviders.BccSignon, personUid, bootstrapId: personUid.ToString());
        SessionClaims.AddPerson(principal, signedIn.PersonId, signedIn.Roles);
        context.Principal = principal;
        // After the stored roles are in the principal, so a person granted the admin role is stepped up like one the token names.
        await MfaStepUp.OnTokenValidatedAsync(context);
    }

    public static async Task EntraTokenValidatedAsync(TokenValidatedContext context)
    {
        var token = context.SecurityToken;
        var tenant = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()[EnabledProviders.EntraTenantKey];
        if (!TenantMatches(token.Claims.FirstOrDefault(c => c.Type == AuthClaims.TenantId)?.Value, tenant))
        {
            context.Fail("The ID token is not from the configured Entra tenant.");
            return;
        }

        if (SessionClaims.FromIdToken(context.Principal!, token.Issuer, AuthProviders.Entra) is not { } principal)
        {
            context.Fail($"The ID token has no {AuthClaims.Subject} claim.");
            return;
        }

        var objectId = token.Claims.FirstOrDefault(c => c.Type == AuthClaims.ObjectId)?.Value;
        var signedIn = await RecordAsync(context, principal, AuthProviders.Entra, signonPersonUid: null, bootstrapId: objectId);
        SessionClaims.AddPerson(principal, signedIn.PersonId, signedIn.Roles);

        var adminRole = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()[AdminMfaMiddleware.AdminRoleKey];
        if (RefusesAdminWithoutMfa(principal, adminRole))
        {
            // Entra has no step-up (ADR 0031): no session, and the person is told to set up a second factor.
            context.HandleResponse();
            context.Response.Redirect(MfaStepUp.RequiredPath);
            return;
        }

        context.Principal = principal;
    }

    /// <summary>An administrator whose Entra sign-in had no second factor in <c>amr</c> gets no session.</summary>
    public static bool RefusesAdminWithoutMfa(ClaimsPrincipal principal, string? adminRole)
    {
        return MfaStepUp.ShouldStepUp(AdminMfaMiddleware.IsAdmin(principal, adminRole), alreadySteppedUp: principal.HasClaim(AuthClaims.Mfa, "true"));
    }

    /// <summary>The ID token's <c>tid</c> must be the one tenant the registration belongs to.</summary>
    public static bool TenantMatches(string? tokenTenant, string? configuredTenant)
    {
        return EnabledProviders.IsSet(tokenTenant) && string.Equals(tokenTenant, configuredTenant, StringComparison.OrdinalIgnoreCase);
    }

    private static Task<SignedInPerson> RecordAsync(TokenValidatedContext context, ClaimsPrincipal principal, string provider, Guid? signonPersonUid, string? bootstrapId)
    {
        var request = new SignInRequest(
            provider,
            principal.FindFirstValue(AuthClaims.Issuer)!,
            principal.FindFirstValue(AuthClaims.Subject)!,
            principal.FindFirstValue(AuthClaims.Name),
            signonPersonUid,
            bootstrapId);
        return context.HttpContext.RequestServices.GetRequiredService<SignInPeople>().RecordAsync(request, context.HttpContext.RequestAborted);
    }
}
