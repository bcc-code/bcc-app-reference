using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BccCode.Template.ServiceDefaults;

public static class Extensions
{
    private const string HEALTH_POLICY = "HealthChecks";

    // Set locally by Aspire (its dashboard) and on Cloud Run by Terraform (the BCC collector). Nothing is exported without it.
    public const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    // OpenTelemetry semantic convention; Managed Prometheus takes its instance label from it.
    public const string ServiceInstanceIdAttribute = "service.instance.id";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.AddGcpStructuredLogging();
        builder.ConfigureOpenTelemetry();
        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        // A probe storm must not become database load: answers are cached briefly and a slow check times out.
        builder.Services.AddOutputCache(o => o.AddPolicy(HEALTH_POLICY, p => p.Expire(TimeSpan.FromSeconds(10))));
        builder.Services.AddRequestTimeouts(o => o.AddPolicy(HEALTH_POLICY, TimeSpan.FromSeconds(5)));
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Wolverine retries the handlers, so unsafe methods (POST, PUT, ...) are retried at that one layer only.
            // One pipeline, and so one circuit breaker, per host: the BCC platform clients share the default client name, and a
            // pipeline per name lets a failing service (PubSub publishes) open the circuit for the others (Core API calls).
            // Hosts come only from configuration, so the number of pipelines stays small.
            http.AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods()).SelectPipelineByAuthority();
            http.AddServiceDiscovery();
        });
        return builder;
    }

    private static void ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var export = !string.IsNullOrWhiteSpace(builder.Configuration[OtlpEndpointVariable]);
        var cloudRun = GcpLoggingExtensions.IsCloudRun(builder.Configuration);

        // The runtime would otherwise write traceparent, tracestate and baggage on every outgoing HttpClient request itself,
        // to any host, including the baggage a caller sent us (ASP.NET Core keeps it on the request activity). Without it,
        // only the HttpClient instrumentation writes them, through the propagators below.
        DistributedContextPropagator.Current = DistributedContextPropagator.CreateNoOutputPropagator();
        Sdk.SetDefaultTextMapPropagator(new CompositeTextMapPropagator([new BccPropagator(), new BccBaggagePropagator()]));

        var telemetry = builder.Services.AddOpenTelemetry()
            // Metrics are cumulative per instance. Without an id of its own, two Cloud Run instances (a scale-out, or the old and
            // new revision during a deploy) write to the same series, and Managed Prometheus rejects or misreads their points.
            // Locally Aspire sets the id, which the dashboard matches to the resource, so it is left alone there.
            .ConfigureResource(resource =>
            {
                if (cloudRun) resource.AddAttributes([new(ServiceInstanceIdAttribute, Guid.NewGuid().ToString())]);
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("Wolverine:*");
                if (export) metrics.AddOtlpExporter();
            })
            .WithTracing(tracing =>
            {
                tracing
                    // Every trace is recorded, in prod too: the traffic is low (standard 22.6). A remote parent's flag is ignored:
                    // Google's front end marks most requests to Cloud Run not sampled. A local parent that is not recorded,
                    // /health and /alive below, keeps the default and takes its children with it; AlwaysOnSampler alone would
                    // export the health check's query as a trace of its own.
                    .SetSampler(new ParentBasedSampler(new AlwaysOnSampler(), remoteParentNotSampled: new AlwaysOnSampler()))
                    .AddAspNetCoreInstrumentation(o => o.Filter = context =>
                        !context.Request.Path.StartsWithSegments("/health") && !context.Request.Path.StartsWithSegments("/alive"))
                    .AddHttpClientInstrumentation()
                    // Npgsql's own spans come from Aspire.Npgsql, which the host registers with the data source.
                    .AddSource("Wolverine");
                if (export) tracing.AddOtlpExporter();
            });

        // On Cloud Run the logs go to stdout as JSON only (GcpLoggingExtensions). The collector would write an OTLP copy back
        // into our own Cloud Logging, so every line would be stored, and every error reported, twice.
        if (!cloudRun)
        {
            telemetry.WithLogging(
                logging =>
                {
                    if (export) logging.AddOtlpExporter();
                },
                options =>
                {
                    options.IncludeFormattedMessage = true;
                    options.IncludeScopes = true;
                });
        }
    }

    /// <summary>
    /// <c>/health</c> runs every check, including the database. <c>/alive</c> runs only the checks tagged "live" (the
    /// process itself): a database outage must not fail liveness, or the platform restarts every instance at once.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.UseRequestTimeouts();
        app.UseOutputCache();
        var probes = app.MapGroup("");
        probes.AllowAnonymous().CacheOutput(HEALTH_POLICY).WithRequestTimeout(HEALTH_POLICY);
        probes.MapHealthChecks("/health");
        probes.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });
        return app;
    }
}
