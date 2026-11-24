using BccCode.Template.Api.Messaging;
using JasperFx;
using JasperFx.CodeGeneration;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;
using Wolverine.Persistence;
using Wolverine.Postgresql;

namespace BccCode.Template.Api.Configuration;

public static class WolverineConfiguration
{
    public static void AddTemplateWolverine(this WebApplicationBuilder builder)
    {
        // Development, the test host and code generation (`jasper codegen`, `jasper openapi`) compile at run time.
        var dynamicCode = builder.Environment.IsDevelopmentOrTest() || CodeGeneration.IsRunningGeneration();

        var connectionString = builder.TemplateConnectionString();
        var schema = PersistenceSchema.Read(builder.Configuration);
        builder.Services.AddWolverine(options =>
        {
            // Generated code compiles into the entry assembly. The slices live in BccCode.Template, so include it for discovery.
            options.ApplicationAssembly = typeof(Program).Assembly;
            options.Discovery.IncludeAssembly(typeof(TemplateAssemblyMarker).Assembly);

            // ServerlessWithOutbox, Pub/Sub or the local inbox, and the failure policy (ADR 0024): a push receiver at
            // /internal/pubsub/{subscription} and the durability pass at /internal/durability/pass.
            options.ConfigureTemplateMessaging(builder.Environment, builder.Configuration);
            // The outbox and Wolverine's EF transaction need a message store. It lives in the app's schema of the same database
            // (a PR preview has its own) and is created by `jasper db-apply`, never at startup.
            options.PersistMessagesWithPostgresql(connectionString, schema);
            // Eager: an explicit transaction around the endpoint, so the rows and the outbox messages of a request commit together.
            options.UseEntityFrameworkCoreTransactions(TransactionMiddlewareMode.Eager);
            options.Policies.AutoApplyTransactions();   // one request, one transaction
            options.UseSystemTextJsonForSerialization();
            // Finds the IValidator<T> implementations by assembly scanning.
            options.UseFluentValidation();
#if DEBUG
            if (dynamicCode) options.UseRuntimeCompilation();
#endif
        });

        builder.Services.CritterStackDefaults(x =>
        {
            // "Production" is every environment that is not Development, so Test is handled explicitly here.
            // Deployed environments run pre-generated code (`jasper codegen write` in the image build) and assert it exists.
            x.Production.GeneratedCodeMode = dynamicCode ? TypeLoadMode.Dynamic : TypeLoadMode.Static;
            x.Production.AssertAllPreGeneratedTypesExist = !dynamicCode;
            x.Production.SourceCodeWritingEnabled = false;
            x.Development.GeneratedCodeMode = TypeLoadMode.Dynamic;
            x.Development.SourceCodeWritingEnabled = false;
            // The schema is applied by the separate `jasper db-apply` step in every environment (standard 12.6), never by the app.
            x.Development.ResourceAutoCreate = AutoCreate.None;
            x.Production.ResourceAutoCreate = AutoCreate.None;
        });

        builder.Services.AddWolverineHttp();
        // After Wolverine's own hosted service, so the runtime is started when the listeners are. The schema step, codegen and openapi
        // serve nothing.
        if (BccPubSubSettings.Enabled(builder.Configuration) && !CodeGeneration.IsJasperFxCommand())
            builder.Services.AddHostedService<BccPubSubListenerStarter>();
    }

    public static void MapTemplateApi(this WebApplication app)
    {
        app.MapWolverineEndpoints(options =>
        {
            if (!CodeGeneration.IsRunningGeneration()) options.WarmUpRoutes = RouteWarmup.Lazy;
            options.UseTemplateConventions();
            // A query value that cannot be parsed (?includeArchived=maybe) answers 400 rather than binding the default and going
            // on, as MVC and minimal APIs do.
            options.RejectUnparseableQueryValues = true;
            // A request with an IValidator<T> is checked before the endpoint runs; failures answer 400 ValidationProblemDetails.
            options.UseFluentValidationProblemDetailMiddleware();
            // A CallerAccess parameter is loaded once per request, never read from the body.
            options.AddParameterHandlingStrategy<CallerAccessParameterStrategy>();
            // A stale version and a second writer of one row answer 409 on every transactional chain, listed in the OpenAPI document.
            options.AddPolicy<ConflictPolicy>();
            // Permission runs after LoadAsync and before Before and Validate (standard 09.2).
            options.AddPolicy<PermissionPolicy>();
        });
    }
}
