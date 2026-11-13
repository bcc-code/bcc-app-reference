using BccCode.Template.Api.Configuration;
using BccCode.Template.IntegrationTests.Infrastructure;
using BccCode.Template.Notes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BccCode.Template.IntegrationTests;

public class SecurityTests(AppFixture fixture) : AppTest(fixture)
{

    private IEnumerable<RouteEndpoint> Endpoints()
    {
        return Host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
    }

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
    public void Only_the_allowlisted_routes_allow_anonymous_access()
    {
        AnonymousRouteGuard.FindViolations(Endpoints(), AnonymousRouteGuard.Allowed).Should().BeEmpty();
    }

    [Fact]
    public async Task Without_a_session_an_api_call_answers_401_and_not_a_redirect()
    {
        var result = await Host.Scenario(s => { s.Get.Url("/api/notes"); s.StatusCodeShouldBe(401); });

        result.Context.Response.Headers.Location.Should().BeEmpty();
    }

    [Fact]
    public async Task A_member_is_refused_the_admin_area_and_an_admin_with_a_second_factor_is_let_in()
    {
        await Host.Scenario(s => { s.As(TestUser.New()); s.Get.Url("/api/admin/stats"); s.StatusCodeShouldBe(403); });
        await Host.Scenario(s => { s.As(TestUser.NewAdmin()); s.Get.Url("/api/admin/stats"); s.StatusCodeShouldBeOk(); });
    }

    [Fact]
    public async Task An_admin_without_a_second_factor_gets_401_with_the_step_up_problem_on_every_api_route()
    {
        foreach (var url in new[] { "/api/admin/stats", "/api/me", "/api/notes" })
        {
            var result = await Host.Scenario(s => { s.As(TestUser.NewAdminWithoutMfa()); s.Get.Url(url); s.StatusCodeShouldBe(401); });

            (await result.ReadAsTextAsync()).Should().Contain("urn:bcc:template:errors.mfaRequired");
        }
    }

    [Fact]
    public async Task With_no_admin_role_configured_nobody_is_an_admin()
    {
        await using var host = await Fixture.StartHostAsync(new Dictionary<string, string?> { ["Auth:AdminRole"] = "" });

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
            headers.ContentSecurityPolicy.ToString().Should().StartWith("default-src 'none'", "an answer that is no page loads nothing");
            headers.CacheControl.ToString().Should().Be("no-store");
            headers.ContainsKey("Server").Should().BeFalse();
        }
    }

    [Fact]
    public async Task Hsts_is_sent_over_https_outside_development()
    {
        var result = await Host.Scenario(s => { s.As(TestUser.New()); s.AsBrowser(); s.WithRequestHeader("Host", "template.example"); s.Get.Url("/api/me"); });

        result.Context.Response.Headers.StrictTransportSecurity.ToString().Should().Be("max-age=31536000");
    }

    [Fact]
    public void The_database_check_is_in_health_and_not_in_alive()
    {
        var registrations = Host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        registrations.Select(r => r.Name).Should().Contain(["self", "PostgreSql"], "Aspire.Npgsql registers the database check with the data source");
        registrations.Single(r => r.Name == "PostgreSql").Tags.Should().NotContain("live");
        registrations.Single(r => r.Name == "self").Tags.Should().Contain("live");
    }

    [Fact]
    public async Task Health_and_alive_answer_without_a_session()
    {
        await Host.Scenario(s => { s.Get.Url("/health"); s.StatusCodeShouldBeOk(); });
        await Host.Scenario(s => { s.Get.Url("/alive"); s.StatusCodeShouldBeOk(); });
    }

    [Fact]
    public void The_note_limits_match_the_contract()
    {
        NoteTitle.MaxLength.Should().Be(200);
        NoteBody.MaxLength.Should().Be(10_000);
    }
}
