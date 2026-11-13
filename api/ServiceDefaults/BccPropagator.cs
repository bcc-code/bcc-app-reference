using System.Diagnostics;
using OpenTelemetry.Context.Propagation;

namespace BccCode.Template.ServiceDefaults;

/// <summary>
/// W3C trace context plus a <c>bcc=&lt;span id&gt;</c> entry in <c>tracestate</c> (standard 22.5, from bcc-monitoring's
/// docs/configuration.md). Google's load balancer replaces the parent span id in <c>traceparent</c> with a span of its own
/// that never reaches the BCC collector, but it passes <c>tracestate</c> on. The entry carries the caller's real span, so
/// a call from another BCC service stays one trace tree.
/// </summary>
public sealed class BccPropagator : TextMapPropagator
{
    public const string TraceStateKey = "bcc";

    private readonly TraceContextPropagator _inner = new();

    public override ISet<string> Fields => _inner.Fields;

    public override void Inject<T>(PropagationContext context, T carrier, Action<T, string, string> setter)
    {
        var activityContext = context.ActivityContext;
        if (activityContext == default)
        {
            _inner.Inject(context, carrier, setter);
            return;
        }

        var traceState = WithEntry(activityContext.TraceState, activityContext.SpanId.ToHexString());
        var withBcc = new ActivityContext(activityContext.TraceId, activityContext.SpanId, activityContext.TraceFlags, traceState, activityContext.IsRemote);
        _inner.Inject(new PropagationContext(withBcc, context.Baggage), carrier, setter);
    }

    public override PropagationContext Extract<T>(PropagationContext context, T carrier, Func<T, string, IEnumerable<string>?> getter)
    {
        var extracted = _inner.Extract(context, carrier, getter);
        var activityContext = extracted.ActivityContext;
        // A missing or malformed entry leaves the parent from traceparent.
        if (activityContext == default || !TryReadEntry(activityContext.TraceState, out var bccSpanId)) return extracted;

        var parent = new ActivityContext(activityContext.TraceId, bccSpanId, activityContext.TraceFlags, activityContext.TraceState, activityContext.IsRemote);
        return new PropagationContext(parent, extracted.Baggage);
    }

    // Ours goes first, any older bcc entry is dropped, the other vendors' entries stay in their order.
    private static string WithEntry(string? traceState, string spanId)
    {
        var others = (traceState ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(e => !e.StartsWith($"{TraceStateKey}=", StringComparison.Ordinal));
        return string.Join(",", others.Prepend($"{TraceStateKey}={spanId}"));
    }

    private static bool TryReadEntry(string? traceState, out ActivitySpanId spanId)
    {
        spanId = default;
        var value = (traceState ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(e => e.StartsWith($"{TraceStateKey}=", StringComparison.Ordinal))?[(TraceStateKey.Length + 1)..];
        // A span id is 16 lowercase hex digits and not all zero (W3C trace context). CreateFromString throws on anything else.
        if (value is not { Length: 16 } || !value.All(char.IsAsciiHexDigitLower) || value.All(c => c == '0')) return false;

        spanId = ActivitySpanId.CreateFromString(value);
        return true;
    }
}
