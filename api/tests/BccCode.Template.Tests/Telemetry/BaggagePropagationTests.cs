using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using BccCode.Template.Api.Auth;
using BccCode.Template.Security;
using BccCode.Template.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Trace;

namespace BccCode.Template.Tests.Telemetry;

// Standard 22.5: baggage (the sign-in session id) reaches BCC services only, and only the one we set; trace context goes
// everywhere.
public sealed class BaggagePropagationTests
{
    private const string SID_HEADER = "X-Test-Sid";

    [Theory]
    [InlineData("https://core.api.sandbox.bcc.no/v2/persons", true)]
    [InlineData("https://login.bcc.no/oauth/token", true)]
    [InlineData("https://bcc.no/", true)]
    [InlineData("https://pubsub.googleapis.com/v1/projects/p/topics/t:publish", false)]
    [InlineData("https://notbcc.no/", false)]
    [InlineData("https://bcc.no.example.com/", false)]
    public void Baggage_is_written_for_bcc_hosts_only(string url, bool written)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        new BccBaggagePropagator().Inject(new PropagationContext(default, Session()), request, (r, key, value) => r.Headers.Add(key, value));

        request.Headers.Contains("baggage").Should().Be(written);
    }

    [Fact]
    public void A_carrier_that_is_not_an_http_request_gets_no_baggage()
    {
        var carrier = new Dictionary<string, string>();

        new BccBaggagePropagator().Inject(new PropagationContext(default, Session()), carrier, (c, key, value) => c[key] = value);

        carrier.Should().BeEmpty();
    }

    [Fact]
    public void A_caller_s_baggage_is_not_extracted()
    {
        var headers = new Dictionary<string, string> { ["baggage"] = "session.id=forged" };

        var context = new BccBaggagePropagator().Extract(default, headers, (h, key) => h.TryGetValue(key, out var value) ? [value] : null);

        context.Baggage.Count.Should().Be(0);
    }

    [Fact]
    public async Task Through_HttpClient_a_BCC_service_gets_the_session_id_and_Google_gets_trace_context_only()
    {
        // The propagators and the runtime's propagator are set by AddServiceDefaults; the tracer provider turns on the
        // HttpClient instrumentation that writes the headers.
        using var app = WebApplication.CreateBuilder().AddServiceDefaults().Build();
        app.Services.GetRequiredService<TracerProvider>();
        await using var downstream = await Downstream.StartAsync();
        using var client = downstream.Client();

        using var request = new Activity("request").Start();
        request.ActivityTraceFlags = ActivityTraceFlags.Recorded;
        // What ASP.NET Core keeps from a caller's baggage header. The runtime would forward it to any host.
        request.AddBaggage("from.caller", "x");
        Baggage.Current = Session();

        await client.GetAsync($"http://core.api.sandbox.bcc.no:{downstream.Port}/", TestContext.Current.CancellationToken);
        await client.GetAsync($"http://pubsub.googleapis.com:{downstream.Port}/", TestContext.Current.CancellationToken);

        var bcc = downstream.Received["core.api.sandbox.bcc.no/"];
        bcc["baggage"].Should().Be("session.id=sid-1");
        bcc["traceparent"].Should().StartWith($"00-{request.TraceId}-");
        bcc["tracestate"].Should().StartWith("bcc=");

        var google = downstream.Received["pubsub.googleapis.com/"];
        google.ContainsKey("baggage").Should().BeFalse();
        google["traceparent"].Should().StartWith($"00-{request.TraceId}-");
    }

    [Fact]
    public async Task A_caller_s_baggage_is_dropped_and_a_signed_in_request_sends_its_own_session_id_only()
    {
        await using var downstream = await Downstream.StartAsync();

        // The real path: the ASP.NET Core instrumentation reads the request's headers through the propagators, and
        // SessionTelemetry adds the session id.
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddServiceDefaults();
        // After AddServiceDefaults, as in the API: a handler keeps the runtime's propagator it was created with, and the
        // default one forwards the baggage ASP.NET Core keeps on the request activity.
        using var client = downstream.Client();
        await using var api = builder.Build();
        // Stands in for the cookie scheme: a request with the header is signed in with that Signon sid.
        api.Use((context, next) =>
        {
            if (context.Request.Headers.TryGetValue(SID_HEADER, out var sid))
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(AuthClaims.SessionId, sid.ToString())], "Test"));
            return next(context);
        });
        api.UseTemplateSessionTelemetry();
        api.MapGet("/{name}", async (string name) =>
        {
            using var response = await client.GetAsync($"http://core.api.sandbox.bcc.no:{downstream.Port}/{name}");
            response.EnsureSuccessStatusCode();
        });
        await api.StartAsync(TestContext.Current.CancellationToken);
        using var caller = new HttpClient { BaseAddress = new Uri(api.Urls.Single()) };

        await SendWithForgedBaggageAsync(caller, "/anonymous", sid: null);
        await SendWithForgedBaggageAsync(caller, "/signed-in", sid: "sid-1");

        downstream.Received["core.api.sandbox.bcc.no/anonymous"].ContainsKey("baggage").Should().BeFalse();
        downstream.Received["core.api.sandbox.bcc.no/signed-in"]["baggage"].Should().Be("session.id=sid-1");
    }

    private static async Task SendWithForgedBaggageAsync(HttpClient caller, string path, string? sid)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        // What any client can send: a session id of its choosing and keys of its own.
        request.Headers.Add("baggage", "session.id=forged,from.caller=x");
        if (sid is not null) request.Headers.Add(SID_HEADER, sid);
        using var response = await caller.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static Baggage Session()
    {
        return Baggage.Create(new Dictionary<string, string> { ["session.id"] = "sid-1" });
    }

    // A loopback server that keeps the headers of each request, by host and path.
    private sealed class Downstream(WebApplication server, ConcurrentDictionary<string, Dictionary<string, string>> received) : IAsyncDisposable
    {
        public ConcurrentDictionary<string, Dictionary<string, string>> Received => received;

        public int Port => new Uri(server.Urls.Single()).Port;

        public static async Task<Downstream> StartAsync()
        {
            var received = new ConcurrentDictionary<string, Dictionary<string, string>>();
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var server = builder.Build();
            server.Run(context =>
            {
                // Kestrel reuses the header collection once the request is done, so keep a copy.
                received[$"{context.Request.Host.Host}{context.Request.Path}"] =
                    context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
                return Task.CompletedTask;
            });
            await server.StartAsync(TestContext.Current.CancellationToken);
            return new Downstream(server, received);
        }

        // Every connection goes to this server whatever the host name, so it sees what each host would get.
        public HttpClient Client()
        {
            var port = Port;
            return new HttpClient(new SocketsHttpHandler
            {
                ConnectCallback = async (_, cancellationToken) =>
                {
                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                    await socket.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                },
            });
        }

        public ValueTask DisposeAsync()
        {
            return server.DisposeAsync();
        }
    }
}
