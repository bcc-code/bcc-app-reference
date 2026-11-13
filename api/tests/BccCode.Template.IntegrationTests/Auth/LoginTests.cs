using BccCode.Template.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BccCode.Template.IntegrationTests.Auth;

/// <summary>The sign-in routes under /auth (standard 14): login redirects to the identity provider, the return URL must be local.</summary>
public class LoginTests(AppFixture fixture) : AppTest(fixture)
{
    [Fact]
    public async Task Login_redirects_to_the_identity_provider_with_a_code_flow_and_a_pkce_challenge()
    {
        var result = await Fixture.Host.Scenario(s => { s.Get.Url("/auth/login?returnUrl=%2Fnotes"); s.StatusCodeShouldBe(302); });

        var location = result.Context.Response.Headers.Location.ToString();
        location.Should().StartWith(TestOidc.AuthorizationEndpoint);
        var query = QueryHelpers.ParseQuery(new Uri(location).Query);
        (query["response_type"].ToString(), query["client_id"].ToString(), query["code_challenge_method"].ToString()).Should().Be(("code", "template", "S256"));
        query["code_challenge"].ToString().Should().NotBeEmpty();
        query["scope"].ToString().Should().Be("openid profile", "no offline_access: there is no refresh token to use");
    }

    // The values are the query string as sent, so "%09" reaches the endpoint as a tab.
    [Theory]
    [InlineData("https%3A%2F%2Fevil.example")]
    [InlineData("%2F%2Fevil.example")]
    [InlineData("%2F%5Cevil.example")]
    [InlineData("%2F%09%2Fevil.example")]
    public async Task Login_with_a_return_URL_that_is_not_local_ends_at_the_root(string returnUrl)
    {
        var result = await Fixture.Host.Scenario(s => { s.Get.Url($"/auth/login?returnUrl={returnUrl}"); s.StatusCodeShouldBe(302); });

        // The OIDC state parameter carries the AuthenticationProperties the callback redirects with.
        var query = QueryHelpers.ParseQuery(new Uri(result.Context.Response.Headers.Location.ToString()).Query);
        var options = Fixture.Host.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(OpenIdConnectDefaults.AuthenticationScheme);
        options.StateDataFormat.Unprotect(query["state"].ToString())!.RedirectUri.Should().Be("/");
    }

    [Fact]
    public async Task Login_does_not_ask_for_a_second_factor_on_the_first_leg()
    {
        var result = await Fixture.Host.Scenario(s => { s.Get.Url("/auth/login"); s.StatusCodeShouldBe(302); });

        QueryHelpers.ParseQuery(new Uri(result.Context.Response.Headers.Location.ToString()).Query)["scope"].ToString().Should().NotContain("mfa");
    }

    [Fact]
    public async Task The_second_factor_page_is_open_and_says_nothing_about_a_person()
    {
        var result = await Fixture.Host.Scenario(s => { s.Get.Url("/auth/mfa-required"); s.StatusCodeShouldBeOk(); });

        result.Context.Response.Headers.CacheControl.ToString().Should().Be("no-store");
        (await result.ReadAsTextAsync()).Should().Contain("Second factor required");
    }
}
