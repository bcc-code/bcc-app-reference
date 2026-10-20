using BccCode.Template.Web.Http;
using BccCode.Template.Web.Messaging;
using JasperFx;
using JasperFx.CodeGeneration;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Http;
using Wolverine.Postgresql;

namespace BccCode.Template.Api.EfCore.Configuration;

public static class WolverineConfiguration
{
    public static void AddTemplateWolverine(this WebApplicationBuilder builder)
    {
        if (CodeGeneration.IsGeneratingOpenApi())
        {
            builder.Services.DisableAllExternalWolverineTransports();
            builder.Services.DisableAllWolverineMessagePersistence();
        }
        else if (CodeGeneration.IsCommandLineTool())
        {
            // The schema and seed steps and code generation send nothing: no Pub/Sub, but the message store stays (the schema step creates it).
            builder.Services.DisableAllExternalWolverineTransports();
        }

        // Document generation runs the app in Production during a Debug build, before any code is pre-generated.
        var dynamicCode = builder.Environment.IsDevelopmentOrTest() || CodeGeneration.IsGeneratingOpenApi();

        var connectionString = builder.TemplateConnectionString();
        builder.Services.AddWolverine(options =>
        {
            options.ApplicationAssembly = typeof(Program).Assembly;

            // ServerlessWithOutbox and Pub/Sub, shared by both variants (ADR 0007): no agents or polling, a transactional outbox,
            // a push receiver at /internal/pubsub/{subscription} and the durability pass at /internal/durability/pass.
            options.ConfigureTemplateMessaging(builder.Environment, builder.Configuration);
            // The outbox and Wolverine's EF transaction need a message store. It lives in the "wolverine" schema of the same
            // database and is created by the migrate step, never at startup.
            options.PersistMessagesWithPostgresql(connectionString);
            options.UseEntityFrameworkCoreTransactions();
            options.Policies.AutoApplyTransactions();   // one request, one transaction
            options.UseSystemTextJsonForSerialization();
#if DEBUG
            if (dynamicCode) options.UseRuntimeCompilation();
#endif
        });

        builder.Services.CritterStackDefaults(x =>
        {
            // "Production" is every environment that is not Development, so Test is handled explicitly here.
            // Deployed environments run pre-generated code (`codegen write` in the image build) and assert it exists.
            x.Production.GeneratedCodeMode = dynamicCode ? TypeLoadMode.Dynamic : TypeLoadMode.Static;
            x.Production.AssertAllPreGeneratedTypesExist = !dynamicCode;
            x.Production.SourceCodeWritingEnabled = false;
            x.Development.GeneratedCodeMode = TypeLoadMode.Dynamic;
            x.Development.SourceCodeWritingEnabled = false;
            // The "wolverine" schema is created at startup in Development and Test only; elsewhere the migrate step creates it.
            x.Production.ResourceAutoCreate = dynamicCode ? AutoCreate.CreateOrUpdate : AutoCreate.None;
        });

        builder.Services.AddWolverineHttp();
    }
}
