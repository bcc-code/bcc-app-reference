using System.Diagnostics;
using BccCode.Template.ServiceDefaults;
using OpenTelemetry.Context.Propagation;

namespace BccCode.Template.Tests.Telemetry;

public sealed class BccPropagatorTests
{
    private static readonly ActivityTraceId TRACE = ActivityTraceId.CreateFromString("0af7651916cd43dd8448eb211c80319c");
    private static readonly ActivitySpanId CALLER = ActivitySpanId.CreateFromString("b7ad6b7169203331");
    // The span Google's load balancer puts into traceparent; it never reaches the BCC collector.
    private const string LOAD_BALANCER_SPAN = "00f067aa0ba902b7";

    private readonly BccPropagator _propagator = new();

    [Fact]
    public void Inject_puts_our_span_first_in_tracestate_and_keeps_the_other_entries()
    {
        var context = new ActivityContext(TRACE, CALLER, ActivityTraceFlags.Recorded, "bcc=1111111111111111,congo=t61rcWkgMzE");

        var headers = Inject(context);

        headers["traceparent"].Should().Be($"00-{TRACE}-{CALLER}-01");
        headers["tracestate"].Should().Be($"bcc={CALLER},congo=t61rcWkgMzE");
    }

    [Fact]
    public void Inject_without_a_span_writes_nothing()
    {
        Inject(default).Should().BeEmpty();
    }

    [Fact]
    public void Extract_takes_the_parent_from_the_bcc_entry()
    {
        var context = Extract($"00-{TRACE}-{LOAD_BALANCER_SPAN}-01", $"congo=t61rcWkgMzE,bcc={CALLER}");

        context.TraceId.Should().Be(TRACE);
        context.SpanId.Should().Be(CALLER);
        context.IsRemote.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("congo=t61rcWkgMzE")]
    [InlineData("bcc=not-a-span-id")]
    [InlineData("bcc=B7AD6B7169203331")]
    [InlineData("bcc=0000000000000000")]
    [InlineData("bcc=")]
    public void Without_a_valid_bcc_entry_the_parent_comes_from_traceparent(string? traceState)
    {
        var context = Extract($"00-{TRACE}-{LOAD_BALANCER_SPAN}-01", traceState);

        context.TraceId.Should().Be(TRACE);
        context.SpanId.ToHexString().Should().Be(LOAD_BALANCER_SPAN);
    }

    [Fact]
    public void Without_traceparent_nothing_is_extracted()
    {
        Extract(traceParent: null, $"bcc={CALLER}").Should().Be(default(ActivityContext));
    }

    private Dictionary<string, string> Inject(ActivityContext context)
    {
        var headers = new Dictionary<string, string>();
        _propagator.Inject(new PropagationContext(context, default), headers, (h, key, value) => h[key] = value);
        return headers;
    }

    private ActivityContext Extract(string? traceParent, string? traceState)
    {
        var headers = new Dictionary<string, string>();
        if (traceParent is not null) headers["traceparent"] = traceParent;
        if (traceState is not null) headers["tracestate"] = traceState;
        return _propagator.Extract(default, headers, (h, key) => h.TryGetValue(key, out var value) ? [value] : null).ActivityContext;
    }
}
