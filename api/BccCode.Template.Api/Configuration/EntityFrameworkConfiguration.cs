using BccCode.Template.Api.Auth;
using BccCode.Template.Api.DataProtection;
using BccCode.Template.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BccCode.Template.Api.Configuration;

/// <summary>
/// The relational store (ADR 0028): <see cref="TemplateDb"/> on the app's connection, its tables in the schema of
/// <see cref="PersistenceSchema"/>. The schema step applies its migrations (<see cref="TemplateMigrations"/>); the app never does.
/// </summary>
public static class EntityFrameworkConfiguration
{
    public const string ConnectionName = "template";

    public static void AddTemplateDb(this WebApplicationBuilder builder)
    {
        if (CodeGeneration.IsRunningGeneration())
        {
            // Generating the OpenAPI document and the Wolverine code needs no database, but the data source does need a
            // connection string. This placeholder is never used to connect.
            builder.Configuration[$"ConnectionStrings:{ConnectionName}"] ??= "Host=localhost;Database=generation";
        }
        else if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString(ConnectionName)))
        {
            throw new InvalidOperationException($"ConnectionStrings:{ConnectionName} is not configured.");
        }

        // The data source gives /health a Postgres check and OpenTelemetry the Npgsql traces and metrics. EF Core shares it.
        ConfigureDataSource(builder);
        builder.Services.AddTemplateDb(PersistenceSchema.Read(builder.Configuration));

        builder.Services.AddSingleton<IAuthSessionStorage, EfAuthSessions>();
        builder.AddTemplateDataProtection(sp => new EfDataProtectionKeys(sp.GetRequiredService<IDbContextFactory<TemplateDb>>()));
    }

    private static void ConfigureDataSource(WebApplicationBuilder builder)
    {
        // Pooler-safe settings (standard 12.11): they keep working if the connections go through a transaction-mode pooler,
        // where no session state survives a transaction, so connections are not reset on close and statements are not auto-prepared.
        builder.AddNpgsqlDataSource(ConnectionName, configureDataSourceBuilder: dataSource =>
        {
            dataSource.ConnectionStringBuilder.NoResetOnClose = true;
            dataSource.ConnectionStringBuilder.MaxAutoPrepare = 0;
        });
    }

    /// <summary>
    /// The connection string with the same pooler-safe settings, for Wolverine's message store, which opens its own connections.
    /// A JasperFx command that only builds the host (<c>openapi</c>, <c>codegen</c>) has no database: it gets a placeholder.
    /// </summary>
    public static string TemplateConnectionString(this WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(ConnectionName)
            ?? (CodeGeneration.IsRunningGeneration()
                ? "Host=localhost;Database=generation"
                : throw new InvalidOperationException($"ConnectionStrings:{ConnectionName} is not configured."));
        return new NpgsqlConnectionStringBuilder(connectionString) { NoResetOnClose = true, MaxAutoPrepare = 0 }.ConnectionString;
    }
}

/// <summary>
/// Applies (<c>jasper db-apply</c>) or checks (<c>jasper db-assert</c>) the EF Core migrations, before the JasperFx command does the
/// same for Wolverine's tables. Migrations are written without a schema; they run on a connection whose <c>search_path</c> is the
/// configured schema, so a PR preview gets its tables in its own schema.
/// </summary>
public static partial class TemplateMigrations
{
    /// <returns>The exit code: 0 when there is nothing to report, 1 when <c>db-assert</c> finds a migration that is not applied.</returns>
    public static async Task<int> RunAsync(WebApplication app, string command, CancellationToken cancellationToken = default)
    {
        if (command is not ("db-apply" or "db-assert")) return 0;

        var schema = PersistenceSchema.Read(app.Configuration);
        var connectionString = new NpgsqlConnectionStringBuilder(
            app.Configuration.GetConnectionString(EntityFrameworkConfiguration.ConnectionName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{EntityFrameworkConfiguration.ConnectionName} is not configured."))
        {
            // Not from the app's pool: search_path is set for this connection only.
            Pooling = false,
            SearchPath = schema,
        };
        var options = TemplateDb.Configure(new DbContextOptionsBuilder<TemplateDb>())
            .UseNpgsql(connectionString.ConnectionString, npgsql => npgsql.MigrationsHistoryTable(TemplateDb.MigrationsHistoryTable))
            .Options;
        await using var db = new TemplateDb((DbContextOptions<TemplateDb>)options, TemplateDbSchema.ForMigrations);

        if (command == "db-apply")
        {
#pragma warning disable EF1002 // The schema name is validated by PersistenceSchema and quoted.
            await db.Database.ExecuteSqlRawAsync($"create schema if not exists {new NpgsqlCommandBuilder().QuoteIdentifier(schema)}", cancellationToken);
#pragma warning restore EF1002
            await db.Database.MigrateAsync(cancellationToken);   // takes a database lock, so two instances applying at once do not collide
            LogApplied(app.Logger, schema);
            return 0;
        }

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0) return 0;
        LogPending(app.Logger, schema, string.Join(", ", pending));
        return 1;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "EF Core migrations applied to schema {Schema}")]
    private static partial void LogApplied(ILogger logger, string schema);

    [LoggerMessage(Level = LogLevel.Error, Message = "EF Core migrations not applied to schema {Schema}: {Migrations}")]
    private static partial void LogPending(ILogger logger, string schema, string migrations);
}
