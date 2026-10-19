using BccCode.Template.Web.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace BccCode.Template.Web.Tests;

public class AnonymousRouteGuardTests
{
    private static RouteEndpoint Endpoint(string route, params object[] metadata) =>
        new(_ => Task.CompletedTask, RoutePatternFactory.Parse(route), 0, new EndpointMetadataCollection(metadata), route);

    [Fact]
    public void An_anonymous_route_that_is_not_on_the_allowlist_is_a_violation()
    {
        var violations = AnonymousRouteGuard.FindViolations(
            [Endpoint("/user/login", new AllowAnonymousAttribute()), Endpoint("/api/copied-attribute", new AllowAnonymousAttribute())],
            AnonymousRouteGuard.Allowed);

        violations.Should().Equal("/api/copied-attribute");
    }

    [Fact]
    public void A_route_without_the_opt_out_is_never_a_violation() =>
        AnonymousRouteGuard.FindViolations([Endpoint("/api/notes")], AnonymousRouteGuard.Allowed).Should().BeEmpty();

    [Fact]
    public void The_allowlist_match_ignores_case() =>
        AnonymousRouteGuard.FindViolations([Endpoint("/User/Login", new AllowAnonymousAttribute())], AnonymousRouteGuard.Allowed).Should().BeEmpty();
}
