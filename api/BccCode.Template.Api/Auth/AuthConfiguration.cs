using BccCode.Template.Api.Configuration;
using BccCode.Template.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace BccCode.Template.Api.Auth;

public static class TemplatePolicies
{
    /// <summary>A signed-in person: the cookie session with the person uid claim. Also the default and the fallback policy.</summary>
    public const string User = "User";

    /// <summary>
    /// Everything under <c>/api/admin</c>. Needs the role named in <c>Auth:AdminRole</c>; with no role configured it denies everyone.
    /// The session must also come from a sign-in with a second factor (<see cref="AdminMfaMiddleware"/>).
    /// </summary>
    public const string Admin = "Admin";
}

/// <summary>
/// The BFF sign-in (standard 14): the browser never sees a token. OIDC authorization code flow with PKCE and PAR, nothing
/// saved from the token response, and a server-side session behind an opaque <c>__Host-</c> cookie. There is no CORS policy on
/// purpose: the SPA and the API share one origin (standard 16.13), so no other origin may call the API with credentials.
/// </summary>
public static class AuthConfiguration
{
    public const string SessionCookieName = "__Host-template";

    /// <summary>
    /// 8 hours, sliding. With the session in the server-side store a longer window is acceptable, because a session can be
    /// revoked (back-channel logout, or deleting the row). Eight hours covers a working day without a second sign-in.
    /// </summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    private static readonly string[] RequiredOidcSettings = ["Oidc:Authority", "Oidc:ClientId", "Oidc:ClientSecret"];

    public static bool IsApiRequest(HttpRequest request)
    {
        return request.Path.StartsWithSegments("/api");
    }

    /// <summary>A JasperFx command such as db-apply needs nothing from the identity provider, so it registers the policies only.</summary>
    public static void AddTemplateAuth(this WebApplicationBuilder builder)
    {
        ValidateOidc(builder.Configuration, builder.Environment);

        builder.Services.AddSingleton<SessionTicketStore>();
        builder.Services.AddSingleton<IPostConfigureOptions<CookieAuthenticationOptions>, SessionStorePostConfigure>();
        builder.Services.AddSingleton<BackchannelLogoutValidator>();

        var authentication = builder.Services
            .AddAuthentication(o =>
            {
                // Authenticate with the session cookie; a request without one is sent to the identity provider (or gets 401 under /api).
                o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(ConfigureCookie);
        if (!CodeGeneration.IsJasperFxCommand())
            authentication.AddOpenIdConnect(o => ConfigureOidc(o, builder.Configuration, builder.Environment));

        var adminRole = builder.Configuration[AdminMfaMiddleware.AdminRoleKey];
        var signedIn = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireClaim(AuthClaims.PersonUid).Build();
        builder.Services.AddAuthorizationBuilder()
            // Default deny: anything without its own policy (minimal routes, the SPA fallback) needs a signed-in user.
            .AddPolicy(TemplatePolicies.User, signedIn)
            .SetDefaultPolicy(signedIn)
            .SetFallbackPolicy(signedIn)
            .AddPolicy(TemplatePolicies.Admin, p =>
            {
                // A missing setting must never open access.
                if (string.IsNullOrWhiteSpace(adminRole)) p.RequireAssertion(_ => false);
                else p.RequireAuthenticatedUser().RequireClaim(AuthClaims.PersonUid).RequireRole(adminRole);
            });
    }

    /// <summary>Outside Development and Test a missing OIDC setting stops the host, instead of leaving sign-in half configured.</summary>
    public static void ValidateOidc(IConfiguration configuration, IHostEnvironment environment)
    {
        if (CodeGeneration.IsJasperFxCommand() || environment.IsDevelopmentOrTest()) return;
        var missing = RequiredOidcSettings.Where(key => string.IsNullOrWhiteSpace(configuration[key])).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException($"{string.Join(", ", missing)} must be set outside Development and Test.");
    }

    // /api and the SPA's fetch calls need a status they can read; a redirect to the identity provider is no use to fetch.
    private static bool WantsStatus(HttpRequest request)
    {
        return IsApiRequest(request) || request.Path.StartsWithSegments("/auth/logout");
    }

    private static void ConfigureCookie(CookieAuthenticationOptions o)
    {
        o.Cookie.Name = SessionCookieName;
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Lax;   // Lax so a link into the app still arrives signed in
        o.Cookie.Path = "/";
        o.ExpireTimeSpan = SessionLifetime;
        o.SlidingExpiration = true;
        // Signing in is the OIDC scheme's job, so the cookie scheme never redirects.
        o.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }

    private static void ConfigureOidc(OpenIdConnectOptions o, IConfiguration configuration, IHostEnvironment environment)
    {
        o.Authority = configuration["Oidc:Authority"];
        o.ClientId = configuration["Oidc:ClientId"];
        o.ClientSecret = configuration["Oidc:ClientSecret"];   // null locally: the mock registers the app as a public PKCE client
        o.RequireHttpsMetadata = !environment.IsDevelopmentOrTest();
        o.ResponseType = OpenIdConnectResponseType.Code;
        o.UsePkce = true;
        // PAR is a deliberate difference from bcc-ateam, which has none (ADR 0019): BCC Signon may not offer it, and then this falls back to a plain request.
        o.PushedAuthorizationBehavior = PushedAuthorizationBehavior.UseIfAvailable;
        o.SaveTokens = false;   // the backend calls no API on the user's behalf, so no token is kept anywhere
        o.GetClaimsFromUserInfoEndpoint = false;
        o.MapInboundClaims = false;
        o.Scope.Clear();   // only what the app reads; no offline_access, because there is no refresh token to use
        o.Scope.Add("openid");
        o.Scope.Add("profile");
        o.CallbackPath = "/signin-oidc";
        o.SignedOutCallbackPath = "/signout-callback-oidc";
        o.TokenValidationParameters.NameClaimType = AuthClaims.Name;
        o.TokenValidationParameters.RoleClaimType = AuthClaims.Role;
        // The default claim actions run after OnTokenValidated and delete iss, which the session store and back-channel logout need.
        o.ClaimActions.Remove(AuthClaims.Issuer);

        o.Events.OnRedirectToIdentityProvider = context =>
        {
            if (WantsStatus(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.HandleResponse();
                return Task.CompletedTask;
            }

            MfaStepUp.AddScope(context);
            return Task.CompletedTask;
        };

        o.Events.OnRemoteFailure = context =>
        {
            // Cancelling the second factor at the identity provider ends on the refusal page instead of an error.
            if (!MfaStepUp.IsStepUpLeg(context.Properties)) return Task.CompletedTask;
            context.HandleResponse();
            context.Response.Redirect(MfaStepUp.RequiredPath);
            return Task.CompletedTask;
        };

        var useAuth0Logout = configuration.GetValue<bool>("Oidc:UseAuth0Logout");
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

        o.Events.OnTokenValidated = async context =>
        {
            if (SessionClaims.FromIdToken(context.Principal!, context.SecurityToken.Issuer) is not { } principal)
            {
                context.Fail($"The ID token has no {AuthClaims.PersonUid} or {AuthClaims.Subject} claim.");
                return;
            }

            context.Principal = principal;
            await MfaStepUp.OnTokenValidatedAsync(context);
        };
    }
}
