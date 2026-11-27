using BccCode.Template.Api.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace BccCode.Template.Tests;

public class AnonymousRouteGuardTests
{
    private static RouteEndpoint Endpoint(string route, params object[] metadata)
    {
        return new(_ => Task.CompletedTask, RoutePatternFactory.Parse(route), 0, new EndpointMetadataCollection(metadata), route);
    }

    [Fact]
    public void An_anonymous_route_that_is_not_on_the_allowlist_is_a_violation()
    {
        var violations = AnonymousRouteGuard.FindViolations(
            [Endpoint("/auth/login", new AllowAnonymousAttribute()), Endpoint("/api/copied-attribute", new AllowAnonymousAttribute())],
            AnonymousRouteGuard.Allowed);

        violations.Should().Equal("/api/copied-attribute");
    }

    [Fact]
    public void A_route_without_the_opt_out_is_never_a_violation()
    {
        AnonymousRouteGuard.FindViolations([Endpoint("/api/notes")], AnonymousRouteGuard.Allowed).Should().BeEmpty();
    }

    [Fact]
    public void The_allowlist_match_ignores_case()
    {
        AnonymousRouteGuard.FindViolations([Endpoint("/Auth/Login", new AllowAnonymousAttribute())], AnonymousRouteGuard.Allowed).Should().BeEmpty();
    }

    [Fact]
    public void The_sign_in_routes_of_both_providers_are_allowed_and_nothing_next_to_them_is()
    {
        var violations = AnonymousRouteGuard.FindViolations(
            [
                Endpoint("/auth/login", new AllowAnonymousAttribute()),
                Endpoint("/auth/login/{provider}", new AllowAnonymousAttribute()),
                Endpoint("/auth/sign-in", new AllowAnonymousAttribute()),
                Endpoint("/auth/login/entra", new AllowAnonymousAttribute()),
                Endpoint("/auth/logout", new AllowAnonymousAttribute()),
            ],
            AnonymousRouteGuard.Allowed);

        violations.Should().Equal("/auth/login/entra", "/auth/logout");
    }
}
