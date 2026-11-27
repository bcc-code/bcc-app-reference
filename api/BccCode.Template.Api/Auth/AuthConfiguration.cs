using BccCode.Template.Api.Configuration;
using BccCode.Template.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace BccCode.Template.Api.Auth;

public static class TemplatePolicies
{
    /// <summary>A signed-in person: the cookie session with the person id claim. Also the default and the fallback policy.</summary>
    public const string User = "User";

    /// <summary>
    /// Everything under <c>/api/admin</c>. Needs the role named in <c>Auth:AdminRole</c>; with no role configured it denies everyone.
    /// The session must also come from a sign-in with a second factor (<see cref="AdminMfaMiddleware"/>).
    /// </summary>
    public const string Admin = "Admin";
}

/// <summary>
/// The BFF sign-in (standard 14): the browser never sees a token. OIDC authorization code flow with PKCE and PAR where the provider
/// offers it, for BCC Signon, Entra ID or both (ADR 0031), nothing saved from the token response, and a server-side session behind an
/// opaque <c>__Host-</c> cookie. There is no CORS policy on purpose: the SPA and the API share one origin (standard 16.13), so no
/// other origin may call the API with credentials.
/// </summary>
public static class AuthConfiguration
{
    public const string SessionCookieName = "__Host-template";

    /// <summary>
    /// 8 hours, sliding. With the session in the server-side store a longer window is acceptable, because a session can be
    /// revoked (back-channel logout, or deleting the row). Eight hours covers a working day without a second sign-in.
    /// </summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    public static bool IsApiRequest(HttpRequest request)
    {
        return request.Path.StartsWithSegments("/api");
    }

