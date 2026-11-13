using OpenTelemetry.Context.Propagation;

namespace BccCode.Template.ServiceDefaults;

/// <summary>
/// W3C baggage, sent to BCC hosts only and never read from a request (standard 22.5). Baggage carries the sign-in session
/// id (<c>session.id</c>), which lets the other BCC services join their traces to ours, and must not reach a third party
/// such as Google's APIs. A carrier that is not an HTTP request to a BCC host gets no baggage at all. A caller's baggage is
/// dropped: anyone can send the header, so forwarding it would let a client put someone else's session id on our calls.
/// What we send is what this service sets (SessionTelemetry in the host). Trace context (traceparent, tracestate) still goes
/// everywhere; it identifies nobody.
/// </summary>
public sealed class BccBaggagePropagator : TextMapPropagator
{
    private readonly BaggagePropagator _inner = new();

    public override ISet<string> Fields => _inner.Fields;

    public static bool IsBccHost(string host)
    {
        return host.Equals("bcc.no", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".bcc.no", StringComparison.OrdinalIgnoreCase);
    }

    public override void Inject<T>(PropagationContext context, T carrier, Action<T, string, string> setter)
    {
        if (carrier is HttpRequestMessage { RequestUri.IsAbsoluteUri: true } request && IsBccHost(request.RequestUri.Host))
            _inner.Inject(context, carrier, setter);
    }

    // The ASP.NET Core instrumentation makes the extracted baggage Baggage.Current, which the HttpClient instrumentation
    // sends on. Returning the context unchanged keeps a caller's baggage out of it.
    public override PropagationContext Extract<T>(PropagationContext context, T carrier, Func<T, string, IEnumerable<string>?> getter)
    {
        return context;
    }
}
