using BccCode.Platform;
using BccCode.Template.Api.Messaging;

namespace BccCode.Template.Api.Configuration;

/// <summary>
/// The <c>BccPlatform</c> settings: the machine-to-machine client in BCC Signon and the BCC PubSub base URL. They are read only when
/// <c>BccPubSub:Enabled</c> is true. Outside Development and Test a missing one stops the host and names the key (standard 14.5);
/// command-line tools (schema step, codegen, openapi) call nothing, so they need none.
/// </summary>
public sealed record PlatformSettings(
    string Authority, string Audience, string TokenEndpoint, string ClientId, string ClientSecret, string PubSubUrl, string PublicBaseUrl)
{
    public const string Section = "BccPlatform";
    public const string DefaultTokenEndpoint = "/oauth/token";

    // Where a setting may be missing (Development, Test, command-line tools) nothing may fall back to the SDK's production
    // URLs, so the client points at a host that cannot resolve.
    private const string UNCONFIGURED = "https://platform.invalid";

    public static PlatformSettings Read(IConfiguration configuration, IHostEnvironment environment)
    {
        var lenient = environment.IsDevelopmentOrTest() || CodeGeneration.IsJasperFxCommand() || CodeGeneration.IsRunningGeneration();

        string Get(string key, string fallback = "")
        {
            var value = configuration[$"{Section}:{key}"]?.Trim();
            if (!string.IsNullOrEmpty(value)) return value;
            return lenient ? fallback : throw new InvalidOperationException($"{Section}:{key} must be set outside Development and Test.");
        }

        return new PlatformSettings(
            Get("Authority"), Get("Audience"),
            // Optional: the SDK's own default is the same path.
            configuration[$"{Section}:TokenEndpoint"] is { Length: > 0 } tokenEndpoint ? tokenEndpoint : DefaultTokenEndpoint,
            Get("ClientId"), Get("ClientSecret"), Get("PubSubUrl", UNCONFIGURED), Get("PublicBaseUrl", UNCONFIGURED));
    }
}

/// <summary>Standard 14: the M2M client for BCC PubSub, and the scheme that validates BCC PubSub's deliver token. Does nothing while <c>BccPubSub:Enabled</c> is false.</summary>
public static class PlatformConfiguration
{
    public const string BccPubSubScheme = "BccPubSub";

    public static void AddTemplatePlatform(this WebApplicationBuilder builder)
    {
        if (!BccPubSubSettings.Enabled(builder.Configuration)) return;

        var settings = PlatformSettings.Read(builder.Configuration, builder.Environment);
        var oauth = new OAuthOptions
        {
            Authority = settings.Authority,
            Audience = settings.Audience,
            TokenEndpoint = settings.TokenEndpoint,
            ClientId = settings.ClientId,
            ClientSecret = settings.ClientSecret,
        };
        builder.Services.AddBccPubSub(settings.PubSubUrl, oauth);
        // The token BCC PubSub pushes with: BCC Signon, the same audience as our M2M client. The scope check is the package's policy.
        builder.Services.AddAuthentication().AddJwtBearer(BccPubSubScheme, o =>
        {
            o.Authority = settings.Authority;
            o.Audience = settings.Audience;
            // The OIDC mock in Development is served over http.
            o.RequireHttpsMetadata = !builder.Environment.IsDevelopmentOrTest();
            o.MapInboundClaims = false;
        });
    }
}
