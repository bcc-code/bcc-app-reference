using BccCode.Template.Web.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace BccCode.Template.Web.Auth;

public static class TemplatePolicies
{
    /// <summary>Everything under <c>/api/admin</c>. Needs the role named in <c>Auth:AdminRole</c>; with no role configured it denies everyone.</summary>
    public const string Admin = "Admin";
}

/// <summary>
/// The BFF sign-in (standard 14): the browser never sees a token. OIDC authorization code flow with PKCE and PAR, nothing
/// saved from the token response, and a server-side session behind an opaque <c>__Host-</c> cookie.
/// </summary>
public static class AuthConfiguration
{
    public const string SessionCookieName = "__Host-template";
    public const string AntiforgeryCookieName = "__Host-template-af";
    public const string AntiforgeryHeaderName = "X-XSRF-TOKEN";

    // Sliding. With the session in the server-side store a longer window is acceptable, because a session can be revoked
    // (back-channel logout, or deleting the row). Eight hours covers a working day without a second sign-in.
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    private static readonly string[] RequiredOidcSettings = ["OIDC:Authority", "OIDC:ClientId", "OIDC:ClientSecret"];

    public static bool IsApiRequest(HttpRequest request) => request.Path.StartsWithSegments("/api");

    public static void AddTemplateAuth(this WebApplicationBuilder builder)
    {
        ValidateOidc(builder.Configuration, builder.Environment);

        builder.Services.AddSingleton<SessionTicketStore>();
        builder.Services.AddSingleton<IPostConfigureOptions<CookieAuthenticationOptions>, SessionStorePostConfigure>();
        builder.Services.AddSingleton<BackchannelLogoutValidator>();

        builder.Services
            .AddAuthentication(o =>
            {
                o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                // The cookie handler challenges: 401 for /api, a redirect to /user/login for pages. /user/login challenges OIDC itself.
                o.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(ConfigureCookie)
            .AddOpenIdConnect(o => ConfigureOidc(o, builder.Configuration, builder.Environment));

        var adminRole = builder.Configuration["Auth:AdminRole"];
        var signedIn = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireClaim(TemplateClaims.Subject).Build();
        builder.Services.AddAuthorizationBuilder()
            // Default deny: anything without its own policy (minimal routes, the SPA fallback) needs a signed-in user.
            .SetDefaultPolicy(signedIn)
            .SetFallbackPolicy(signedIn)
            .AddPolicy(TemplatePolicies.Admin, p =>
            {
                // A missing setting must never open access.
                if (string.IsNullOrWhiteSpace(adminRole)) p.RequireAssertion(_ => false);
                else p.RequireAuthenticatedUser().RequireClaim(TemplateClaims.Subject).RequireRole(adminRole);
            });

        builder.Services.AddAntiforgery(o =>
        {
            o.HeaderName = AntiforgeryHeaderName;
            o.Cookie.Name = AntiforgeryCookieName;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });
    }

    /// <summary>Outside Development and Test a missing OIDC setting stops the host, instead of leaving sign-in half configured.</summary>
    public static void ValidateOidc(IConfiguration configuration, IHostEnvironment environment)
    {
        if (CodeGeneration.IsGeneratingOpenApi() || environment.IsDevelopmentOrTest()) return;
        var missing = RequiredOidcSettings.Where(key => string.IsNullOrWhiteSpace(configuration[key])).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException($"{string.Join(", ", missing)} must be set outside Development and Test.");
    }

    private static void ConfigureCookie(CookieAuthenticationOptions o)
    {
        o.Cookie.Name = SessionCookieName;
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Lax;   // Lax so a link into the app still arrives signed in
        o.Cookie.Path = "/";
        o.LoginPath = "/user/login";
        o.ReturnUrlParameter = "returnUrl";
        o.ExpireTimeSpan = SessionLifetime;
        o.SlidingExpiration = true;
        // /api callers get a status, never a redirect to the login page: fetch cannot follow it and the real status is lost.
        o.Events.OnRedirectToLogin = c => ApiAware(c, StatusCodes.Status401Unauthorized);
        o.Events.OnRedirectToAccessDenied = c => ApiAware(c, StatusCodes.Status403Forbidden);
    }

    private static Task ApiAware(RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        if (IsApiRequest(context.Request)) context.Response.StatusCode = status;
        else context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }

    private static void ConfigureOidc(OpenIdConnectOptions o, IConfiguration configuration, IHostEnvironment environment)
    {
        o.Authority = configuration["OIDC:Authority"];
        o.ClientId = configuration["OIDC:ClientId"];
        o.ClientSecret = configuration["OIDC:ClientSecret"];   // null locally: the mock registers the app as a public PKCE client
        o.RequireHttpsMetadata = !environment.IsDevelopmentOrTest();
        o.ResponseType = OpenIdConnectResponseType.Code;
        o.UsePkce = true;
        o.PushedAuthorizationBehavior = PushedAuthorizationBehavior.UseIfAvailable;
        o.SaveTokens = false;   // the backend calls no API on the user's behalf, so no token is kept anywhere
        o.GetClaimsFromUserInfoEndpoint = false;
        o.MapInboundClaims = false;
        o.Scope.Clear();   // only what the app reads; no offline_access, because there is no refresh token to use
        o.Scope.Add("openid");
        o.Scope.Add("profile");
        o.CallbackPath = "/signin-oidc";
        o.SignedOutCallbackPath = "/signout-callback-oidc";
        o.TokenValidationParameters.NameClaimType = TemplateClaims.Name;
        o.TokenValidationParameters.RoleClaimType = TemplateClaims.Role;

        o.Events.OnTokenValidated = context =>
        {
            context.Principal = SessionClaims.Slim(context.Principal!, context.SecurityToken.Issuer);
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToIdentityProvider = context =>
        {
            if (IsApiRequest(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.HandleResponse();
            }

            return Task.CompletedTask;
        };

        var useAuth0Logout = configuration.GetValue<bool>("OIDC:UseAuth0Logout");
        o.Events.OnRedirectToIdentityProviderForSignOut = context =>
        {
            if (!useAuth0Logout)
            {
                // Standard end-session (the local mock). No tokens are kept, so there is no id_token_hint; client_id identifies us.
                context.ProtocolMessage.ClientId = context.Options.ClientId;
                return Task.CompletedTask;
            }

            // BCC Signon's documented logout endpoint. returnTo must be one of the client's allowed logout URLs.
            var request = context.Request;
            var returnTo = $"{request.Scheme}://{request.Host}{request.PathBase}{LocalRedirectGuard.SafeOrRoot(context.Properties.RedirectUri)}";
            context.Response.Redirect(QueryHelpers.AddQueryString($"{context.Options.Authority!.TrimEnd('/')}/v2/logout",
                new Dictionary<string, string?> { ["client_id"] = context.Options.ClientId, ["returnTo"] = returnTo }));
            context.HandleResponse();
            return Task.CompletedTask;
        };
    }
}
