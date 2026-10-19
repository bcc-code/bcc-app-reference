using System.Security.Claims;
using System.Text.Encodings.Web;
using BccCode.Template.Api.EfCore.Configuration;
using BccCode.Template.Api.EfCore.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace BccCode.Template.Api.EfCore.Tests.Infrastructure;

/// <summary>A signed-in test user. Each test makes its own, so tests share one database without seeing each other's notes.</summary>
public sealed record TestUser(string Subject, string Name, params string[] Roles)
{
    public static TestUser New(string name = "Test User", params string[] roles) => new(Guid.NewGuid().ToString(), name, roles);

    public static TestUser NewAdmin() => New("Test Admin", "admin");

    internal string Header => $"{Subject}|{Name}|{string.Join(',', Roles)}";
}

/// <summary>
/// The real API host (Alba) on a real Postgres (Testcontainers), shared by the tests of one collection. Sign-in is replaced by
/// a header: <c>X-Test-User</c> names the person, and a request without it is not signed in. Everything else in the
/// pipeline (policies, CSRF, headers, ProblemDetails) is the production code.
/// </summary>
public sealed class AppFixture : IAsyncLifetime
{
    public const string UserHeader = "X-Test-User";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();

    public IAlbaHost Host { get; private set; } = null!;

    public string ConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        // What the migrate step does before the API starts in a real environment; the API itself never migrates. The tables
        // come first because the host reads the Data Protection key ring while it starts.
        var options = new DbContextOptionsBuilder<TemplateDbContext>();
        EfCoreConfiguration.ConfigureProvider(options, ConnectionString);
        await using (var db = new TemplateDbContext(options.Options)) await db.Database.MigrateAsync();
        Host = await StartHostAsync();
        await Migrator.RunAsync(Host.Services, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>Another host on the same database, for a test that changes configuration.</summary>
    public Task<IAlbaHost> StartHostAsync(IReadOnlyDictionary<string, string?>? settings = null, Action<IServiceCollection>? services = null) =>
        AlbaHost.For<Program>(builder =>
        {
            builder.UseEnvironment("Test");
            var all = new Dictionary<string, string?>
            {
                ["ConnectionStrings:template"] = ConnectionString,
                ["OIDC:Authority"] = "https://oidc.invalid",
                ["OIDC:ClientId"] = "template",
                ["Auth:AdminRole"] = "admin",
                ["Frontend:Mode"] = "Static",
                ["Frontend:StaticPath"] = TestFrontend.Path,
            };
            foreach (var (key, value) in settings ?? new Dictionary<string, string?>()) all[key] = value;
            foreach (var (key, value) in all) builder.UseSetting(key, value);

            builder.ConfigureTestServices(collection =>
            {
                collection.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                collection.PostConfigure<AuthenticationOptions>(o => o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName);
                services?.Invoke(collection);
            });
        });

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var header)) return Task.FromResult(AuthenticateResult.NoResult());

            var parts = header.ToString().Split('|');
            var claims = new List<Claim> { new("sub", parts[0]), new("name", parts[1]) };
            claims.AddRange(parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(role => new Claim("role", role)));
            var identity = new ClaimsIdentity(claims, SchemeName, "name", "role");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
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
            "<!doctype html><html><head><title>Template</title><script type=\"module\" src=\"/assets/app.js\"></script></head><body></body></html>");
        return folder;
    }
}

[CollectionDefinition(Name)]
public sealed class AppCollectionDefinition : ICollectionFixture<AppFixture>
{
    public const string Name = "app";
}

public static class ScenarioExtensions
{
    public static void As(this Scenario scenario, TestUser user) => scenario.WithRequestHeader(AppFixture.UserHeader, user.Header);

    /// <summary>Marks the request as a browser's: it arrives over https and carries the session cookie, plus any extra cookies.</summary>
    public static void AsBrowser(this Scenario scenario, string extraCookies = "")
    {
        scenario.WithRequestHeader("X-Forwarded-Proto", "https");
        scenario.WithRequestHeader("Cookie", "__Host-template=session" + (extraCookies.Length > 0 ? "; " + extraCookies : ""));
    }
}