    /// <summary>A JasperFx command such as db-apply needs nothing from the identity provider, so it registers the policies only.</summary>
    public static void AddTemplateAuth(this WebApplicationBuilder builder)
    {
        ValidateProviders(builder.Configuration, builder.Environment);
        var providers = EnabledProviders.From(builder.Configuration, builder.Environment);

        builder.Services.AddSingleton(providers);
        builder.Services.AddSingleton<SessionTicketStore>();
        builder.Services.AddSingleton<IPostConfigureOptions<CookieAuthenticationOptions>, SessionStorePostConfigure>();
        builder.Services.AddSingleton<BackchannelLogoutValidator>();
        builder.Services.AddSingleton<SignInPeople>();

        var authentication = builder.Services
            .AddAuthentication(o =>
            {
                // Authenticate with the session cookie. A request without one goes straight to the only provider; with two (or
                // none) the cookie scheme is the challenge, which sends a page navigation to the sign-in page and answers /api with 401.
                o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                o.DefaultChallengeScheme = providers.Count == 1
                    ? EnabledProviders.SchemeOf(providers.Keys[0])!
                    : CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(o => ConfigureCookie(o, providers));
        if (!CodeGeneration.IsJasperFxCommand())
        {
            if (providers.Signon) authentication.AddOpenIdConnect(o => ConfigureSignon(o, builder.Configuration, builder.Environment));
            if (providers.Entra)
                authentication.AddOpenIdConnect(EnabledProviders.EntraScheme, o => ConfigureEntra(o, builder.Configuration, builder.Environment));
        }

        var adminRole = builder.Configuration[AdminMfaMiddleware.AdminRoleKey];
        var signedIn = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireClaim(AuthClaims.PersonId).Build();
        builder.Services.AddAuthorizationBuilder()
            // Default deny: anything without its own policy (minimal routes, the SPA fallback) needs a signed-in user.
            .AddPolicy(TemplatePolicies.User, signedIn)
            .SetDefaultPolicy(signedIn)
            .SetFallbackPolicy(signedIn)
            .AddPolicy(TemplatePolicies.Admin, p =>
            {
                // A missing setting must never open access.
                if (string.IsNullOrWhiteSpace(adminRole)) p.RequireAssertion(_ => false);
                else p.RequireAuthenticatedUser().RequireClaim(AuthClaims.PersonId).RequireRole(adminRole);
            });
    }

    /// <summary>
    /// Outside Development and Test a half configured provider (a setting missing), a missing client secret, no provider at all, an
    /// <c>Entra:Authority</c> override, or a malformed <c>Auth:BootstrapAdmins</c> stops the host, instead of leaving sign-in half configured or pointed elsewhere.
    /// </summary>
    public static void ValidateProviders(IConfiguration configuration, IHostEnvironment environment)
    {
        if (CodeGeneration.IsJasperFxCommand() || environment.IsDevelopmentOrTest()) return;
        var problems = EnabledProviders.Problems(configuration).ToList();
        if (BootstrapAdmins.Validate(configuration[BootstrapAdmins.Key]) is { } bootstrapProblem) problems.Add(bootstrapProblem);
        if (problems.Count > 0) throw new InvalidOperationException(string.Join(" ", problems));
    }

    /// <summary>
    /// The Entra authority: the tenant's v2.0 endpoint. <c>Entra:Authority</c> replaces it in Development and Test only, for the local
    /// mock; elsewhere the setting is refused at startup and ignored here.
    /// </summary>
    public static string EntraAuthority(IConfiguration configuration, IHostEnvironment environment)
    {
        var overridden = configuration[EnabledProviders.EntraAuthorityKey];
        return environment.IsDevelopmentOrTest() && EnabledProviders.IsSet(overridden)
            ? overridden!
            : $"https://login.microsoftonline.com/{configuration[EnabledProviders.EntraTenantKey]}/v2.0";
    }

    // /api and the SPA's fetch calls need a status they can read; a redirect to the identity provider is no use to fetch.
    private static bool WantsStatus(HttpRequest request)
    {
        return IsApiRequest(request) || request.Path.StartsWithSegments("/auth/logout");
    }

    private static void ConfigureCookie(CookieAuthenticationOptions o, EnabledProviders providers)
    {
        o.Cookie.Name = SessionCookieName;
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Lax;   // Lax so a link into the app still arrives signed in
        o.Cookie.Path = "/";
        o.ExpireTimeSpan = SessionLifetime;
        o.SlidingExpiration = true;
        // Signing in is the OIDC schemes' job. The cookie scheme is the default challenge only with two providers (or none), and then
        // all it does is send a page navigation to the sign-in page; everything else gets 401.
        o.Events.OnRedirectToLogin = context =>
        {
            if (!providers.Both || WantsStatus(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            var request = context.Request;
            var original = LocalRedirectGuard.SafeOrRoot($"{request.PathBase}{request.Path}{request.QueryString}");
            context.Response.Redirect($"{AuthEndpoints.SignInPath}?returnUrl={Uri.EscapeDataString(original)}");
            return Task.CompletedTask;
        };
        // A session written before the person id claim existed (ADR 0031) still authenticates, but the fallback policy would answer it 403
        // for ever: the SPA only signs in again on 401, and sliding expiry keeps the ticket alive. So it is rejected, and signing out
        // removes its row from the session store; the request is then challenged like one without a session. Claims only, no database read.
        o.Events.OnValidatePrincipal = async context =>
        {
            if (context.Principal?.TryPersonId(out _) == true) return;
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        };
        o.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }

    /// <summary>What both schemes share: code flow with PKCE, PAR where offered, nothing kept from the token response, no userinfo call.</summary>
    private static void ConfigureCommon(OpenIdConnectOptions o, IHostEnvironment environment)
    {
        o.RequireHttpsMetadata = !environment.IsDevelopmentOrTest();
        o.ResponseType = OpenIdConnectResponseType.Code;
        o.UsePkce = true;
        // PAR is a deliberate difference from bcc-ateam, which has none (ADR 0019): BCC Signon may not offer it, and Entra has no PAR
        // endpoint; then this falls back to a plain request.
        o.PushedAuthorizationBehavior = PushedAuthorizationBehavior.UseIfAvailable;
        o.SaveTokens = false;   // the backend calls no API on the user's behalf, so no token is kept anywhere
        o.GetClaimsFromUserInfoEndpoint = false;
        o.MapInboundClaims = false;
        o.Scope.Clear();   // only what the app reads; no offline_access, because there is no refresh token to use
        o.Scope.Add("openid");
        o.Scope.Add("profile");
        o.TokenValidationParameters.NameClaimType = AuthClaims.Name;
        o.TokenValidationParameters.RoleClaimType = AuthClaims.Role;
        // The default claim actions run after OnTokenValidated and delete iss, which the session store and back-channel logout need.
        o.ClaimActions.Remove(AuthClaims.Issuer);
    }

    private static void ConfigureEntra(OpenIdConnectOptions o, IConfiguration configuration, IHostEnvironment environment)
    {
        ConfigureCommon(o, environment);
        o.Authority = EntraAuthority(configuration, environment);
        o.ClientId = configuration[EnabledProviders.EntraClientIdKey];
        o.ClientSecret = configuration[EnabledProviders.EntraClientSecretKey];   // null locally: the mock registers the app as a public PKCE client
        o.CallbackPath = "/signin-entra";
        o.SignedOutCallbackPath = "/signout-callback-entra";

        o.Events.OnRedirectToIdentityProvider = context =>
        {
            // No second-factor scope: Entra does not know it, and a Conditional Access policy asks for the factor (ADR 0031).
            if (!WantsStatus(context.Request)) return Task.CompletedTask;
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.HandleResponse();
            return Task.CompletedTask;
        };

        // Standard end-session. No tokens are kept, so there is no id_token_hint; client_id identifies us.
        o.Events.OnRedirectToIdentityProviderForSignOut = context =>
        {
            context.ProtocolMessage.ClientId = context.Options.ClientId;
            return Task.CompletedTask;
        };

        o.Events.OnTokenValidated = ProviderSignIn.EntraTokenValidatedAsync;
    }

    private static void ConfigureSignon(OpenIdConnectOptions o, IConfiguration configuration, IHostEnvironment environment)
    {
        ConfigureCommon(o, environment);
        o.Authority = configuration[EnabledProviders.SignonAuthorityKey];
        o.ClientId = configuration[EnabledProviders.SignonClientIdKey];
        o.ClientSecret = configuration[EnabledProviders.SignonClientSecretKey];   // null locally: the mock registers the app as a public PKCE client
        o.CallbackPath = "/signin-oidc";
        o.SignedOutCallbackPath = "/signout-callback-oidc";

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

        o.Events.OnTokenValidated = ProviderSignIn.SignonTokenValidatedAsync;
    }
}
