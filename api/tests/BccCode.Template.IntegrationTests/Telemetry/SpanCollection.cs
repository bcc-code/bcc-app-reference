using System.Collections;
using System.Diagnostics;

namespace BccCode.Template.IntegrationTests.Telemetry;

/// <summary>
/// What a test host's in-memory exporter receives. The exporter adds from the request threads, and the server span ends
/// after the response is sent, so reads take a copy under the lock.
/// </summary>
public sealed class SpanCollection : ICollection<Activity>
{
    private readonly List<Activity> _spans = [];

    public int Count
    {
        get
        {
            lock (_spans) return _spans.Count;
        }
    }

    public bool IsReadOnly => false;

    public Activity[] InTrace(ActivityTraceId traceId)
    {
        lock (_spans) return [.. _spans.Where(s => s.TraceId == traceId)];
    }

    /// <summary>The trace's spans once its server span has ended, which happens after the test has its response.</summary>
    public async Task<Activity[]> TraceAsync(ActivityTraceId traceId)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var spans = InTrace(traceId);
            if (spans.Any(s => s.Kind == ActivityKind.Server)) return spans;
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"No server span in trace {traceId} after 5 seconds.");
    }

    public void Add(Activity item)
    {
        lock (_spans) _spans.Add(item);
    }

    public void Clear()
    {
        lock (_spans) _spans.Clear();
    }

    public bool Contains(Activity item)
    {
        lock (_spans) return _spans.Contains(item);
    }

    public void CopyTo(Activity[] array, int arrayIndex)
    {
        lock (_spans) _spans.CopyTo(array, arrayIndex);
    }

    public bool Remove(Activity item)
    {
        lock (_spans) return _spans.Remove(item);
    }

    public IEnumerator<Activity> GetEnumerator()
    {
        lock (_spans) return _spans.ToList().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
