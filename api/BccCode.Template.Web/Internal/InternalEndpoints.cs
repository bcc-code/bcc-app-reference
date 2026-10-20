using System.Reflection;
using Wolverine.Http;
using Wolverine.Http.Transport.Push;
using Wolverine.Pubsub.Http;
using Wolverine.Runtime;

namespace BccCode.Template.Web.Internal;

/// <summary>
/// The routes under <c>/internal</c>, called by Pub/Sub push and Cloud Scheduler. They are minimal APIs outside Wolverine's
/// endpoint conventions, so each names its internal policy and never gets the user or admin policy. They are not part of the
/// OpenAPI document (the contract is the same for every variant and does not list them).
/// </summary>
public static class InternalEndpoints
{
    public const string Prefix = "/internal";

    /// <summary>Pub/Sub push subscriptions deliver to <c>/internal/pubsub/{subscription}</c>. Wolverine reads the subscription from the body.</summary>
    public const string PubSubPushRoute = "/pubsub/{subscription}";

    /// <summary><c>POST /internal/durability/pass</c> runs one bounded durability pass (Cloud Scheduler).</summary>
    public const string DurabilityPrefix = "/durability";

    /// <summary>
    /// The policy a route under <c>/internal</c> must carry, or null when the route has no place there. Used by
    /// <see cref="InternalRouteGuard"/>, so a new internal route cannot start without naming who may call it.
    /// </summary>
    public static string? ExpectedPolicy(string route) =>
        route.StartsWith(Prefix + "/pubsub/", StringComparison.OrdinalIgnoreCase) ? InternalPolicies.PubSub
        : route.StartsWith(Prefix + DurabilityPrefix + "/", StringComparison.OrdinalIgnoreCase) ? InternalPolicies.Scheduler
        : null;

    /// <param name="messageAssemblies">The assemblies that declare integration messages; only their types run when pushed.</param>
    public static void MapTemplateInternal(this WebApplication app, params Assembly[] messageAssemblies)
    {
        var routes = app.MapGroup(Prefix).ExcludeFromDescription();
        var runtime = app.Services.GetRequiredService<IWolverineRuntime>();

        // Wolverine's push receiver: maps the push request to its envelopes, runs the handlers inline, sends the outgoing
        // messages, then answers 204 (ack) or 500 (redeliver) as PushFailurePolicies decide.
        var mapper = new IntegrationPushMapper(
            new PubsubPushEnvelopeMapper(runtime),
            IntegrationPushMapper.TypesIn([.. messageAssemblies, typeof(Messaging.IIntegrationMessage).Assembly]),
            app.Services.GetRequiredService<ILogger<IntegrationPushMapper>>());
        routes.MapWolverinePushReceiver(PubSubPushRoute, mapper, options =>
        {
            options.RequireAuthorization(InternalPolicies.PubSub);
            options.SuccessStatusCode = StatusCodes.Status204NoContent;
        });

        // Sends the outbox rows whose inline send failed and deletes expired envelopes and dead letters. 500 when a step
        // failed, so Cloud Scheduler records it.
        routes.MapWolverineDurabilityEndpoints(options => options.RequireAuthorization(InternalPolicies.Scheduler), DurabilityPrefix);
    }
}
