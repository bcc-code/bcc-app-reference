using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using BccCode.Template.Api.Internal;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace BccCode.Template.Tests;

/// <summary>
/// The authentication of <c>/internal/**</c> (standard 13 rule 12, standard 15): a probe route per internal policy on a real
/// Kestrel host whose <c>GoogleOidc</c> scheme trusts a local key instead of Google's. The default scheme is a signed-in user,
/// so the tests also show that a user session never satisfies an internal policy.
/// </summary>
public sealed class InternalAuthFixture : IAsyncLifetime
{
    public const string PubSubAudience = "https://template.test/internal/pubsub";
    public const string PubSubServiceAccount = "pubsub-push@test.iam.gserviceaccount.com";
    public const string SchedulerAudience = "https://template.test/internal/scheduler";
    public const string SchedulerServiceAccount = "scheduler@test.iam.gserviceaccount.com";
    public const string GoogleIssuer = "https://accounts.google.com";

    public static readonly Dictionary<string, string?> Settings = new()
    {
        [InternalAuthConfiguration.PubSubAudienceKey] = PubSubAudience,
        [InternalAuthConfiguration.PubSubServiceAccountKey] = PubSubServiceAccount,
        [InternalAuthConfiguration.SchedulerAudienceKey] = SchedulerAudience,
        [InternalAuthConfiguration.SchedulerServiceAccountKey] = SchedulerServiceAccount,
    };

    private readonly RSA _googleKey = RSA.Create(2048);
    private readonly RSA _otherKey = RSA.Create(2048);
    private WebApplication _app = null!;

    public HttpClient Client { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Test" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(Settings);

        // A signed-in user is the default, as the cookie session is in the app.
        builder.Services.AddAuthentication("User").AddScheme<AuthenticationSchemeOptions, SignedInUserHandler>("User", _ => { });
        builder.AddTemplateInternalAuth();

        var google = new OpenIdConnectConfiguration { Issuer = GoogleIssuer };
        google.SigningKeys.Add(new RsaSecurityKey(_googleKey) { KeyId = "google-test" });
        builder.Services.PostConfigure<JwtBearerOptions>(InternalAuthConfiguration.GoogleOidcScheme, options =>
        {
            options.Configuration = google;
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(google);
        });

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapGet("/probe/pubsub", () => Results.Ok()).RequireAuthorization(InternalPolicies.PubSub);
        _app.MapGet("/probe/scheduler", () => Results.Ok()).RequireAuthorization(InternalPolicies.Scheduler);
        await _app.StartAsync();
        Client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
        _googleKey.Dispose();
        _otherKey.Dispose();
    }

    public string GoogleToken(string audience, string email, bool emailVerified = true)
    {
        return Token(_googleKey, "google-test", GoogleIssuer, audience, email, emailVerified);
    }

    /// <summary>A well-formed token from a key and issuer the GoogleOidc scheme does not trust.</summary>
    public string OtherToken(string audience, string email)
    {
        return Token(_otherKey, "other", "https://login.example/", audience, email, true);
    }

    private static string Token(RSA key, string keyId, string issuer, string audience, string email, bool emailVerified)
    {
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["email"] = email, ["email_verified"] = emailVerified },
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(key) { KeyId = keyId }, SecurityAlgorithms.RsaSha256),
        });
    }

    public async Task<HttpStatusCode> GetAsync(string path, string? bearer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var response = await Client.SendAsync(request);
        return response.StatusCode;
    }

    private sealed class SignedInUserHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim("sub", "a-user"), new Claim("role", "admin")], "User");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "User")));
        }
    }
}

public class InternalAuthTests(InternalAuthFixture fixture) : IClassFixture<InternalAuthFixture>
{
    private const string PUBSUB = "/probe/pubsub";
    private const string SCHEDULER = "/probe/scheduler";

    [Theory]
    [InlineData(PUBSUB, InternalAuthFixture.PubSubAudience, InternalAuthFixture.PubSubServiceAccount)]
    [InlineData(SCHEDULER, InternalAuthFixture.SchedulerAudience, InternalAuthFixture.SchedulerServiceAccount)]
    public async Task The_configured_caller_is_let_in(string path, string audience, string email)
    {
        (await fixture.GetAsync(path, fixture.GoogleToken(audience, email))).Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(PUBSUB)]
    [InlineData(SCHEDULER)]
    public async Task No_token_is_401_even_with_a_signed_in_user_as_the_default(string path)
    {
        (await fixture.GetAsync(path)).Should().Be(HttpStatusCode.Unauthorized, "a user session never satisfies an internal policy");
    }

