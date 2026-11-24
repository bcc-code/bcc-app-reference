using BccCode.Template.Persistence;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Npgsql;
using OidcMock;

namespace BccCode.Template.IntegrationTests.Infrastructure;

/// <summary>
/// The database and API host one test class shares across its tests. The database is a clone of the migrated template that
/// <see cref="PostgresFixture"/> prepared once, so classes run in parallel and share nothing but the server. Tests in a class start
/// from empty data through <see cref="ResetDataAsync"/>. Sign-in is real: <c>scenario.As(user)</c> makes a session in the session
/// store and sends its cookie (<see cref="TestSessions"/>), so everything in the pipeline (policies, the second-factor check, the
/// cross-origin check, headers, ProblemDetails) is the production code. Internal messages use the durable local inbox, as they do
/// locally (<c>Messaging:UsePubSub</c> is off), and the tests run the durability pass themselves.
/// </summary>
public sealed class AppFixture(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly string _databaseName = $"template_test_{Guid.NewGuid():N}";
    private SessionInjector _sessions = null!;
    private string? _dirtySql;

    public IAlbaHost Host { get; private set; } = null!;

    public string ConnectionString { get; private set; } = null!;

    /// <summary>A new <see cref="TemplateDb"/> on the host's database and schema. Dispose it.</summary>
    public TemplateDb NewDb()
    {
        return Host.Services.GetRequiredService<IDbContextFactory<TemplateDb>>().CreateDbContext();
    }

    public async ValueTask InitializeAsync()
    {
        ConnectionString = await postgres.CreateDatabaseFromTemplateAsync(_databaseName);
        Host = await StartHostAsync();
        _sessions = SessionInjector.Install(Host);
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        await postgres.DropDatabaseAsync(_databaseName);
    }

    /// <summary>
    /// Puts the database back to the state it was cloned in: no rows in any table the tests write to (notes,
    /// sign-in sessions, and Wolverine's inbox, outbox and dead letters), and the sequences they used restarted. The Data
    /// Protection keys stay, because a running host holds its key ring in memory and does not write the keys again, and so do the
    /// migration history and Wolverine's node bookkeeping, which the host depends on.
    /// </summary>
    /// <remarks>
    /// Only the tables that hold a row, and the sequences that handed out a value, are touched, because a truncate costs about the
    /// same per table whether it holds rows or not. Foreign keys and triggers are off for the transaction
    /// (<c>session_replication_role = replica</c>), so the tables can be emptied in any order.
    /// </remarks>
    public async Task ResetDataAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        _dirtySql ??= await BuildDirtySqlAsync(connection);

        var statements = new List<string>();
        await using (var query = new NpgsqlCommand(_dirtySql, connection))
        await using (var reader = await query.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                statements.Add(reader.GetString(0) == "table" ? $"delete from {reader.GetString(1)};" : $"alter sequence {reader.GetString(1)} restart;");
        }

        if (statements.Count > 0)
        {
            await using var transaction = await connection.BeginTransactionAsync();
            await using var command = new NpgsqlCommand($"set local session_replication_role = replica; {string.Concat(statements)}", connection, transaction);
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }

        _sessions.Clear();
    }

    /// <summary>
    /// A query that names every resettable table that holds a row (<c>table</c>) and every sequence one of them owns that has handed
    /// out a value (<c>sequence</c>). Sequences are checked on their own because a test may insert into a table and delete the row
    /// again: left alone, the next test would get other ids.
    /// </summary>
    private static async Task<string> BuildDirtySqlAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            """
            WITH resettable AS (
                SELECT format('%I.%I', schemaname, tablename)::regclass AS oid
                FROM pg_tables
                WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
                  AND tablename NOT IN ('__ef_migrations_history', 'data_protection_keys')
                  AND (tablename NOT LIKE 'wolverine\_%'
                       OR tablename IN ('wolverine_incoming_envelopes', 'wolverine_outgoing_envelopes', 'wolverine_dead_letters'))
            )
            SELECT 'table', r.oid::regclass::text FROM resettable r
            UNION ALL
            SELECT 'sequence', s.oid::regclass::text
            FROM resettable r
            JOIN pg_depend d ON d.classid = 'pg_class'::regclass AND d.refobjid = r.oid AND d.deptype IN ('a', 'i')
            JOIN pg_class s ON s.oid = d.objid AND s.relkind = 'S'
            """, connection);

        var checks = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var (kind, name) = (reader.GetString(0), reader.GetString(1));
                var dirty = kind == "table" ? $"EXISTS (SELECT 1 FROM {name})" : $"(SELECT is_called FROM {name})";
                checks.Add($"SELECT '{kind}', '{name.Replace("'", "''")}' WHERE {dirty}");
            }
        }

        return string.Join("\nUNION ALL ", checks);
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

/// <summary>
/// For the tests that cannot run beside other classes because they change process-wide state (environment variables, as
/// <see cref="SchemaCommands"/> does). xUnit runs this collection alone, after the parallel classes are done.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SerialCollectionDefinition
{
    public const string Name = "serial";
}

/// <summary>
/// Base class for tests on the API host. Every test starts with empty data, so no test depends on what another one stored. The
/// persona helpers sign in as the OIDC mock's personas, whose uids the seeder writes notes for.
/// </summary>
public abstract class AppTest(AppFixture fixture) : IClassFixture<AppFixture>, IAsyncLifetime
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
