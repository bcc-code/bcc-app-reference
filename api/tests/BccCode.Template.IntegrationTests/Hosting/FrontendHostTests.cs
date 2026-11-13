using System.Text.RegularExpressions;
using BccCode.Template.IntegrationTests.Infrastructure;

namespace BccCode.Template.IntegrationTests.Hosting;

/// <summary>
/// The SPA shell (standard 16.2): an endpoint under the fallback policy, so a signed-out person is sent to the identity provider
/// before any JavaScript loads, and a signed-in one gets index.html with a fresh CSP nonce.
/// </summary>
public class FrontendHostTests(AppFixture fixture) : AppTest(fixture)
{

    [Theory]
    [InlineData("/")]
    [InlineData("/notes")]
    [InlineData("/index.html")]
    [InlineData("/INDEX.HTML")]
    public async Task A_signed_out_person_is_sent_straight_to_the_identity_provider(string path)
    {
        var result = await Host.Scenario(s => { s.Get.Url(path); s.WithRequestHeader("Accept", "text/html"); s.StatusCodeShouldBe(302); });

        result.Context.Response.Headers.Location.ToString().Should().StartWith(TestOidc.AuthorizationEndpoint);
    }

    [Fact]
    public async Task A_signed_in_person_gets_the_shell_with_a_nonce_on_every_script_and_style_and_in_the_csp()
    {
        var result = await Host.Scenario(s => { s.As(TestUser.New()); s.Get.Url("/notes/some-client-route"); s.WithRequestHeader("Accept", "text/html"); });

        var csp = result.Context.Response.Headers.ContentSecurityPolicy.ToString();
        var nonce = Regex.Match(csp, "'nonce-([^']+)'").Groups[1].Value;
        nonce.Should().NotBeEmpty();
        csp.Should().Contain($"script-src 'self' 'nonce-{nonce}'").And.Contain($"style-src 'self' 'nonce-{nonce}'").And.NotContain("unsafe-inline");
        var html = await result.ReadAsTextAsync();
        html.Should().Contain($"<script nonce=\"{nonce}\"").And.Contain($"<meta property=\"csp-nonce\" nonce=\"{nonce}\"").And.NotContain("__CSP_NONCE__");
        result.Context.Response.Headers.CacheControl.ToString().Should().Contain("no-store");
        result.Context.Response.ContentType.Should().StartWith("text/html");
    }

    [Fact]
    public async Task Each_response_has_a_nonce_of_its_own()
    {
        async Task<string> NonceAsync()
        {
            var result = await Host.Scenario(s => { s.As(TestUser.New()); s.Get.Url("/"); });
            return Regex.Match(result.Context.Response.Headers.ContentSecurityPolicy.ToString(), "'nonce-([^']+)'").Groups[1].Value;
        }

        (await NonceAsync()).Should().NotBe(await NonceAsync());
    }

    [Fact]
    public async Task The_built_index_html_is_never_served_as_a_static_file()
    {
        // As a file it would skip the session check and carry no nonce. A signed-in request for the file gets the rendered shell.
        var result = await Host.Scenario(s => { s.As(TestUser.New()); s.Get.Url("/index.html"); });

        (await result.ReadAsTextAsync()).Should().NotContain("__CSP_NONCE__");
        result.Context.Response.Headers.ContentSecurityPolicy.ToString().Should().Contain("'nonce-");
    }

    [Fact]
    public async Task A_missing_asset_is_a_404_and_not_the_shell()
    {
        await Host.Scenario(s => { s.As(TestUser.New()); s.Get.Url("/assets/missing.js"); s.StatusCodeShouldBe(404); });
    }

    [Fact]
    public async Task A_mistyped_api_path_is_a_404_problem_and_not_the_shell()
    {
        var result = await Host.Scenario(s => { s.As(TestUser.New()); s.Get.Url("/api/does-not-exist"); s.StatusCodeShouldBe(404); });

        result.Context.Response.ContentType.Should().StartWith("application/problem+json");
        await Host.Scenario(s => { s.Get.Url("/api/does-not-exist"); s.StatusCodeShouldBe(401); });
    }
}
