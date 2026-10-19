using BccCode.Template.Web.Http;
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

        // Document generation runs the app in Production during a Debug build, before any code is pre-generated.
        var dynamicCode = builder.Environment.IsDevelopmentOrTest() || CodeGeneration.IsGeneratingOpenApi();

        builder.Services.AddWolverine(options =>
        {
            options.ApplicationAssembly = typeof(Program).Assembly;

            // Serverless: no agents and no polling, so the API needs no CPU between requests. The app sends no messages yet.
            // Once it does, pick the delivery style in an ADR: a durable outbox (Balanced) or send-after-commit with a reconcile job.
            options.Durability.Mode = DurabilityMode.Serverless;
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
