using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace OidcMock;

public static class OidcEndpoints
{
    /// <summary>The BCC Signon claim the app keys its data by.</summary>
    public const string PersonUidClaim = "https://login.bcc.no/claims/personUid";

    public static void MapOidcEndpoints(this WebApplication app)
    {
        app.MapMethods("/connect/authorize", ["GET", "POST"], (Delegate)AuthorizeAsync);
        app.MapPost("/connect/token", (Delegate)ExchangeAsync);
        app.MapMethods("/connect/endsession", ["GET", "POST"], (Delegate)EndSessionAsync);
        app.MapGet("/login", LoginPage);
        // [FromForm] binding implies antiforgery metadata. The mock is a local-only test tool with nothing to protect, so it
        // runs without the antiforgery middleware instead of failing every persona login with a 500.
        app.MapPost("/login", LoginAsync).DisableAntiforgery();
        app.MapGet("/", () => Results.Redirect("/login"));
    }

    private static async Task<IResult> AuthorizeAsync(HttpContext context)
    {
        var request = context.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("No OpenIddict request.");
        var session = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!session.Succeeded)
        {
            var returnUrl = context.Request.PathBase + context.Request.Path + QueryString.Create(context.Request.Query.ToList());
            return Results.Challenge(new AuthenticationProperties { RedirectUri = returnUrl }, [CookieAuthenticationDefaults.AuthenticationScheme]);
        }

        var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, Claims.Name, Claims.Role);
        foreach (var claim in session.Principal!.Claims)
            identity.AddClaim(claim.SetDestinations(Destinations.AccessToken, Destinations.IdentityToken));
        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(request.GetScopes());
        return Results.SignIn(principal, properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    // The authorization code carries the principal; the token endpoint only turns it into tokens.
    private static async Task<IResult> ExchangeAsync(HttpContext context)
    {
        var request = context.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("No OpenIddict request.");
        if (!request.IsAuthorizationCodeGrantType())
            return Results.Forbid(properties: null, [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

        var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        return Results.SignIn(result.Principal!, properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    // Ends the mock session and returns to the client. OpenIddict has checked post_logout_redirect_uri against the client
    // and sends the request's state back with it, which the client's OIDC handler needs after /signout-callback-oidc.
    private static async Task<IResult> EndSessionAsync(HttpContext context)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    private static IResult LoginPage(string? returnUrl)
    {
        var buttons = string.Concat(Persona.All.Select(p =>
            $"""
             <form method="post" action="/login">
               <input type="hidden" name="returnUrl" value="{WebUtility.HtmlEncode(returnUrl)}">
               <button name="persona" value="{p.Key}">{WebUtility.HtmlEncode(p.Name)} <small>({p.Key})</small><br><small>{WebUtility.HtmlEncode(p.Description)}</small></button>
             </form>
             """));
        return Results.Content($$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><title>OIDC mock</title>
            <style>body{font-family:system-ui;max-width:28rem;margin:4rem auto}button{display:block;width:100%;margin:.5rem 0;padding:.75rem;font-size:1rem;text-align:left}</style></head>
            <body><h1>Sign in as</h1>{{buttons}}<p>Local development only. Not a real identity provider.</p></body></html>
            """, "text/html");
    }

    private static async Task<IResult> LoginAsync(HttpContext context, [FromForm] string persona, [FromForm] string? returnUrl)
    {
        if (Persona.Find(persona) is not { } person) return Results.BadRequest();

        var claims = new List<Claim>
        {
            new(Claims.Subject, person.PersonUid),
            new(Claims.Name, person.Name),
            new(PersonUidClaim, person.PersonUid),
            new("sid", Guid.NewGuid().ToString("N")),
            new(Claims.AuthenticationMethodReference, "pwd"),
        };
        if (person.Mfa) claims.Add(new Claim(Claims.AuthenticationMethodReference, "mfa"));
        claims.AddRange(person.Roles.Select(role => new Claim(Claims.Role, role)));
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));

        // Only the authorize request that sent the user here is a valid target, so the page is no open redirect.
        return Results.LocalRedirect(returnUrl?.StartsWith("/connect/authorize", StringComparison.Ordinal) == true ? returnUrl : "/");
    }
}
