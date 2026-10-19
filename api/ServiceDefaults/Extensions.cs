using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace BccCode.Template.ServiceDefaults;

public static class Extensions
{
    private const string HealthPolicy = "HealthChecks";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.AddGcpStructuredLogging();
        builder.ConfigureOpenTelemetry();
        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        // A probe storm must not become database load: answers are cached briefly and a slow check times out.
        builder.Services.AddOutputCache(o => o.AddPolicy(HealthPolicy, p => p.Expire(TimeSpan.FromSeconds(10))));
        builder.Services.AddRequestTimeouts(o => o.AddPolicy(HealthPolicy, TimeSpan.FromSeconds(5)));
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });
        return builder;
    }

    private static void ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("Wolverine:*")
                .AddMeter("Marten"))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(o => o.Filter = context =>
                    !context.Request.Path.StartsWithSegments("/health") && !context.Request.Path.StartsWithSegments("/alive"))
                .AddHttpClientInstrumentation()
                .AddNpgsql()
                .AddSource("Wolverine")
                .AddSource("Marten"));

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
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
        probes.AllowAnonymous().CacheOutput(HealthPolicy).WithRequestTimeout(HealthPolicy);
        probes.MapHealthChecks("/health");
        probes.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });
        return app;
    }
}
