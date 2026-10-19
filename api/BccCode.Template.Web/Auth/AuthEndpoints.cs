using BccCode.Template.Web.Http;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.RateLimiting;

namespace BccCode.Template.Web.Auth;

/// <summary>
/// The sign-in routes. They are minimal endpoints, outside the Wolverine convention, and left out of the OpenAPI document
/// because the SPA reaches them by navigation. Each anonymous route is on the allowlist in <see cref="AnonymousRouteGuard"/>
/// and says here how it is protected instead.
/// </summary>
public static class AuthEndpoints
{
    public const string XsrfCookieName = "XSRF-TOKEN";

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var user = app.MapGroup("/user").ExcludeFromDescription();

        // Anonymous by nature: it starts the sign-in. The return URL is checked as local, so it is no open redirect.
        user.MapGet("/login", (string? returnUrl) => Results.Challenge(
                new AuthenticationProperties { RedirectUri = LocalRedirectGuard.SafeOrRoot(returnUrl) },
                [OpenIdConnectDefaults.AuthenticationScheme]))
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Anonymous);

        // POST with the antiforgery token, so another site cannot sign the user out with an image tag or a form. The SPA
        // submits a real form (field __RequestVerificationToken) because the answer is a redirect through the identity provider.
        // Needs a session (fallback policy), and signing out removes the server-side session.
        user.MapPost("/logout", async (HttpContext context, IAntiforgery antiforgery) =>
            await AntiforgeryMiddleware.CheckAsync(context, antiforgery) ?? Results.SignOut(
                new AuthenticationProperties { RedirectUri = "/" },
                [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));

        // Anonymous so an unauthenticated SPA load gets 204 instead of a login redirect. A token is only issued for a signed-in user.
        user.MapGet("/antiforgery-token", (HttpContext context, IAntiforgery antiforgery) =>
            {
                if (context.User.Identity?.IsAuthenticated != true) return Results.NoContent();
                var tokens = antiforgery.GetAndStoreTokens(context);
                // Readable by JavaScript on purpose: the SPA echoes it in X-XSRF-TOKEN. It is not the cookie token.
                context.Response.Cookies.Append(XsrfCookieName, tokens.RequestToken!, new CookieOptions
                {
                    HttpOnly = false,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    Path = "/",
                });
                return Results.NoContent();
            })
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Anonymous);

        // Anonymous because the identity provider calls it, not the browser: the signed logout token is the credential.
        user.MapPost("/backchannel-logout", async (HttpContext context, BackchannelLogoutValidator validator, IAuthSessionStorage sessions) =>
            {
                context.Response.Headers.CacheControl = "no-cache, no-store";
                if (!context.Request.HasFormContentType) return Results.BadRequest(new { error = "invalid_request" });

                var form = await context.Request.ReadFormAsync(context.RequestAborted);
                var result = await validator.ValidateAsync(form["logout_token"].FirstOrDefault(), context.RequestAborted);
                if (result.Logout is not { } logout) return Results.BadRequest(new { error = "invalid_request" });

                await sessions.DeleteByIdentityProviderSessionAsync(logout.Issuer, logout.Sid, context.RequestAborted);
                return Results.Ok();
            })
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Anonymous);
    }
}
