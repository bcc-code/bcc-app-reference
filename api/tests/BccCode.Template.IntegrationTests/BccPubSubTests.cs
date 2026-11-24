using BccCode.Template.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BccCode.Template.IntegrationTests;

/// <summary>BCC PubSub (standard 13, integration events) is off by default and, when it is switched on, maps its delivery endpoint.</summary>
public class BccPubSubTests(AppFixture fixture) : AppTest(fixture)
{
    private static readonly Dictionary<string, string?> Platform = new()
    {
        ["BccPubSub:Enabled"] = "true",
        ["BccPlatform:Authority"] = "https://signon.invalid",
        ["BccPlatform:Audience"] = "audience",
        ["BccPlatform:ClientId"] = "client",
        ["BccPlatform:ClientSecret"] = "not-a-real-secret",
        ["BccPlatform:PubSubUrl"] = "https://pubsub.invalid",
        ["BccPlatform:PublicBaseUrl"] = "https://template.invalid",
    };

    private static List<string> Routes(IAlbaHost host)
    {
        return host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().Select(e => e.RoutePattern.RawText!).ToList();
    }

    [Fact]
    public void It_is_off_by_default_and_maps_nothing()
    {
        Routes(Fixture.Host).Should().NotContain(route => route.StartsWith("/_bccpubsub", StringComparison.Ordinal))
            .And.NotContain("/internal/bcc-pubsub/subscriptions");
    }

    [Fact]
    public async Task When_it_is_switched_on_the_delivery_endpoint_and_the_subscription_route_exist_and_refuse_a_call_without_a_token()
    {
        await using var host = await Fixture.StartHostAsync(Platform);

        Routes(host).Should().Contain(route => route.StartsWith("/_bccpubsub", StringComparison.Ordinal)).And.Contain("/internal/bcc-pubsub/subscriptions");
        await host.Scenario(s => { s.Post.Text("{}").ToUrl("/_bccpubsub/no.bcc.template.sample.v1"); s.StatusCodeShouldBe(401); });
    }
}
