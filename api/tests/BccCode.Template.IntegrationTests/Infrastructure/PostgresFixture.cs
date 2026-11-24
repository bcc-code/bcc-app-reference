using Npgsql;
using Testcontainers.PostgreSql;

namespace BccCode.Template.IntegrationTests.Infrastructure;

/// <summary>
/// The one Postgres container of the run, an assembly fixture so test classes are not forced into one collection and xUnit can run
/// them in parallel. The schema is applied once, to a template database, the way the schema step does before the API starts in a real
/// environment (<c>jasper db-apply</c>: the EF migrations, then Wolverine's tables; the host itself never creates the schema,
/// standard 12.6). Each test class then clones that template (<see cref="CreateDatabaseFromTemplateAsync"/>), which is a file copy and
/// far cheaper than running the migrations again.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string TEMPLATE_DATABASE = "template_test_template";

    // The container is thrown away after the run, so nothing needs to survive a crash: no fsync, no WAL flush per commit, and the
    // data directory lives in memory. max_connections is raised because classes run in parallel, each with the pools of its own
    // hosts. Postgres 18 images keep the data in a versioned folder under /var/lib/postgresql, so that is the folder in memory.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18")
        .WithCommand(
            "-c", "max_connections=1000",
            "-c", "fsync=off",
            "-c", "synchronous_commit=off",
            "-c", "full_page_writes=off")
        .WithTmpfsMount("/var/lib/postgresql")
        .Build();

    /// <summary>A connection string to the server's default database, for creating and dropping databases.</summary>
    public string AdminConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        // Pooling is off for the template: Postgres refuses to clone a database that anything is still connected to, and a pooled
        // connection left idle by db-apply would be that.
        var template = BuildConnectionString(TEMPLATE_DATABASE) + ";Pooling=false";
        await ExecuteAdminAsync($"create database \"{TEMPLATE_DATABASE}\"");
        (await SchemaCommands.RunAsync("db-apply", template)).Should().Be(0, "db-apply creates the schema every host needs");
        await ExecuteAdminAsync(
            $"select pg_terminate_backend(pid) from pg_stat_activity where datname = '{TEMPLATE_DATABASE}' and pid <> pg_backend_pid()");
        await ExecuteAdminAsync($"alter database \"{TEMPLATE_DATABASE}\" is_template true");
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public string BuildConnectionString(string database)
    {
        return new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = database }.ConnectionString;
    }

    /// <summary>Clones the migrated template into a new database and returns its connection string.</summary>
    public async Task<string> CreateDatabaseFromTemplateAsync(string database)
    {
        await ExecuteAdminAsync($"create database \"{database}\" template \"{TEMPLATE_DATABASE}\"");
        return BuildConnectionString(database);
    }

    public async Task DropDatabaseAsync(string database)
    {
        // WITH (FORCE) ends the sessions a host under test has not closed yet.
        await ExecuteAdminAsync($"drop database if exists \"{database}\" with (force)");
        await using var pooled = new NpgsqlConnection(BuildConnectionString(database));
        NpgsqlConnection.ClearPool(pooled);
    }

    private async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