    [Theory]
    [InlineData(PUBSUB, InternalAuthFixture.PubSubAudience, InternalAuthFixture.PubSubServiceAccount)]
    [InlineData(SCHEDULER, InternalAuthFixture.SchedulerAudience, InternalAuthFixture.SchedulerServiceAccount)]
    public async Task A_token_signed_by_another_key_is_401(string path, string audience, string email)
    {
        (await fixture.GetAsync(path, fixture.OtherToken(audience, email))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_that_is_not_a_jwt_is_401()
    {
        (await fixture.GetAsync(PUBSUB, "not-a-token")).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_audience_nobody_configured_is_401()
    {
        (await fixture.GetAsync(PUBSUB, fixture.GoogleToken("https://elsewhere.test", InternalAuthFixture.PubSubServiceAccount))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(PUBSUB, InternalAuthFixture.PubSubAudience, "someone-else@test.iam.gserviceaccount.com")]
    [InlineData(SCHEDULER, InternalAuthFixture.SchedulerAudience, "someone-else@test.iam.gserviceaccount.com")]
    [InlineData(PUBSUB, InternalAuthFixture.PubSubAudience, InternalAuthFixture.SchedulerServiceAccount)]
    [InlineData(SCHEDULER, InternalAuthFixture.SchedulerAudience, InternalAuthFixture.PubSubServiceAccount)]
    public async Task The_wrong_service_account_is_403(string path, string audience, string email)
    {
        (await fixture.GetAsync(path, fixture.GoogleToken(audience, email))).Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(PUBSUB, InternalAuthFixture.SchedulerAudience, InternalAuthFixture.PubSubServiceAccount)]
    [InlineData(SCHEDULER, InternalAuthFixture.PubSubAudience, InternalAuthFixture.SchedulerServiceAccount)]
    public async Task The_other_callers_audience_is_403(string path, string audience, string email)
    {
        (await fixture.GetAsync(path, fixture.GoogleToken(audience, email))).Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_unverified_email_is_403()
    {
        (await fixture.GetAsync(PUBSUB, fixture.GoogleToken(InternalAuthFixture.PubSubAudience, InternalAuthFixture.PubSubServiceAccount, emailVerified: false)))
            .Should().Be(HttpStatusCode.Forbidden);
    }
}

public class InternalAuthConfigurationTests
{
    private static WebApplicationBuilder Builder(string environment, Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration.AddInMemoryCollection(settings);
        return builder;
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Preview")]
    public void AllowAll_is_refused_outside_development_and_test(string environment)
    {
        var builder = Builder(environment, new() { [InternalAuthConfiguration.AllowAllKey] = "true" });

        var act = builder.AddTemplateInternalAuth;

        act.Should().Throw<InvalidOperationException>().WithMessage($"*Internal:AllowAll*{environment}*");
    }

    [Theory]
    [InlineData(InternalAuthConfiguration.PubSubAudienceKey)]
    [InlineData(InternalAuthConfiguration.PubSubServiceAccountKey)]
    [InlineData(InternalAuthConfiguration.SchedulerAudienceKey)]
    [InlineData(InternalAuthConfiguration.SchedulerServiceAccountKey)]
    public void A_missing_setting_stops_the_host_and_names_the_key(string missingKey)
    {
        var settings = new Dictionary<string, string?>(InternalAuthFixture.Settings) { [missingKey] = " " };
        var builder = Builder("Production", settings);

        var act = builder.AddTemplateInternalAuth;

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{missingKey}*");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public async Task AllowAll_opens_both_policies_in_development_and_test(string environment)
    {
        var builder = Builder(environment, new() { [InternalAuthConfiguration.AllowAllKey] = "true" });
        builder.AddTemplateInternalAuth();

        await using var services = builder.Services.AddLogging().BuildServiceProvider();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        (await authorization.AuthorizeAsync(anonymous, InternalPolicies.PubSub)).Succeeded.Should().BeTrue();
        (await authorization.AuthorizeAsync(anonymous, InternalPolicies.Scheduler)).Succeeded.Should().BeTrue();
    }
}
