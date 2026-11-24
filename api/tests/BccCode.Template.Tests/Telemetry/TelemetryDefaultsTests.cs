using BccCode.Template.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;

namespace BccCode.Template.Tests.Telemetry;

public sealed class TelemetryDefaultsTests
{
    [Fact]
    public void On_Cloud_Run_logs_are_not_sent_over_OpenTelemetry_as_well()
    {
        // stdout JSON is the only log path there; an OTLP copy would come back into our own Cloud Logging.
        using var app = Build(cloudRunService: "template-api-sandbox");

        app.Services.GetServices<ILoggerProvider>().Should().NotContain(p => p is OpenTelemetryLoggerProvider);
    }

    [Fact]
    public void Locally_logs_go_through_OpenTelemetry_to_the_Aspire_dashboard()
    {
        using var app = Build(cloudRunService: null);

        app.Services.GetServices<ILoggerProvider>().Should().Contain(p => p is OpenTelemetryLoggerProvider);
    }

    [Fact]
    public void On_Cloud_Run_each_instance_has_an_instance_id_of_its_own()
    {
        // Otherwise two instances write the same cumulative metric series (the resource there names no instance).
        using var first = Build(cloudRunService: "template-api-sandbox", resourceAttributes: "cloud.account.id=p,deployment.environment.name=sandbox");
        using var second = Build(cloudRunService: "template-api-sandbox", resourceAttributes: "cloud.account.id=p,deployment.environment.name=sandbox");

        var id = InstanceId(first);

        id.Should().NotBeNullOrWhiteSpace();
        InstanceId(second).Should().NotBe(id);
    }

    [Fact]
    public void Locally_the_instance_id_from_Aspire_is_kept()
    {
        // The dashboard matches telemetry to the resource by it.
        using var app = Build(cloudRunService: null, resourceAttributes: "service.instance.id=api-abc");

        InstanceId(app).Should().Be("api-abc");
    }

    private static WebApplication Build(string? cloudRunService, string? resourceAttributes = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [GcpLoggingExtensions.CloudRunServiceVariable] = cloudRunService,
            ["OTEL_RESOURCE_ATTRIBUTES"] = resourceAttributes,
        });
        return builder.AddServiceDefaults().Build();
    }

    private static string? InstanceId(WebApplication app)
    {
        return app.Services.GetRequiredService<MeterProvider>().GetResource().Attributes
            .SingleOrDefault(a => a.Key == Extensions.ServiceInstanceIdAttribute).Value as string;
    }
}
