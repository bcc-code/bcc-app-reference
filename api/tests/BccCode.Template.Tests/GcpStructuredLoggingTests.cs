using System.Diagnostics;
using System.Text.Json;
using BccCode.Template.ServiceDefaults;
using Google.Cloud.Logging.Console;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace BccCode.Template.Tests;

/// <summary>
/// Cloud Run's log agent reads stdout one line at a time, so the contract is one JSON line per entry with the fields Cloud
/// Logging recognises. The formatter comes from Google.Cloud.Logging.Console; these tests pin our configuration of it, so a
/// broken format cannot silently disconnect logs from traces (standard 22, rule 7).
/// </summary>
public class GcpStructuredLoggingTests
{
    [Fact]
    public void On_Cloud_Run_the_structured_formatter_is_selected()
    {
        using var app = BuildApp(cloudRunService: "template-api", projectId: "template-sandbox");

        var formatter = app.Services.GetServices<ConsoleFormatter>().Should().ContainSingle(f => f is GoogleCloudConsoleFormatter).Subject;
        app.Services.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue.FormatterName.Should().Be(formatter.Name);
    }

    [Fact]
    public void Outside_Cloud_Run_a_project_id_alone_keeps_the_readable_formatter()
    {
        using var app = BuildApp(cloudRunService: null, projectId: "template-sandbox");

        app.Services.GetServices<ConsoleFormatter>().Should().NotContain(f => f is GoogleCloudConsoleFormatter);
    }

    [Theory]
    [InlineData(LogLevel.Debug, "DEBUG")]
    [InlineData(LogLevel.Information, "INFO")]
    [InlineData(LogLevel.Warning, "WARNING")]
    [InlineData(LogLevel.Error, "ERROR")]
    public void The_log_level_becomes_the_Cloud_Logging_severity(LogLevel level, string severity)
    {
        Format(level, "Something happened").GetProperty("severity").GetString().Should().Be(severity);
    }

    [Fact]
    public void A_multi_line_message_and_stack_trace_stay_on_one_line()
    {
        var output = Write(LogLevel.Error, "Line one\nline two", Thrown());

        output.TrimEnd('\r', '\n').Should().NotContain("\n");
        JsonElement.Parse(output).GetProperty("exception").GetString().Should().Contain("boom");
    }

    [Fact]
    public void An_exception_at_error_level_is_tagged_for_Error_Reporting()
    {
        Format(LogLevel.Error, "Failed", Thrown()).GetProperty("@type").GetString().Should().Be(ErrorReportingLogAugmenter.ReportedErrorEventType);
    }

    [Fact]
    public void A_warning_with_an_exception_is_not_tagged()
    {
        Format(LogLevel.Warning, "Retrying", Thrown()).TryGetProperty("@type", out _).Should().BeFalse();
    }

    [Fact]
    public void An_entry_links_to_the_current_trace_when_the_project_id_is_known()
    {
        using var source = new ActivitySource(nameof(GcpStructuredLoggingTests));
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == nameof(GcpStructuredLoggingTests),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("request");

        var entry = Format(LogLevel.Information, "Handled", projectId: "template-sandbox");

        entry.GetProperty("logging.googleapis.com/trace").GetString().Should().Be($"projects/template-sandbox/traces/{activity!.TraceId.ToHexString()}");
    }

    private static WebApplication BuildApp(string? cloudRunService, string? projectId)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [GcpLoggingExtensions.CloudRunServiceVariable] = cloudRunService,
            [GcpLoggingExtensions.ProjectIdConfigurationKey] = projectId,
        });
        builder.AddGcpStructuredLogging();
        return builder.Build();
    }

    private static JsonElement Format(LogLevel level, string message, Exception? exception = null, string? projectId = null)
    {
        return JsonElement.Parse(Write(level, message, exception, projectId));
    }

    private static string Write(LogLevel level, string message, Exception? exception = null, string? projectId = null)
    {
        var options = new GoogleCloudConsoleFormatterOptions();
        GcpLoggingExtensions.ConfigureFormatter(options, projectId);
        using var formatter = new GoogleCloudConsoleFormatter(new StaticOptionsMonitor(options));

        var entry = new LogEntry<IReadOnlyList<KeyValuePair<string, object?>>>(level, "Template.Test", new EventId(0), [], exception, (_, _) => message);
        var writer = new StringWriter();
        formatter.Write(entry, scopeProvider: null!, writer);
        return writer.ToString();
    }

    private static InvalidOperationException Thrown()
    {
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException e)
        {
            return e;
        }
    }

    private sealed class StaticOptionsMonitor(GoogleCloudConsoleFormatterOptions options) : IOptionsMonitor<GoogleCloudConsoleFormatterOptions>
    {
        public GoogleCloudConsoleFormatterOptions CurrentValue => options;

        public GoogleCloudConsoleFormatterOptions Get(string? name)
        {
            return options;
        }

        public IDisposable? OnChange(Action<GoogleCloudConsoleFormatterOptions, string?> listener)
        {
            return null;
        }
    }
}
