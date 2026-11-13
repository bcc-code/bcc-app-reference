using BccCode.Template.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BccCode.Template.IntegrationTests.Hosting;

/// <summary>
/// The gateway shape is X-Forwarded-For "&lt;supplied&gt;,&lt;client&gt;,&lt;load balancer&gt;" and X-Forwarded-Proto "https".
/// Terraform sets ASPNETCORE_FORWARDEDHEADERS_ENABLED behind the gateway; the app only sets the limit.
/// </summary>
public class ForwardedHeadersTests(AppFixture fixture) : AppTest(fixture)
{
    [Fact]
    public void The_client_is_the_second_entry_from_the_right()
    {
        Fixture.Host.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value.ForwardLimit.Should().Be(2);
    }

    [Fact]
    public async Task The_sign_in_uses_the_forwarded_scheme_for_the_redirect_uri_when_the_switch_is_on()
    {
        var result = await Fixture.Host.Scenario(s =>
        {
            s.WithRequestHeader("X-Forwarded-For", "203.0.113.9, 10.1.2.3");
            s.WithRequestHeader("X-Forwarded-Proto", "https");
            s.Get.Url("/auth/login");
            s.StatusCodeShouldBe(302);
        });

        var query = QueryHelpers.ParseQuery(new Uri(result.Context.Response.Headers.Location.ToString()).Query);
        query["redirect_uri"].ToString().Should().StartWith("https://");
    }

    [Fact]
    public async Task The_host_ignores_forwarded_headers_when_the_switch_is_off()
    {
        await using var host = await Fixture.StartHostAsync(new Dictionary<string, string?> { ["FORWARDEDHEADERS_ENABLED"] = "false" });

        var result = await host.Scenario(s =>
        {
            s.WithRequestHeader("X-Forwarded-Proto", "https");
            s.Get.Url("/auth/login");
            s.StatusCodeShouldBe(302);
        });

        var query = QueryHelpers.ParseQuery(new Uri(result.Context.Response.Headers.Location.ToString()).Query);
        query["redirect_uri"].ToString().Should().StartWith("http://");
    }
}
