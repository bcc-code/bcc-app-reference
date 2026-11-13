using BccCode.Template.Persistence;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using OidcMock;
using Testcontainers.PostgreSql;
using Testcontainers.Xunit;
using Wolverine.Runtime;
using Xunit.Sdk;

namespace BccCode.Template.IntegrationTests.Infrastructure;

/// <summary>
/// One Postgres container and one API host for all integration tests. The schema is applied with db-apply before the host starts,
/// because the host never creates it (standard 12.6). Tests share the container and start from an empty database through
/// <see cref="ResetDataAsync"/>. Sign-in is real: <c>scenario.As(user)</c> makes a session in the session store and sends its
/// cookie (<see cref="TestSessions"/>), so everything in the pipeline (policies, the second-factor check, the cross-origin check,
/// headers, ProblemDetails) is the production code. Internal messages use the durable local inbox, as they do locally
/// (<c>Messaging:UsePubSub</c> is off), and the tests run the durability pass themselves.
/// </summary>
public sealed class AppFixture(IMessageSink messageSink) : ContainerFixture<PostgreSqlBuilder, PostgreSqlContainer>(messageSink)
{
    private SessionInjector _sessions = null!;

    public IAlbaHost Host { get; private set; } = null!;

    public string ConnectionString => Container.GetConnectionString();

    /// <summary>A new <see cref="TemplateDb"/> on the host's database and schema (ADR 0028). Dispose it.</summary>
    public TemplateDb NewDb()
    {
        return Host.Services.GetRequiredService<IDbContextFactory<TemplateDb>>().CreateDbContext();
    }

    protected override PostgreSqlBuilder Configure()
    {
        return new("postgres:18");
    }

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        // What the schema step does before the API starts in a real environment; the API itself never migrates. The tables
        // come first because the host reads the Data Protection key ring while it starts.
        (await SchemaCommands.RunAsync("db-apply", ConnectionString)).Should().Be(0, "db-apply creates the schema the host needs");
        Host = await StartHostAsync();
        _sessions = SessionInjector.Install(Host);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await Host.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    /// <summary>
    /// Removes the rows of every table (notes, their history, changes, sign-in sessions) and Wolverine's inbox, outbox and dead
    /// letters, so a test sees only what it stores itself. The Data Protection keys stay: a running host holds its key ring in
    /// memory and does not write the keys again.
    /// </summary>
    public async Task ResetDataAsync()
    {
        await Host.Services.GetRequiredService<IWolverineRuntime>().Storage.Admin.ClearAllAsync();
        await using var db = NewDb();
        await db.DeleteAllDataAsync([typeof(DataProtectionKeyRow)]);
        _sessions.Clear();
    }

    /// <summary>Another host on the same database, for a test that changes configuration.</summary>
    public async Task<IAlbaHost> StartHostAsync(IReadOnlyDictionary<string, string?>? settings = null, Action<IServiceCollection>? services = null)
    {
        var all = new Dictionary<string, string?>
        {
            ["ConnectionStrings:template"] = ConnectionString,
            ["Oidc:Authority"] = "https://oidc.invalid",
            ["Oidc:ClientId"] = TestOidc.ClientId,
            // What Terraform sets behind the gateway: the forwarded scheme and client address are trusted.
            ["FORWARDEDHEADERS_ENABLED"] = "true",
            ["Auth:AdminRole"] = "admin",
            ["Frontend:Mode"] = "Static",
            ["Frontend:StaticPath"] = TestFrontend.Path,
            // The /internal routes are open in these hosts, as in the AppHost; the tests of the internal authentication start their own
            // hosts without this.
            ["Internal:AllowAll"] = "true",
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>()) all[key] = value;

        var host = await AlbaHost.For<Program>(builder =>
            {
                builder.UseEnvironment("Test");
                builder.ConfigureTestServices(collection =>
                {
                    // No metadata fetch: the sign-in redirect and the back-channel logout validation use this configuration.
                    collection.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme,
                        o => o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(TestOidc.Configuration()));
                    services?.Invoke(collection);
                });
            })
            .WithConfiguration(all);
        // The host that is not the fixture's own gets its sessions too; they live as long as that host.
        if (_sessions is not null) SessionInjector.Install(host);
        return host;
    }
}

/// <summary>A folder with an index.html, which the API serves in Static mode.</summary>
public static class TestFrontend
{
    public static string Path { get; } = Create();

    private static string Create()
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "template-test-frontend-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(System.IO.Path.Combine(folder, "index.html"),
            "<!doctype html><html><head><meta property=\"csp-nonce\" nonce=\"__CSP_NONCE__\"><title>Template</title>" +
            "<script nonce=\"__CSP_NONCE__\" type=\"module\" src=\"/assets/app.js\"></script>" +
            "<link nonce=\"__CSP_NONCE__\" rel=\"stylesheet\" href=\"/assets/app.css\"></head><body><div id=\"app\"></div></body></html>");
        return folder;
    }
}

[CollectionDefinition(Name)]
public sealed class AppCollectionDefinition : ICollectionFixture<AppFixture>
{
    public const string Name = "app";
}

/// <summary>
/// Base class for tests on the API host. Every test starts with empty data, so no test depends on what another one stored. The
/// persona helpers sign in as the OIDC mock's personas, whose uids the seeder writes notes for.
/// </summary>
[Collection(AppCollectionDefinition.Name)]
public abstract class AppTest(AppFixture fixture) : IAsyncLifetime
{
    protected AppFixture Fixture => fixture;

    /// <summary>The host as an anonymous caller. Use <see cref="AsUser"/> or <see cref="As"/> for a signed-in one.</summary>
    protected IAlbaHost Host => fixture.Host;

    public virtual async ValueTask InitializeAsync()
    {
        await fixture.ResetDataAsync();
    }

    /// <summary>The host as the "user" persona.</summary>
    protected Task<IScenarioResult> AsUser(Action<Scenario> configure)
    {
        return As(Persona.User, configure);
    }

    /// <summary>The host (or another one) as a persona, signed in with a real session.</summary>
    protected Task<IScenarioResult> As(Persona persona, Action<Scenario> configure, IAlbaHost? host = null)
    {
        return (host ?? Host).Scenario(s =>
        {
            s.As(TestUser.Of(persona));
            configure(s);
        });
    }

    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}

public static class ScenarioExtensions
{
    /// <summary>Signs the request in as <paramref name="user"/>: a real session in the store, created when the request runs.</summary>
    public static void As(this Scenario scenario, TestUser user)
    {
        TestSessions.Sign(scenario, user);
    }

    /// <summary>
    /// Marks the request as a browser's on our own origin: it arrives over https and carries the fetch metadata a browser sends for a
    /// page of the app. Pass <c>sameOrigin: false</c> for a request that another site caused.
    /// </summary>
    public static void AsBrowser(this Scenario scenario, bool sameOrigin = true)
    {
        scenario.WithRequestHeader("X-Forwarded-Proto", "https");
        scenario.WithRequestHeader("Sec-Fetch-Site", sameOrigin ? "same-origin" : "cross-site");
    }
}
