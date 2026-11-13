using BccCode.Template.Api.Internal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace BccCode.Template.Tests;

public class InternalRouteGuardTests
{
    private static RouteEndpoint Endpoint(string route, params object[] metadata)
    {
        return new(_ => Task.CompletedTask, RoutePatternFactory.Parse(route), 0, new EndpointMetadataCollection(metadata), route);
    }

    [Theory]
    [InlineData("/internal/pubsub/{subscription}", "InternalPubSub")]
    [InlineData("/internal/durability/pass", "InternalScheduler")]
    [InlineData("/internal/bcc-pubsub/subscriptions", "InternalScheduler")]
    [InlineData("/internal/other", null)]
    public void The_policy_follows_from_the_route(string route, string? policy)
    {
        InternalEndpoints.ExpectedPolicy(route).Should().Be(policy);
    }

    [Fact]
    public void A_route_with_its_own_internal_policy_is_fine()
    {
        InternalRouteGuard.FindViolations(
        [
            Endpoint("/internal/pubsub/{subscription}", new AuthorizeAttribute(InternalPolicies.PubSub)),
            Endpoint("/internal/durability/pass", new AuthorizeAttribute(InternalPolicies.Scheduler)),
        ]).Should().BeEmpty();
    }

    [Fact]
    public void A_route_with_the_other_policy_the_user_default_or_none_is_a_violation()
    {
        InternalRouteGuard.FindViolations(
        [
            Endpoint("/internal/pubsub/{subscription}", new AuthorizeAttribute(InternalPolicies.Scheduler)),
            Endpoint("/internal/durability/pass", new AuthorizeAttribute()),
            Endpoint("/internal/pubsub/other"),
        ]).Should().Equal("/internal/pubsub/{subscription}", "/internal/durability/pass", "/internal/pubsub/other");
    }

    [Fact]
    public void An_anonymous_internal_route_and_an_unknown_one_are_violations()
    {
        InternalRouteGuard.FindViolations(
        [
            Endpoint("/internal/pubsub/{subscription}", new AuthorizeAttribute(InternalPolicies.PubSub), new AllowAnonymousAttribute()),
            Endpoint("/internal/new-thing", new AuthorizeAttribute(InternalPolicies.PubSub)),
        ]).Should().Equal("/internal/pubsub/{subscription}", "/internal/new-thing");
    }

    [Fact]
    public void Routes_outside_internal_are_not_this_guards_business()
    {
        InternalRouteGuard.FindViolations([Endpoint("/api/notes"), Endpoint("/internalx/pubsub")]).Should().BeEmpty();
    }
}
