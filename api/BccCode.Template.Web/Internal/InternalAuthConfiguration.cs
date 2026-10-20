using BccCode.Template.Web.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace BccCode.Template.Web.Internal;

public static class InternalPolicies
{
    /// <summary>The Pub/Sub push subscription: <c>POST /internal/pubsub/{subscription}</c>.</summary>
    public const string PubSub = "InternalPubSub";

    /// <summary>Cloud Scheduler: <c>POST /internal/durability/pass</c>.</summary>
    public const string Scheduler = "InternalScheduler";
}

/// <summary>
/// Authentication for <c>/internal/**</c>, called by Pub/Sub push and Cloud Scheduler with Google-signed OIDC tokens. One
/// <see cref="GoogleOidcScheme"/> accepts both audiences; each policy requires its own audience and service account and uses
/// only that scheme, so a user's session cookie never satisfies it. The scheme is not a default scheme.
/// </summary>
/// <remarks>
/// Fails closed: in a command-line tool or while the OpenAPI document is written, both policies deny everything. Elsewhere the
/// four <c>Internal:*</c> settings are required and a missing one stops the host, naming the key. <c>Internal:AllowAll</c> (the
/// local emulator sends no Google tokens) opens both policies, and is refused outside Development and Test.
/// </remarks>
public static class InternalAuthConfiguration
{
    public const string GoogleOidcScheme = "GoogleOidc";
    public const string AllowAllKey = "Internal:AllowAll";
    public const string PubSubAudienceKey = "Internal:PubSubAudience";
    public const string PubSubServiceAccountKey = "Internal:PubSubServiceAccount";
    public const string SchedulerAudienceKey = "Internal:SchedulerAudience";
    public const string SchedulerServiceAccountKey = "Internal:SchedulerServiceAccount";

    private const string GoogleAuthority = "https://accounts.google.com";

    private sealed record Caller(string Policy, string AudienceKey, string ServiceAccountKey);

    private static readonly Caller[] Callers =
    [
        new(InternalPolicies.PubSub, PubSubAudienceKey, PubSubServiceAccountKey),
        new(InternalPolicies.Scheduler, SchedulerAudienceKey, SchedulerServiceAccountKey),
    ];

    public static void AddTemplateInternalAuth(this WebApplicationBuilder builder)
    {
        var allowAll = builder.Configuration.GetValue<bool>(AllowAllKey);
        // Only where the local emulator or the tests push without a token. Any other environment name (Production, a preview or
        // sandbox name) is refused, so a deployed service never runs with the internal endpoints open.
        if (allowAll && !builder.Environment.IsDevelopmentOrTest())
            throw new InvalidOperationException($"{AllowAllKey} is only allowed in Development and Test, not in {builder.Environment.EnvironmentName}.");

        var authentication = builder.Services.AddAuthentication();
        var authorization = builder.Services.AddAuthorizationBuilder();

        if (CodeGeneration.IsGeneratingOpenApi() || CodeGeneration.IsCommandLineTool())
        {
            // Nothing calls these endpoints here. The scheme rejects every token (no keys, no audiences), so an unauthenticated
            // call gets a 401 from it and not a redirect from the default sign-in challenge.
            authentication.AddJwtBearer(GoogleOidcScheme, options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters { ValidAudiences = [], ValidIssuers = [] };
            });
            foreach (var caller in Callers)
                authorization.AddPolicy(caller.Policy, policy => policy.AddAuthenticationSchemes(GoogleOidcScheme).RequireAssertion(_ => false));
            return;
        }

        if (allowAll)
        {
            foreach (var caller in Callers)
                authorization.AddPolicy(caller.Policy, policy => policy.RequireAssertion(_ => true));
            return;
        }

        var callers = Callers
            .Select(caller => (
                caller.Policy,
                Audience: Required(builder.Configuration, caller.AudienceKey),
                ServiceAccount: Required(builder.Configuration, caller.ServiceAccountKey)))
            .ToList();

        authentication.AddJwtBearer(GoogleOidcScheme, options =>
        {
            options.Authority = GoogleAuthority;
            options.MapInboundClaims = false;   // keep "email" and "aud" as the token names them
            options.TokenValidationParameters.ValidIssuers = [GoogleAuthority, "accounts.google.com"];
            options.TokenValidationParameters.ValidAudiences = callers.Select(caller => caller.Audience).Distinct().ToList();
        });

        foreach (var (policyName, audience, serviceAccount) in callers)
            authorization.AddPolicy(policyName, policy => policy
                .AddAuthenticationSchemes(GoogleOidcScheme)
                .RequireAuthenticatedUser()
                .RequireClaim("aud", audience)
                .RequireClaim("email", serviceAccount)
                .RequireClaim("email_verified", "true"));
    }

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] is { } value && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new InvalidOperationException($"{key} must be set (or set {AllowAllKey} in Development or Test).");
}
