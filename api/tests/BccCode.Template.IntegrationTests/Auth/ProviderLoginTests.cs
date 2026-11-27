using BccCode.Template.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BccCode.Template.IntegrationTests.Auth;

/// <summary>
/// Signing in with BCC Signon, Entra or both (ADR 0031): the routes under /auth and where a request without a session goes. The
/// fixture's own host has Signon only; the tests that need another combination start a host with it.
/// </summary>
public class ProviderLoginTests(AppFixture fixture) : AppTest(fixture)
{
    private static string LocationOf(IScenarioResult result)
    {
        return result.Context.Response.Headers.Location.ToString();
    }

    private static Dictionary<string, string> QueryOf(string location)
    {
        return QueryHelpers.ParseQuery(new Uri(location, UriKind.RelativeOrAbsolute).IsAbsoluteUri ? new Uri(location).Query : location[location.IndexOf('?')..])
            .ToDictionary(p => p.Key, p => p.Value.ToString());
    }

    private static string StateRedirectUri(IAlbaHost host, string scheme, string location)
    {
        // The OIDC state parameter carries the AuthenticationProperties the callback redirects with.
        var options = host.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(scheme);
        return options.StateDataFormat.Unprotect(QueryOf(location)["state"])!.RedirectUri!;
    }

    private static async Task<IScenarioResult> GetAsync(IAlbaHost host, string url, int status = 302)
    {
        return await host.Scenario(s => { s.Get.Url(url); s.StatusCodeShouldBe(status); });
    }

    [Theory]
    [InlineData("bcc-signon", "OpenIdConnect", TestOidc.AuthorizationEndpoint, "template", "/signin-oidc")]
    [InlineData("entra", "Entra", TestEntra.AuthorizationEndpoint, TestEntra.ClientId, "/signin-entra")]
    public async Task A_provider_route_redirects_to_that_provider_with_a_code_flow_and_a_pkce_challenge(
        string provider, string scheme, string authorizationEndpoint, string clientId, string callback)
    {
        await using var host = await Fixture.StartProviderHostAsync(signon: true, entra: true);

        var result = await GetAsync(host, $"/auth/login/{provider}?returnUrl=%2Fnotes");

        var location = LocationOf(result);
        location.Should().StartWith(authorizationEndpoint);
        var query = QueryOf(location);
        (query["response_type"], query["client_id"], query["code_challenge_method"]).Should().Be(("code", clientId, "S256"));
        query["code_challenge"].Should().NotBeEmpty();
        query["scope"].Should().Be("openid profile", "no offline_access and no second-factor scope on the first leg");
        new Uri(query["redirect_uri"]).AbsolutePath.Should().Be(callback);
        StateRedirectUri(host, scheme, location).Should().Be("/notes");
    }

    // The values are the query string as sent, so "%09" reaches the endpoint as a tab.
    [Theory]
    [InlineData("bcc-signon", "OpenIdConnect", "https%3A%2F%2Fevil.example")]
    [InlineData("bcc-signon", "OpenIdConnect", "%2F%2Fevil.example")]
    [InlineData("entra", "Entra", "https%3A%2F%2Fevil.example")]
    [InlineData("entra", "Entra", "%2F%2Fevil.example")]
    [InlineData("entra", "Entra", "%2F%5Cevil.example")]
    [InlineData("entra", "Entra", "%2F%09%2Fevil.example")]
    public async Task A_provider_route_with_a_return_URL_that_is_not_local_ends_at_the_root(string provider, string scheme, string returnUrl)
    {
        await using var host = await Fixture.StartProviderHostAsync(signon: true, entra: true);

        var result = await GetAsync(host, $"/auth/login/{provider}?returnUrl={returnUrl}");

        StateRedirectUri(host, scheme, LocationOf(result)).Should().Be("/");
    }

    [Fact]
    public async Task With_both_providers_login_sends_the_person_to_the_sign_in_page_with_the_return_URL()
    {
        await using var host = await Fixture.StartProviderHostAsync(signon: true, entra: true);

        var result = await GetAsync(host, "/auth/login?returnUrl=%2Fnotes%3Fa%3D1");

        LocationOf(result).Should().Be("/auth/sign-in?returnUrl=%2Fnotes%3Fa%3D1");
    }

