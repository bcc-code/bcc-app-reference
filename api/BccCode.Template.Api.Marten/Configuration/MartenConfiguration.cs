using BccCode.Template.Api.Marten.Infrastructure;
using BccCode.Template.Web.Auth;
using BccCode.Template.Web.DataProtection;
using BccCode.Template.Web.Http;
using JasperFx;
using Marten;
using Npgsql;
using Weasel.Core;
using Wolverine.Marten;

namespace BccCode.Template.Api.Marten.Configuration;

public static class MartenConfiguration
{
    public const string ConnectionName = "template";

    public static void AddTemplateMarten(this WebApplicationBuilder builder)
    {
        var connectionString = CodeGeneration.IsGeneratingOpenApi()
            ? "Host=localhost;Database=openapi"
            : builder.Configuration.GetConnectionString(ConnectionName)
              ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionName} is not configured.");

        // Pooler-safe settings (standard 12, rule 11): behind a transaction-mode pooler no session state survives between
        // transactions, so connections are not reset on close and statements are not auto-prepared.
        var npgsql = new NpgsqlConnectionStringBuilder(connectionString) { NoResetOnClose = true, MaxAutoPrepare = 0 };

        builder.Services.AddMarten(options =>
            {
                options.Connection(npgsql.ConnectionString);
                options.UseSystemTextJsonForSerialization(EnumStorage.AsString);
                // The schema is created by the schema step (resources setup) before the API starts, never by the API in a
                // shared environment: a failed change then stops the deploy instead of crash-looping every instance.
                options.AutoCreateSchemaObjects = builder.Environment.IsDevelopmentOrTest() ? AutoCreate.CreateOrUpdate : AutoCreate.None;
                TemplateStore.Configure(options);
            })
            .UseLightweightSessions()
            .IntegrateWithWolverine();

        builder.Services.AddSingleton<IAuthSessionStorage, MartenAuthSessions>();
        builder.AddTemplateDataProtection(sp => new MartenDataProtectionKeys(sp.GetRequiredService<IDocumentStore>()));
        builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
    }
}
