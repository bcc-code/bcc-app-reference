using System.Security.Claims;
using System.Text.Encodings.Web;
using BccCode.Template.Api.Configuration;
using BccCode.Template.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace BccCode.Template.Api.Auth;

/// <summary>
/// The browser-facing sign-in routes under <c>/auth</c>. Minimal endpoints, outside the Wolverine convention, and left out of the
/// OpenAPI document because the SPA reaches them by navigation. Each anonymous route is on the allowlist in
/// <see cref="AnonymousRouteGuard"/> and says here how it is protected instead.
/// </summary>
public static class AuthEndpoints
{
    public const string SignInPath = "/auth/sign-in";

    private static readonly Dictionary<string, string> ProviderLabels = new()
    {
        [AuthProviders.BccSignon] = "Sign in with BCC Signon",
        [AuthProviders.Entra] = "Sign in with Microsoft",
    };

    // Plain HTML without styles or scripts, so the default no-document CSP fits it. One paragraph per link, so the links have the room a touch target needs.
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

        // Anonymous by nature: it starts the sign-in. The return URL is checked as local, so it is no open redirect. With one provider
        // it challenges that provider, with two the person picks one on the sign-in page.
        auth.MapGet("/login", IResult (string? returnUrl, EnabledProviders providers) =>
            {
                var safe = LocalRedirectGuard.SafeOrRoot(returnUrl);
                if (providers.Count == 0) return Results.NotFound();
                return providers.Both ? Results.Redirect(SignInUrl(safe)) : Challenge(providers.Keys[0], safe);
            })
            .AllowAnonymous();

        // Anonymous because it starts the sign-in of one provider. Unknown and switched-off providers are 404, and the return URL is
        // checked as local.
        auth.MapGet("/login/{provider}", IResult (string provider, string? returnUrl, EnabledProviders providers) =>
                providers.Has(provider) ? Challenge(provider, LocalRedirectGuard.SafeOrRoot(returnUrl)) : Results.NotFound())
            .AllowAnonymous();

        // Where a navigation without a session ends when two providers are on. Fixed text and links; the return URL is the checked one.
        auth.MapGet("/sign-in", IResult (HttpContext context, string? returnUrl, EnabledProviders providers) =>
            {
                var safe = LocalRedirectGuard.SafeOrRoot(returnUrl);
                if (providers.Count == 0) return Results.NotFound();
                if (!providers.Both) return Results.Redirect($"/auth/login?returnUrl={Uri.EscapeDataString(safe)}");

                context.Response.Headers.CacheControl = "no-store";
                return Results.Content(SignInPage(providers, safe), "text/html; charset=utf-8");
            })
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
        // server-side session. It signs out of the provider the session came from.
        auth.MapPost("/logout", (ClaimsPrincipal user, EnabledProviders providers) =>
        {
            var provider = user.FindFirstValue(AuthClaims.Provider) ?? AuthProviders.BccSignon;
            List<string> schemes = [CookieAuthenticationDefaults.AuthenticationScheme];
            if (providers.Has(provider)) schemes.Add(EnabledProviders.SchemeOf(provider)!);
            return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, schemes);
        });

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

    private static string SignInUrl(string returnUrl)
    {
        return $"{SignInPath}?returnUrl={Uri.EscapeDataString(returnUrl)}";
    }

    private static IResult Challenge(string provider, string returnUrl)
    {
        return Results.Challenge(new AuthenticationProperties { RedirectUri = returnUrl }, [EnabledProviders.SchemeOf(provider)!]);
    }

    // Plain HTML without styles or scripts, so the default no-document CSP fits it. One paragraph per link, so the links have the room a touch target needs. Every value is encoded for the attribute it is in.
    private static string SignInPage(EnabledProviders providers, string returnUrl)
    {
        var html = HtmlEncoder.Default;
        var links = new List<string>();
        foreach (var provider in providers.Keys)
        {
            var href = html.Encode($"/auth/login/{provider}?returnUrl={Uri.EscapeDataString(returnUrl)}");
            links.Add($"<p><a href=\"{href}\">{html.Encode(ProviderLabels[provider])}</a></p>");
        }

        return $$"""
            <!doctype html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Sign in</title></head>
            <body>
            <main>
            <h1>Sign in</h1>
            {{string.Concat(links)}}
            </main>
            </body>
            </html>
            """;
    }
}
