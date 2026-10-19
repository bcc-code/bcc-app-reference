using BccCode.Template.Api.EfCore.Infrastructure;
using BccCode.Template.Web.Auth;
using BccCode.Template.Web.DataProtection;
using BccCode.Template.Web.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BccCode.Template.Api.EfCore.Configuration;

public static class EfCoreConfiguration
{
    public const string ConnectionName = "template";

    public static void AddTemplateEfCore(this WebApplicationBuilder builder)
    {
        var connectionString = builder.TemplateConnectionString();

        // A factory, which also registers the scoped context the endpoints receive. The factory is what the singletons use
        // (session storage, the key ring, the health check): they must not hold a scoped context.
        builder.Services.AddDbContextFactory<TemplateDbContext>(options => ConfigureProvider(options, connectionString));

        builder.Services.AddSingleton<IAuthSessionStorage, EfAuthSessions>();
        builder.AddTemplateDataProtection(sp => new EfDataProtectionKeys(sp.GetRequiredService<IDbContextFactory<TemplateDbContext>>()));
        builder.Services.AddExceptionHandler<ConcurrencyConflictHandler>();
        builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
    }

    /// <summary>
    /// The connection string with the pooler-safe settings (standard 12, rule 11): behind a transaction-mode pooler no session
    /// state survives between transactions, so connections are not reset on close and statements are not auto-prepared.
    /// </summary>
    public static string TemplateConnectionString(this WebApplicationBuilder builder)
    {
        var connectionString = CodeGeneration.IsGeneratingOpenApi()
            ? "Host=localhost;Database=openapi"
            : builder.Configuration.GetConnectionString(ConnectionName)
              ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionName} is not configured.");
        return new NpgsqlConnectionStringBuilder(connectionString) { NoResetOnClose = true, MaxAutoPrepare = 0 }.ConnectionString;
    }

    /// <summary>The provider settings the API, the migrate step and <c>dotnet ef</c> share, so they agree on the model and the SQL.</summary>
    public static void ConfigureProvider(DbContextOptionsBuilder options, string connectionString)
    {
        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
    }
}
