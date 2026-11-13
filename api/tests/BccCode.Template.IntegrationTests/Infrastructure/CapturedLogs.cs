using Microsoft.Extensions.Logging;

namespace BccCode.Template.IntegrationTests.Infrastructure;

/// <summary>Keeps the formatted log lines of a host, so a test can see that a handler ran.</summary>
public sealed class CapturedLogs : ILoggerProvider
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines
    {
        get { lock (_lines) return [.. _lines]; }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new Capture(this);
    }

    public void Dispose()
    {
    }

    private sealed class Capture(CapturedLogs owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner._lines) owner._lines.Add(formatter(state, exception));
        }
    }
}
