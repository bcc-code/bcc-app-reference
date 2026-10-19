using BccCode.Template.Api.Marten.Features.Notes;
using BccCode.Template.Api.Marten.Tests.Infrastructure;
using BccCode.Template.Web.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BccCode.Template.Api.Marten.Tests;

[Collection(AppCollectionDefinition.Name)]
public class SecurityTests(AppFixture app)
{
    private IAlbaHost Host => app.Host;

    private IEnumerable<RouteEndpoint> Endpoints() => Host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();

    /// <summary>The sweep: the only check that catches a wrong or missing policy on a new endpoint (standard 15, rule 6).</summary>
    [Fact]
    public void Every_api_endpoint_has_the_policy_its_route_requires_and_none_allows_anonymous()
    {
        var api = Endpoints().Where(e => e.RoutePattern.RawText!.StartsWith("/api/", StringComparison.Ordinal)).ToList();

        api.Should().HaveCountGreaterThan(5, "a broken filter must not pass the sweep vacuously");
        foreach (var endpoint in api)
        {
            var route = endpoint.RoutePattern.RawText!;
            endpoint.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull($"{route} must not allow anonymous access");
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToList();
            var expected = EndpointConventions.ExpectedPolicy(route);
            if (expected.Length > 0) policies.Should().Contain(expected, $"{route} needs the {expected} policy");
            else endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().NotBeEmpty($"{route} needs authorization");
        }
    }

    [Fact]
    public void Only_the_allowlisted_routes_allow_anonymous_access() =>
        AnonymousRouteGuard.FindViolations(Endpoints(), AnonymousRouteGuard.Allowed).Should().BeEmpty();

    [Fact]
    public async Task Without_a_session_an_api_call_answers_401_and_not_a_redirect()
    {
        var result = await Host.Scenario(s => { s.Get.Url("/api/notes"); s.StatusCodeShouldBe(401); });

        result.Context.Response.Headers.Location.Should().BeEmpty();
    }

    [Fact]
    public async Task A_member_is_refused_the_admin_area_and_an_admin_is_let_in()
    {
        await Host.Scenario(s => { s.As(TestUser.New()); s.Get.Url("/api/admin/stats"); s.StatusCodeShouldBe(403); });
        await Host.Scenario(s => { s.As(TestUser.NewAdmin()); s.Get.Url("/api/admin/stats"); s.StatusCodeShouldBeOk(); });
    }

    [Fact]
    public async Task With_no_admin_role_configured_nobody_is_an_admin()
    {
        await using var host = await app.StartHostAsync(new Dictionary<string, string?> { ["Auth:AdminRole"] = "" });

        await host.Scenario(s => { s.As(TestUser.NewAdmin()); s.Get.Url("/api/admin/stats"); s.StatusCodeShouldBe(403); });
    }

    [Fact]
    public async Task Every_response_carries_the_security_headers_errors_included()
    {
        foreach (var scenario in new Action<Scenario>[]
                 {
                     s => { s.As(TestUser.New()); s.Get.Url("/api/me"); },
                     s => { s.Get.Url("/api/me"); s.StatusCodeShouldBe(401); },
                     s => { s.As(TestUser.New()); s.Get.Url($"/api/notes/{Guid.NewGuid()}"); s.StatusCodeShouldBe(404); },
                 })
        {
            var headers = (await Host.Scenario(scenario)).Context.Response.Headers;

            headers.XContentTypeOptions.ToString().Should().Be("nosniff");
            headers.XFrameOptions.ToString().Should().Be("DENY");
            headers["Referrer-Policy"].ToString().Should().Be("strict-origin-when-cross-origin");
            headers["Cross-Origin-Opener-Policy"].ToString().Should().Be("same-origin");
            headers["Cross-Origin-Resource-Policy"].ToString().Should().Be("same-origin");
            headers["Permissions-Policy"].ToString().Should().Contain("camera=()");
            headers.CacheControl.ToString().Should().Be("no-store");
            headers.ContainsKey("Server").Should().BeFalse();
        }
    }

    [Fact]
    public async Task Hsts_is_sent_over_https_outside_development()
    {
        var result = await Host.Scenario(s => { s.As(TestUser.New()); s.AsBrowser(); s.Get.Url("/api/me"); });

        result.Context.Response.Headers.StrictTransportSecurity.ToString().Should().Be("max-age=31536000");
    }

    [Fact]
    public async Task The_shell_needs_a_session_and_carries_a_nonce_csp()
    {
        var anonymous = await Host.Scenario(s => { s.Get.Url("/notes"); s.WithRequestHeader("Accept", "text/html"); s.StatusCodeShouldBe(302); });
        anonymous.Context.Response.Headers.Location.ToString().Should().Be("/user/login?returnUrl=%2Fnotes");

        var signedIn = await Host.Scenario(s => { s.As(TestUser.New()); s.Get.Url("/notes"); s.WithRequestHeader("Accept", "text/html"); });
        var csp = signedIn.Context.Response.Headers.ContentSecurityPolicy.ToString();
        var nonce = System.Text.RegularExpressions.Regex.Match(csp, "'nonce-([^']+)'").Groups[1].Value;
        nonce.Should().NotBeEmpty();
        (await signedIn.ReadAsTextAsync()).Should().Contain($"<script nonce=\"{nonce}\"");
        signedIn.Context.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    [Fact]
    public void The_database_check_is_in_health_and_not_in_alive()
    {
        var registrations = Host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        registrations.Single(r => r.Name == "database").Tags.Should().NotContain("live");
        registrations.Single(r => r.Name == "self").Tags.Should().Contain("live");
    }

    [Fact]
    public async Task Health_and_alive_answer_without_a_session()
    {
        await Host.Scenario(s => { s.Get.Url("/health"); s.StatusCodeShouldBeOk(); });
        await Host.Scenario(s => { s.Get.Url("/alive"); s.StatusCodeShouldBeOk(); });
    }

    [Fact]
    public async Task The_anonymous_routes_are_rate_limited_with_a_retry_after_and_a_problem()
    {
        await using var host = await app.StartHostAsync(new Dictionary<string, string?> { ["RateLimits:AnonymousPerMinute"] = "3" });

        for (var i = 0; i < 3; i++) await host.Scenario(s => { s.Get.Url("/user/antiforgery-token"); s.StatusCodeShouldBe(204); });
        var limited = await host.Scenario(s => { s.Get.Url("/user/antiforgery-token"); s.StatusCodeShouldBe(429); });

        limited.Context.Response.Headers.RetryAfter.ToString().Should().NotBeEmpty();
        (await limited.ReadAsTextAsync()).Should().Contain("urn:bcc:template:errors.tooManyRequests");
    }

    [Fact]
    public void The_note_limits_match_the_contract()
    {
        NoteTitle.MaxLength.Should().Be(200);
        NoteBody.MaxLength.Should().Be(10_000);
    }
}
