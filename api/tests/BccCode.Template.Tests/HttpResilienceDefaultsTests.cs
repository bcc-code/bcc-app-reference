using System.Net;
using BccCode.Template.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Polly.CircuitBreaker;

namespace BccCode.Template.Tests;

/// <summary>
/// The BCC platform clients (Core API, PubSub, ...) share the default HttpClient name. Their resilience pipeline must be per
/// host, or the failures of one service open the circuit for the others.
/// </summary>
public class HttpResilienceDefaultsTests
{
    private const string CLIENT_NAME = "Default";

    [Fact]
    public async Task Failures_of_one_host_do_not_open_the_circuit_for_another_host_on_the_same_client()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        builder.Services.AddHttpClient(CLIENT_NAME).ConfigurePrimaryHttpMessageHandler(() => new HostHandler());
        builder.Services.ConfigureAll<HttpStandardResilienceOptions>(o =>
        {
            o.Retry.MaxRetryAttempts = 1;
            o.Retry.Delay = TimeSpan.Zero;
            o.CircuitBreaker.MinimumThroughput = 2;
            o.CircuitBreaker.FailureRatio = 0.5;
        });
        using var host = builder.Build();
        var client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient(CLIENT_NAME);

        Func<Task> down = () => client.GetAsync("http://down.test/publish", TestContext.Current.CancellationToken);
        for (var i = 0; i < 4; i++)
            await down.Should().ThrowAsync<Exception>();
        await down.Should().ThrowAsync<BrokenCircuitException>("the circuit for the failing host is open");

        var response = await client.GetAsync("http://up.test/persons", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed class HostHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return request.RequestUri!.Host == "down.test"
                ? throw new HttpRequestException("Connection refused (down.test)")
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