    [Fact]
    public async Task With_both_providers_login_with_a_return_URL_that_is_not_local_goes_to_the_sign_in_page_for_the_root()
    {
        await using var host = await Fixture.StartProviderHostAsync(signon: true, entra: true);

        var result = await GetAsync(host, "/auth/login?returnUrl=https%3A%2F%2Fevil.example");

        LocationOf(result).Should().Be("/auth/sign-in?returnUrl=%2F");
    }

    [Fact]
    public async Task The_sign_in_page_has_one_link_per_provider_with_the_safe_return_URL_and_is_not_cached()
    {
        await using var host = await Fixture.StartProviderHostAsync(signon: true, entra: true);

        var result = await GetAsync(host, "/auth/sign-in?returnUrl=%2Fnotes%3Fa%3D1%26b%3D2", status: 200);

        result.Context.Response.Headers.CacheControl.ToString().Should().Be("no-store");
        result.Context.Response.ContentType.Should().StartWith("text/html");
        var body = await result.ReadAsTextAsync();
        body.Should().Contain("href=\"/auth/login/bcc-signon?returnUrl=%2Fnotes%3Fa%3D1%26b%3D2\"")
            .And.Contain("href=\"/auth/login/entra?returnUrl=%2Fnotes%3Fa%3D1%26b%3D2\"")
            .And.NotContain("<script").And.NotContain("<style");
    }

    [Theory]
    [InlineData("https%3A%2F%2Fevil.example")]
    [InlineData("%2F%2Fevil.example")]
    public async Task The_sign_in_page_links_carry_the_root_when_the_return_URL_is_not_local(string returnUrl)
    {
        await using var host = await Fixture.StartProviderHostAsync(signon: true, entra: true);

        var body = await (await GetAsync(host, $"/auth/sign-in?returnUrl={returnUrl}", status: 200)).ReadAsTextAsync();

        body.Should().Contain("href=\"/auth/login/bcc-signon?returnUrl=%2F\"").And.Contain("href=\"/auth/login/entra?returnUrl=%2F\"").And.NotContain("evil.example");
    }

    [Fact]
    public async Task A_return_URL_cannot_break_out_of_the_link_it_is_in()
    {
        await using var host = await Fixture.StartProviderHostAsync(signon: true, entra: true);

        var body = await (await GetAsync(host, "/auth/sign-in?returnUrl=%2Fx%22%3E%3Cscript%3Ealert(1)%3C%2Fscript%3E", status: 200)).ReadAsTextAsync();

        body.Should().NotContain("<script").And.NotContain("\"><script").And.Contain("%3Cscript%3E");
    }

    [Fact]
    public async Task With_one_provider_the_sign_in_page_goes_straight_to_login()
    {
        // The fixture's host has Signon only.
        var signonOnly = await GetAsync(Host, "/auth/sign-in?returnUrl=%2Fnotes");
        await using var entraOnly = await Fixture.StartProviderHostAsync(signon: false, entra: true);
        var entra = await GetAsync(entraOnly, "/auth/sign-in?returnUrl=%2Fnotes");

        LocationOf(signonOnly).Should().Be("/auth/login?returnUrl=%2Fnotes");
        LocationOf(entra).Should().Be("/auth/login?returnUrl=%2Fnotes");
    }

    [Fact]
    public async Task A_provider_that_is_unknown_or_switched_off_is_404()
    {
        await GetAsync(Host, "/auth/login/entra", status: 404);   // Signon only
        await GetAsync(Host, "/auth/login/unknown", status: 404);
        await using var entraOnly = await Fixture.StartProviderHostAsync(signon: false, entra: true);
        await GetAsync(entraOnly, "/auth/login/bcc-signon", status: 404);
        await GetAsync(entraOnly, "/auth/login/unknown", status: 404);
    }

    [Fact]
    public async Task With_one_provider_login_goes_to_that_provider()
    {
        await using var entraOnly = await Fixture.StartProviderHostAsync(signon: false, entra: true);

        var entra = await GetAsync(entraOnly, "/auth/login?returnUrl=%2Fnotes");
        var signon = await GetAsync(Host, "/auth/login/bcc-signon?returnUrl=%2Fnotes");

        LocationOf(entra).Should().StartWith(TestEntra.AuthorizationEndpoint);
        StateRedirectUri(entraOnly, "Entra", LocationOf(entra)).Should().Be("/notes");
        LocationOf(signon).Should().StartWith(TestOidc.AuthorizationEndpoint);
    }

