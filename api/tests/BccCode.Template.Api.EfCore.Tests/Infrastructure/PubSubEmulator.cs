using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging;

namespace BccCode.Template.Api.EfCore.Tests.Infrastructure;

/// <summary>
/// The GCP Pub/Sub emulator, the image the AppHost runs. Wolverine and the Google clients find it through the
/// <c>PUBSUB_EMULATOR_HOST</c> environment variable, which is set for the whole test process while it runs. The emulator pushes
/// nothing here (no push subscription exists), so tests pull what a host published from a subscription of their own and post it
/// to the push receiver as Pub/Sub would.
/// </summary>
public sealed class PubSubEmulator : IAsyncDisposable
{
    public const string ProjectId = "template-test";
    private const string HostVariable = "PUBSUB_EMULATOR_HOST";

    private readonly IContainer _container = new ContainerBuilder(
            "gcr.io/google.com/cloudsdktool/google-cloud-cli:587.0.0-emulators@sha256:8d8573471c489a409f03891d2c24d177fa531276184f7002b9a2d853d1b3ed54")
        .WithPortBinding(8085, assignRandomHostPort: true)
        .WithCommand("gcloud", "beta", "emulators", "pubsub", "start", "--host-port=0.0.0.0:8085", $"--project={ProjectId}")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server started"))
        .Build();

    public async Task StartAsync()
    {
        await _container.StartAsync();
        Environment.SetEnvironmentVariable(HostVariable, $"{_container.Hostname}:{_container.GetMappedPublicPort(8085)}");
    }

    public async ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable(HostVariable, null);
        await _container.DisposeAsync();
    }
}

/// <summary>Keeps the formatted log lines of a host, so a test can see that a handler ran.</summary>
public sealed class CapturedLogs : ILoggerProvider
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines
    {
        get { lock (_lines) return [.. _lines]; }
    }

    public ILogger CreateLogger(string categoryName) => new Capture(this);

    public void Dispose()
    {
    }

    private sealed class Capture(CapturedLogs owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner._lines) owner._lines.Add(formatter(state, exception));
        }
    }
}
