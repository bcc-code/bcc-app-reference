using System.Reflection;
using BccCode.PubSub.Wolverine;
using BccCode.Template.Api.Messaging;
using Wolverine.Http;
using Wolverine.Http.Transport.Push;
using Wolverine.Pubsub;
using Wolverine.Pubsub.Http;
using Wolverine.Runtime;
using IInternalMessage = BccCode.Template.Messaging.IInternalMessage;   // Wolverine.Runtime has an IInternalMessage of its own

namespace BccCode.Template.Api.Internal;

/// <summary>
/// The routes under <c>/internal</c> (Google Pub/Sub push and Cloud Scheduler) and BCC PubSub's push endpoint. They are minimal APIs
/// outside Wolverine's endpoint conventions, so each names its internal policy and never gets the user or admin policy. They are
/// not part of the OpenAPI document (the contract does not list them).
/// </summary>
public static partial class InternalEndpoints
{
    public const string Prefix = "/internal";

    /// <summary>Pub/Sub push subscriptions deliver to <c>/internal/pubsub/{subscription}</c>. Wolverine reads the subscription from the body.</summary>
    public const string PubSubPushRoute = "/pubsub/{subscription}";

    /// <summary><c>POST /internal/durability/pass</c> runs one bounded durability pass (Cloud Scheduler, or the AppHost locally).</summary>
    public const string DurabilityPrefix = "/durability";

    /// <summary><c>POST /internal/bcc-pubsub/subscriptions</c> registers the BCC PubSub subscriptions again (Cloud Scheduler). Only mapped while BCC PubSub is enabled.</summary>
    public const string BccPubSubPrefix = "/bcc-pubsub";

    /// <summary>
    /// The policy a route under <c>/internal</c> must carry, or null when the route has no place there. Used by
    /// <see cref="InternalRouteGuard"/>, so a new internal route cannot start without naming who may call it.
    /// </summary>
    public static string? ExpectedPolicy(string route)
    {
        return route.StartsWith(Prefix + "/pubsub/", StringComparison.OrdinalIgnoreCase) ? InternalPolicies.PubSub
            : route.StartsWith(Prefix + DurabilityPrefix + "/", StringComparison.OrdinalIgnoreCase) ? InternalPolicies.Scheduler
            : route.StartsWith(Prefix + BccPubSubPrefix + "/", StringComparison.OrdinalIgnoreCase) ? InternalPolicies.Scheduler
            : null;
    }

    /// <param name="app">The application that gets the internal routes.</param>
    /// <param name="messageAssemblies">The assemblies that declare internal messages; only their types run when pushed.</param>
    public static void MapTemplateInternal(this WebApplication app, params Assembly[] messageAssemblies)
    {
        var routes = app.MapGroup(Prefix).ExcludeFromDescription();
        var runtime = app.Services.GetRequiredService<IWolverineRuntime>();

        // The receiver maps a push through Wolverine's Pub/Sub transport object. With Messaging:UsePubSub the transport is
        // configured with the project (TemplateMessaging). Without it there is none, because creating it at startup would
        // connect to Google, so it is created once the host has started: it is never initialized, and a push is mapped
        // without a connection.
        var usePubSub = MessagingSettings.UsePubSub(app.Configuration);
        InternalMessageTransport(app.Logger, usePubSub ? "Google Pub/Sub" : "the local durable inbox");
        if (!usePubSub)
            app.Lifetime.ApplicationStarted.Register(() => runtime.Options.Transports.GetOrCreate<PubsubTransport>().ProjectId = "local");

        // Wolverine's push receiver: maps the push request to its envelopes, runs the handlers inline, sends the outgoing
        // messages, then answers 204 (ack) or 500 (redeliver). The mapper resolves the message type against the internal
        // messages only (standard 13.3), never against a name the sender chooses.
        var mapper = new InternalMessagePushMapper(
            new PubsubPushEnvelopeMapper(runtime),
            InternalMessagePushMapper.TypesIn([.. messageAssemblies, typeof(IInternalMessage).Assembly]),
            app.Services.GetRequiredService<ILogger<InternalMessagePushMapper>>());
        routes.MapWolverinePushReceiver(PubSubPushRoute, mapper, options =>
        {
            options.RequireAuthorization(InternalPolicies.PubSub);
            options.SuccessStatusCode = StatusCodes.Status204NoContent;
            // A redelivery of a message being handled gets 500, one already handled is acknowledged without running.
            options.DeduplicateWithInbox = true;
        });

        // Sends stale outbox rows, releases due scheduled messages, runs recovered inbox rows and deletes expired envelopes
        // and dead letters. 500 when a step failed, so Cloud Scheduler records it.
        routes.MapWolverineDurabilityEndpoints(options => options.RequireAuthorization(InternalPolicies.Scheduler), DurabilityPrefix);

        if (!BccPubSubSettings.Enabled(app.Configuration)) return;

        // BCC PubSub subscriptions do not expire. Cloud Scheduler registers them again once a day and after a deploy.
        var logger = app.Logger;
        routes.MapPost(BccPubSubPrefix + "/subscriptions", async (IWolverineRuntime wolverine) =>
        {
            var transport = wolverine.Options.Transports.GetOrCreate<BccPubSubTransport>();
            var failed = new List<string>();
            foreach (var endpoint in transport.Endpoints().OfType<BccPubSubEndpoint>().Where(e => e.IsListener))
            {
                try
                {
                    await endpoint.SetupAsync(logger);
                }
                catch (Exception ex)
                {
                    SubscribeFailed(logger, ex, endpoint.Topic);
                    failed.Add(endpoint.Topic);
                }
            }

            return failed.Count == 0
                ? Results.NoContent()
                : Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Subscribing failed.", detail: $"Topics: {string.Join(", ", failed)}");
        }).RequireAuthorization(InternalPolicies.Scheduler);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Internal messages use {Transport}")]
    private static partial void InternalMessageTransport(ILogger logger, string transport);

    [LoggerMessage(Level = LogLevel.Error, Message = "Subscribing to BCC PubSub topic {Topic} failed")]
    private static partial void SubscribeFailed(ILogger logger, Exception exception, string topic);

    /// <summary>BCC PubSub pushes to <c>/_bccpubsub/{topic}/{subscriptionId?}</c> with a BCC Signon token that needs the pubsub#deliver scope.</summary>
    public static void MapTemplateBccPubSub(this WebApplication app)
    {
        if (!BccPubSubSettings.Enabled(app.Configuration)) return;

        app.MapBccPubSubEndpoints();
    }
}
