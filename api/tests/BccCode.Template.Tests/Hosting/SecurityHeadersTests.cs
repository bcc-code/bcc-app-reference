using BccCode.Template.Api.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;

namespace BccCode.Template.Tests.Hosting;

/// <summary>
/// The full host only runs as Development or Test in tests (outside them, Wolverine needs pre-generated code), so the HSTS switch is
/// tested on a bare app with only the security headers middleware.
/// </summary>
public sealed class SecurityHeadersTests
{
    private static async Task<HttpResponseMessage> GetAsync(string environment, string url)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.AddTemplateSecurityHeaders();
        var app = builder.Build();
        app.UseTemplateSecurityHeaders();
        app.MapGet("/", () => "ok");
        app.MapGet("/api/x", () => "ok");
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        return await client.GetAsync(url, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("Production", "https://example.com/", "max-age=31536000")]
    [InlineData("Staging", "https://example.com/", "max-age=31536000")]
    [InlineData("Production", "http://example.com/", null)]
    [InlineData("Development", "https://example.com/", null)]
    public async Task Hsts_is_sent_on_https_outside_Development_only(string environment, string url, string? expected)
    {
        using var response = await GetAsync(environment, url);

        var hsts = response.Headers.TryGetValues("Strict-Transport-Security", out var values) ? values.Single() : null;
        hsts.Should().Be(expected);
    }

    [Fact]
    public async Task Every_response_has_the_baseline_headers_and_a_csp_that_lets_nothing_load()
    {
        using var response = await GetAsync("Production", "http://example.com/");

        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
        response.Headers.GetValues("Content-Security-Policy").Single().Should().StartWith("default-src 'none'");
        response.Headers.GetValues("Permissions-Policy").Single().Should().Contain("camera=()").And.Contain("geolocation=()");
        response.Headers.Contains("Server").Should().BeFalse();
    }

    [Fact]
    public async Task An_api_answer_is_not_cached_unless_the_endpoint_says_so()
    {
        using var api = await GetAsync("Production", "http://example.com/api/x");
        using var page = await GetAsync("Production", "http://example.com/");

        api.Headers.CacheControl!.NoStore.Should().BeTrue();
        page.Headers.CacheControl.Should().BeNull();
    }
}
