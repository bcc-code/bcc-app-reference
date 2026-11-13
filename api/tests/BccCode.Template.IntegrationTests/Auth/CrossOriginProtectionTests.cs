using BccCode.Template.IntegrationTests.Infrastructure;
using BccCode.Template.Notes;
using Microsoft.EntityFrameworkCore;

namespace BccCode.Template.IntegrationTests.Auth;

/// <summary>
/// Standard 14.10 and 16.9: an unsafe request that a page on another origin caused is refused, with or without a cookie, and
/// needs no token from a page of our own. Nothing is written by a refused request.
/// </summary>
public class CrossOriginProtectionTests(AppFixture fixture) : AppTest(fixture)
{

    [Fact]
    public async Task A_safe_request_from_another_origin_is_not_refused()
    {
        await Host.Scenario(s => { s.As(TestUser.New()); s.AsBrowser(sameOrigin: false); s.Get.Url("/api/notes"); s.StatusCodeShouldBeOk(); });
    }

    [Theory]
    [InlineData("cross-site")]
    [InlineData("same-site")]   // another app under the same parent domain
    public async Task A_post_that_another_site_caused_is_refused_and_writes_nothing(string site)
    {
        var user = TestUser.New();
        await using var db = Fixture.NewDb();
        var before = await db.Notes.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken);

        var result = await Host.Scenario(s =>
        {
            s.As(user);
            s.WithRequestHeader("X-Forwarded-Proto", "https");
            s.WithRequestHeader("Sec-Fetch-Site", site);
            s.Post.Json(new CreateNoteRequest("Forged", "")).ToUrl("/api/notes");
            s.StatusCodeShouldBe(403);
        });

        result.Context.Response.ContentType.Should().StartWith("application/problem+json");
        (await db.Notes.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken)).Should().Be(before);
    }

    [Fact]
    public async Task A_bearer_header_does_not_exempt_a_cross_origin_request()
    {
        await Host.Scenario(s =>
        {
            s.As(TestUser.New());
            s.AsBrowser(sameOrigin: false);
            s.WithRequestHeader("Authorization", "Bearer anything");
            s.Post.Json(new CreateNoteRequest("Forged", "")).ToUrl("/api/notes");
            s.StatusCodeShouldBe(403);
        });
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("https://other.localhost")]
    [InlineData("null")]
    public async Task Without_fetch_metadata_a_foreign_origin_is_refused(string origin)
    {
        await Host.Scenario(s =>
        {
            s.As(TestUser.New());
            s.WithRequestHeader("X-Forwarded-Proto", "https");
            s.WithRequestHeader("Origin", origin);
            s.Post.Json(new CreateNoteRequest("Forged", "")).ToUrl("/api/notes");
            s.StatusCodeShouldBe(403);
        });
    }

    [Fact]
    public async Task Fetch_metadata_decides_over_the_origin_header()
    {
        await Host.Scenario(s =>
        {
            s.As(TestUser.New());
            s.WithRequestHeader("X-Forwarded-Proto", "https");
            s.WithRequestHeader("Sec-Fetch-Site", "cross-site");
            s.WithRequestHeader("Origin", "http://localhost");
            s.Post.Json(new CreateNoteRequest("Forged", "")).ToUrl("/api/notes");
            s.StatusCodeShouldBe(403);
        });
    }

    [Theory]
    [InlineData("Sec-Fetch-Site", "same-origin")]
    [InlineData("Sec-Fetch-Site", "none")]
    [InlineData("Origin", "http://localhost")]   // the test server's own origin
    public async Task A_post_from_our_own_origin_needs_no_token(string header, string value)
    {
        await Host.Scenario(s =>
        {
            s.As(TestUser.New());
            s.WithRequestHeader(header, value);
            s.Post.Json(new CreateNoteRequest("Mine", "")).ToUrl("/api/notes");
            s.StatusCodeShouldBe(201);
        });
    }

    [Fact]
    public async Task A_post_without_any_origin_signal_passes_because_it_is_not_from_a_web_page()
    {
        await Host.Scenario(s => { s.As(TestUser.New()); s.Post.Json(new CreateNoteRequest("Mine", "")).ToUrl("/api/notes"); s.StatusCodeShouldBe(201); });
    }

    [Fact]
    public async Task Logout_caused_by_another_site_is_refused_and_a_get_does_not_log_out()
    {
        await Host.Scenario(s => { s.As(TestUser.New()); s.AsBrowser(sameOrigin: false); s.Post.Url("/auth/logout"); s.StatusCodeShouldBe(403); });
        // A GET is no route of its own: it gets the shell like any page, and nothing is signed out.
        var get = await Host.Scenario(s => { s.As(TestUser.New()); s.AsBrowser(); s.Get.Url("/auth/logout"); s.StatusCodeShouldBeOk(); });
        get.Context.Response.ContentType.Should().StartWith("text/html");
        get.Context.Response.Headers.SetCookie.Should().BeEmpty();
    }

    [Fact]
    public async Task Logout_needs_a_session()
    {
        await Host.Scenario(s => { s.AsBrowser(); s.Post.Url("/auth/logout"); s.StatusCodeShouldBe(401); });
    }

    [Fact]
    public async Task There_is_no_xsrf_cookie_any_more()
    {
        var result = await Host.Scenario(s => { s.As(TestUser.New()); s.AsBrowser(); s.Get.Url("/api/me"); });
        result.Context.Response.Headers.SetCookie.Should().BeEmpty();
    }
}
