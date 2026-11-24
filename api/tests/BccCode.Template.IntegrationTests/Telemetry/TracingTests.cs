using System.Diagnostics;
using BccCode.Template.Api.Auth;
using BccCode.Template.IntegrationTests.Infrastructure;
using OpenTelemetry.Trace;

namespace BccCode.Template.IntegrationTests.Telemetry;

/// <summary>
/// The spans the API exports (standard 22), from a host with an in-memory exporter next to the OTLP one. Each request sends a
/// traceparent the way Google's front end does, marked not sampled, and its trace is looked up by id.
/// </summary>
public sealed class TracingTests(AppFixture fixture) : AppTest(fixture)
{
    private readonly SpanCollection _spans = [];
    private IAlbaHost _host = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _host = await Fixture.StartHostAsync(services: s => s.ConfigureOpenTelemetryTracerProvider(t => t.AddInMemoryExporter(_spans)));
    }

    public override async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task A_request_marked_not_sampled_upstream_is_one_trace_with_its_database_spans_and_the_signon_session_id()
    {
        var user = TestUser.New();
        var (trace, upstream) = NewTrace();

        await _host.Scenario(s =>
        {
            s.As(user);
            s.WithRequestHeader("traceparent", $"00-{trace}-{upstream}-00");
            s.Get.Url("/api/notes");
        });

        var spans = await _spans.TraceAsync(trace);
        var request = spans.Should().ContainSingle(s => s.Kind == ActivityKind.Server).Subject;
        request.ParentSpanId.Should().Be(upstream);
        request.GetTagItem(SessionTelemetry.SessionIdKey).Should().Be(TestUser.SidOf(user.PersonUid.ToString()));
        spans.Should().Contain(s => s.GetTagItem("db.system.name") as string == "postgresql", "the Npgsql spans come from the data source");
    }

    [Fact]
    public async Task A_caller_s_bcc_tracestate_entry_becomes_the_parent_in_place_of_the_load_balancer_s_span()
    {
        var (trace, loadBalancer) = NewTrace();
        var caller = ActivitySpanId.CreateRandom();

        await _host.Scenario(s =>
        {
            s.WithRequestHeader("traceparent", $"00-{trace}-{loadBalancer}-01");
            s.WithRequestHeader("tracestate", $"bcc={caller}");
            s.Get.Url("/api/me");
            s.StatusCodeShouldBe(401);
        });

        var request = (await _spans.TraceAsync(trace)).Should().ContainSingle(s => s.Kind == ActivityKind.Server).Subject;
        request.ParentSpanId.Should().Be(caller);
    }

    [Fact]
    public async Task A_request_without_a_signed_in_session_carries_no_session_id()
    {
        var (trace, upstream) = NewTrace();

        await _host.Scenario(s =>
        {
            s.WithRequestHeader("traceparent", $"00-{trace}-{upstream}-00");
            s.Get.Url("/api/me");
            s.StatusCodeShouldBe(401);
        });

        var request = (await _spans.TraceAsync(trace)).Should().ContainSingle(s => s.Kind == ActivityKind.Server).Subject;
        request.GetTagItem(SessionTelemetry.SessionIdKey).Should().BeNull();
    }

    [Fact]
    public async Task Health_and_alive_leave_no_spans_not_even_the_database_check()
    {
        var (probes, upstream) = NewTrace();
        await _host.Scenario(s =>
        {
            s.WithRequestHeader("traceparent", $"00-{probes}-{upstream}-00");
            s.Get.Url("/health");
        });
        await _host.Scenario(s =>
        {
            s.WithRequestHeader("traceparent", $"00-{probes}-{upstream}-00");
            s.Get.Url("/alive");
        });

        // A request after the probes, whose trace shows the exporter got everything up to it.
        var (after, _) = NewTrace();
        await _host.Scenario(s =>
        {
            s.WithRequestHeader("traceparent", $"00-{after}-{upstream}-00");
            s.Get.Url("/api/me");
            s.StatusCodeShouldBe(401);
        });
        await _spans.TraceAsync(after);

        _spans.InTrace(probes).Should().BeEmpty();
    }

    private static (ActivityTraceId Trace, ActivitySpanId Upstream) NewTrace()
    {
        return (ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom());
    }
}
