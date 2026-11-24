using BccCode.Template.Api.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace BccCode.Template.Api.Auth;

/// <summary>
/// The browser-facing sign-in routes under <c>/auth</c>. Minimal endpoints, outside the Wolverine convention, and left out of the
/// OpenAPI document because the SPA reaches them by navigation. Each anonymous route is on the allowlist in
/// <see cref="AnonymousRouteGuard"/> and says here how it is protected instead.
/// </summary>
public static class AuthEndpoints
{
    // Plain HTML without styles or scripts, so the default no-document CSP fits it.
    private const string MFA_REQUIRED_PAGE = """
        <!doctype html>
        <html lang="en">
        <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Second factor required</title></head>
        <body>
        <main>
        <h1>Second factor required</h1>
        <p>You have the administrator role, so you have to sign in with a second factor. Set up two-factor authentication with your identity provider and try again.</p>
        <p><a href="/auth/login">Sign in again</a></p>
        </main>
        </body>
        </html>
        """;

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/auth").ExcludeFromDescription();

        // Anonymous by nature: it starts the sign-in. The return URL is checked as local, so it is no open redirect.
        auth.MapGet("/login", (string? returnUrl) => Results.Challenge(
                new AuthenticationProperties { RedirectUri = LocalRedirectGuard.SafeOrRoot(returnUrl) },
                [OpenIdConnectDefaults.AuthenticationScheme]))
            .AllowAnonymous();

        // Where a step-up without a second factor ends. No session exists yet, so the page is open; it shows fixed text only.
        auth.MapGet("/mfa-required", (HttpContext context) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return Results.Content(MFA_REQUIRED_PAGE, "text/html; charset=utf-8");
            })
            .AllowAnonymous();

        // A POST (standard 14.10), so the cross-origin check covers it: the SPA posts a plain form, because the answer is a redirect
        // through the identity provider, which fetch cannot follow. Needs a session (fallback policy), and signing out removes the
        // server-side session.
        auth.MapPost("/logout", () => Results.SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));

        // Anonymous because the identity provider calls it, not the browser: the signed logout token is the credential. It is a
        // server call, so it sends no Origin and passes the cross-origin check.
        auth.MapPost("/backchannel-logout", async (HttpContext context, BackchannelLogoutValidator validator, IAuthSessionStorage sessions) =>
            {
                context.Response.Headers.CacheControl = "no-cache, no-store";
                if (!context.Request.HasFormContentType) return Results.BadRequest(new { error = "invalid_request" });

                var form = await context.Request.ReadFormAsync(context.RequestAborted);
                var result = await validator.ValidateAsync(form["logout_token"].FirstOrDefault(), context.RequestAborted);
                if (result.Logout is not { } logout) return Results.BadRequest(new { error = "invalid_request" });

                if (logout.Sid is not null) await sessions.DeleteByIdentityProviderSessionAsync(logout.Issuer, logout.Sid, context.RequestAborted);
                else await sessions.DeleteBySubjectAsync(logout.Issuer, logout.Subject!, context.RequestAborted);
                return Results.Ok();
            })
            .AllowAnonymous();
    }
}
