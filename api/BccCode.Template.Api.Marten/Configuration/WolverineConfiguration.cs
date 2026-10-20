using BccCode.Template.Web.Http;
using BccCode.Template.Web.Messaging;
using JasperFx;
using JasperFx.CodeGeneration;
using Wolverine;
using Wolverine.Http;

namespace BccCode.Template.Api.Marten.Configuration;

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

        builder.Services.AddWolverine(options =>
        {
            options.ApplicationAssembly = typeof(Program).Assembly;

            // ServerlessWithOutbox and Pub/Sub, shared by both variants (ADR 0007): no agents or polling, a transactional outbox,
            // a push receiver at /internal/pubsub/{subscription} and the durability pass at /internal/durability/pass.
            options.ConfigureTemplateMessaging(builder.Environment, builder.Configuration);
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
        });

        builder.Services.AddWolverineHttp();
    }
}