    [Fact]
    public async Task A_navigation_without_a_session_goes_straight_to_the_only_provider()
    {
        await using var entraOnly = await Fixture.StartProviderHostAsync(signon: false, entra: true);

        LocationOf(await GetAsync(Host, "/")).Should().StartWith(TestOidc.AuthorizationEndpoint);
        LocationOf(await GetAsync(entraOnly, "/")).Should().StartWith(TestEntra.AuthorizationEndpoint);
    }

    [Fact]
    public async Task With_both_providers_a_navigation_without_a_session_goes_to_the_chooser_with_the_page_asked_for()
    {
        await using var host = await Fixture.StartProviderHostAsync(signon: true, entra: true);

        LocationOf(await GetAsync(host, "/")).Should().Be("/auth/sign-in?returnUrl=%2F");
        LocationOf(await GetAsync(host, "/notes?x=1")).Should().Be("/auth/sign-in?returnUrl=%2Fnotes%3Fx%3D1");
    }

    [Fact]
    public async Task The_api_still_answers_401_without_a_redirect_with_each_combination_of_providers()
    {
        await using var both = await Fixture.StartProviderHostAsync(signon: true, entra: true);
        await using var entraOnly = await Fixture.StartProviderHostAsync(signon: false, entra: true);

        foreach (var host in new[] { Host, both, entraOnly })
        {
            var result = await host.Scenario(s => { s.Get.Url("/api/notes"); s.StatusCodeShouldBe(401); });

            result.Context.Response.Headers.Location.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Logout_signs_out_of_the_provider_the_session_came_from()
    {
        await using var both = await Fixture.StartProviderHostAsync(signon: true, entra: true);

        var entra = await both.Scenario(s => { s.As(TestUser.NewEntra()); s.AsBrowser(); s.Post.Url("/auth/logout"); s.StatusCodeShouldBe(302); });
        var signon = await both.Scenario(s => { s.As(TestUser.New()); s.AsBrowser(); s.Post.Url("/auth/logout"); s.StatusCodeShouldBe(302); });

        LocationOf(entra).Should().StartWith(TestEntra.EndSessionEndpoint);
        LocationOf(signon).Should().StartWith("https://oidc.invalid/", "BCC Signon's logout, not Entra's");
        LocationOf(signon).Should().NotStartWith("https://entra.invalid");
    }

    [Fact]
    public async Task Back_channel_logout_is_for_signon_and_answers_400_when_signon_is_off()
    {
        await using var entraOnly = await Fixture.StartProviderHostAsync(signon: false, entra: true);

        await entraOnly.Scenario(s =>
        {
            s.Post.FormData(new Dictionary<string, string> { ["logout_token"] = TestOidc.LogoutToken() }).ToUrl("/auth/backchannel-logout");
            s.StatusCodeShouldBe(400);
        });
    }

    private async Task<int> SessionRowsOfAsync(TestUser user)
    {
        await using var db = Fixture.NewDb();
        return await db.AuthSessions.CountAsync(r => r.Subject == user.PersonId.ToString(), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_session_from_before_the_person_id_claim_gets_401_on_the_api_and_its_row_is_removed()
    {
        var legacy = TestUser.NewLegacy();
        var cookie = await TestSessions.CreateAsync(Host, legacy);
        (await SessionRowsOfAsync(legacy)).Should().Be(1);

        await Host.Scenario(s => { s.WithRequestHeader("Cookie", cookie); s.Get.Url("/api/me"); s.StatusCodeShouldBe(401); });

        (await SessionRowsOfAsync(legacy)).Should().Be(0, "signing out of the rejected session removes it from the store");
    }

    [Fact]
    public async Task A_session_from_before_the_person_id_claim_is_sent_to_sign_in_again_on_a_navigation()
    {
        var legacy = TestUser.NewLegacy();
        var cookie = await TestSessions.CreateAsync(Host, legacy);

        var result = await Host.Scenario(s => { s.WithRequestHeader("Cookie", cookie); s.Get.Url("/"); s.StatusCodeShouldBe(302); });

        LocationOf(result).Should().StartWith(TestOidc.AuthorizationEndpoint, "Signon only: straight to the provider, not a 403");
        (await SessionRowsOfAsync(legacy)).Should().Be(0);
    }

    [Fact]
    public async Task A_session_from_before_the_person_id_claim_cannot_log_out_with_a_403_either()
    {
        var cookie = await TestSessions.CreateAsync(Host, TestUser.NewLegacy());

        await Host.Scenario(s => { s.WithRequestHeader("Cookie", cookie); s.AsBrowser(); s.Post.Url("/auth/logout"); s.StatusCodeShouldBe(401); });
    }
}
