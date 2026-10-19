using BccCode.Template.Api.Marten.Features.Notes;
using BccCode.Template.Api.Marten.Tests.Infrastructure;

namespace BccCode.Template.Api.Marten.Tests;

[Collection(AppCollectionDefinition.Name)]
public class CsrfTests(AppFixture app)
{
    private IAlbaHost Host => app.Host;

    private static string CookieValue(IScenarioResult result, string name)
    {
        var line = result.Context.Response.Headers.SetCookie.Single(c => c!.StartsWith(name + "=", StringComparison.Ordinal))!;
        return line[(name.Length + 1)..line.IndexOf(';', StringComparison.Ordinal)];
    }

    [Fact]
    public async Task A_safe_request_with_the_session_cookie_needs_no_token() =>
        await Host.Scenario(s => { s.As(TestUser.New()); s.AsBrowser(); s.Get.Url("/api/notes"); s.StatusCodeShouldBeOk(); });

    [Fact]
    public async Task An_unsafe_request_with_the_session_cookie_and_no_token_is_refused_with_a_refresh_hint()
    {
        var result = await Host.Scenario(s =>
        {
            s.As(TestUser.New());
            s.AsBrowser();
            s.Post.Json(new CreateNote("Title", "")).ToUrl("/api/notes");
            s.StatusCodeShouldBe(400);
        });

        result.Context.Response.Headers["X-Csrf-Refresh"].ToString().Should().Be("1");
        (await result.ReadAsTextAsync()).Should().Contain("urn:bcc:template:errors.csrf");
    }

    [Fact]
    public async Task A_bearer_header_does_not_exempt_a_request_that_has_the_session_cookie()
    {
        await Host.Scenario(s =>
        {
            s.As(TestUser.New());
            s.AsBrowser();
            s.WithRequestHeader("Authorization", "Bearer anything");
            s.Post.Json(new CreateNote("Title", "")).ToUrl("/api/notes");
            s.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task An_unsafe_request_with_a_valid_token_goes_through()
    {
        var user = TestUser.New();
        var token = await Host.Scenario(s => { s.As(user); s.AsBrowser(); s.Get.Url("/user/antiforgery-token"); s.StatusCodeShouldBe(204); });
        var requestToken = CookieValue(token, "XSRF-TOKEN");
        var cookieToken = CookieValue(token, "__Host-template-af");

        await Host.Scenario(s =>
        {
            s.As(user);
            s.AsBrowser($"__Host-template-af={cookieToken}");
            s.WithRequestHeader("X-XSRF-TOKEN", Uri.UnescapeDataString(requestToken));
            s.Post.Json(new CreateNote("Title", "")).ToUrl("/api/notes");
            s.StatusCodeShouldBe(201);
        });
    }

    [Fact]
    public async Task The_antiforgery_cookie_is_strict_and_the_request_token_is_readable_by_script()
    {
        var result = await Host.Scenario(s => { s.As(TestUser.New()); s.AsBrowser(); s.Get.Url("/user/antiforgery-token"); s.StatusCodeShouldBe(204); });

        var cookies = result.Context.Response.Headers.SetCookie.Select(c => c!).ToList();
        cookies.Single(c => c.StartsWith("__Host-template-af=", StringComparison.Ordinal)).Should().Contain("samesite=strict").And.Contain("httponly");
        cookies.Single(c => c.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Should().NotContain("httponly");
    }

    [Fact]
    public async Task No_token_is_issued_to_someone_who_is_not_signed_in()
    {
        var result = await Host.Scenario(s => { s.Get.Url("/user/antiforgery-token"); s.StatusCodeShouldBe(204); });

        result.Context.Response.Headers.SetCookie.Should().BeEmpty();
    }

    [Fact]
    public async Task Logout_is_a_post_that_needs_the_token_and_a_get_does_not_log_out()
    {
        await Host.Scenario(s => { s.As(TestUser.New()); s.AsBrowser(); s.Post.Url("/user/logout"); s.StatusCodeShouldBe(400); });
        await Host.Scenario(s => { s.As(TestUser.New()); s.AsBrowser(); s.Get.Url("/user/logout"); s.StatusCodeShouldBe(405); });
    }

    [Fact]
    public async Task Logout_needs_a_session()
    {
        var result = await Host.Scenario(s => { s.Post.Url("/user/logout"); s.StatusCodeShouldBe(302); });

        result.Context.Response.Headers.Location.ToString().Should().Contain("/user/login");
    }

    [Fact]
    public async Task Back_channel_logout_refuses_a_bad_token_without_saying_why()
    {
        var result = await Host.Scenario(s =>
        {
            s.Post.FormData(new Dictionary<string, string> { ["logout_token"] = "not-a-token" }).ToUrl("/user/backchannel-logout");
            s.StatusCodeShouldBe(400);
        });

        (await result.ReadAsTextAsync()).Should().Contain("invalid_request").And.NotContain("not-a-token");
    }
}
