using System.Text.Json;
using Google.Cloud.Logging.Console;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BccCode.Template.ServiceDefaults;

/// <summary>
/// Switches the console logger over to Cloud Logging's structured JSON format. Cloud Run's log
/// agent reads container stdout line by line, so the default text formatter turns every line of a
/// multi-line message (and every frame of a stack trace) into its own entry, all at INFO severity
/// with nothing tying them together. Google's <c>GoogleCloudConsoleFormatter</c> writes one
/// single-line JSON object per entry using the field names the agent understands, which keeps each
/// log event together with its severity, its structured properties and its trace id.
/// Only wired up when running on Cloud Run; local development keeps the human-readable formatter.
/// </summary>
public static class GcpLoggingExtensions
{
    /// <summary>
    /// Cloud Run sets this on every container instance as part of its runtime contract, so it is a
    /// reliable marker for "this process writes to the Cloud Run log agent" that needs no extra
    /// configuration in <c>the Cloud Run Terraform</c>.
    /// See https://cloud.google.com/run/docs/container-contract#services-env-vars.
    /// </summary>
    public const string CloudRunServiceVariable = "K_SERVICE";

    /// <summary>
    /// An entry is only linked to its trace when the trace name is fully qualified with the project
    /// id. <c>GCP__PROJECTID</c> is set by <c>the Cloud Run Terraform</c>; trace fields are omitted
    /// when it is missing.
    /// </summary>
    public const string ProjectIdConfigurationKey = "Gcp:ProjectId";

    public static bool IsCloudRun(IConfiguration configuration)
    {
        return !string.IsNullOrWhiteSpace(configuration[CloudRunServiceVariable]);
    }

    public static TBuilder AddGcpStructuredLogging<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        if (!IsCloudRun(builder.Configuration))
            return builder;

        var projectId = builder.Configuration[ProjectIdConfigurationKey];
        builder.Logging.AddGoogleCloudConsole(options => ConfigureFormatter(options, projectId));

        return builder;
    }

    /// <summary>
    /// Kept separate from the registration so the formatter tests exercise the same options the API
    /// actually runs with.
    /// </summary>
    public static void ConfigureFormatter(GoogleCloudConsoleFormatterOptions options, string? projectId)
    {
        // Logging scopes carry the ambient request/message context (RequestPath, Wolverine message
        // id, session id, ...) — worth having as searchable fields in Cloud Logging.
        options.IncludeScopes = true;

        // Numbers in message templates stay JSON numbers instead of strings, so log-based metrics
        // and range filters work on them.
        options.EmitNumericTypes = true;

        options.TraceGoogleCloudProjectId = projectId;

        options.LogAugmenter = new ErrorReportingLogAugmenter();
    }
}

/// <summary>
/// The formatter puts an exception's stack trace in its own "exception" field, which Error
/// Reporting does not look at. Tagging the entry as a ReportedErrorEvent makes Error Reporting pick
/// failures up and group them by stack trace, which is the difference between seeing a crash once
/// and seeing "this has happened 240 times since the deploy".
/// </summary>
public sealed class ErrorReportingLogAugmenter : IGoogleCloudConsoleLogAugmenter
{
    public const string ReportedErrorEventType =
        "type.googleapis.com/google.devtools.clouderrorreporting.v1beta1.ReportedErrorEvent";

    public void AugmentFormattedLogEntry<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider scopeProvider,
        Utf8JsonWriter writer)
    {
        if (logEntry.Exception is null || logEntry.LogLevel < LogLevel.Error)
            return;

        writer.WriteString("@type", ReportedErrorEventType);
    }
}
