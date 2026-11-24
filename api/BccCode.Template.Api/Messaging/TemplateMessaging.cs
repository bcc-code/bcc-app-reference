using BccCode.PubSub.Wolverine;
using BccCode.Template.Api.Configuration;
using BccCode.Template.Messaging;
using JasperFx;
using JasperFx.Core;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Pubsub;

namespace BccCode.Template.Api.Messaging;

/// <summary>
/// Wolverine delivery (standard 13): <c>ServerlessWithOutbox</c>, internal messages on a Google Pub/Sub topic (or, when
/// <c>Messaging:UsePubSub</c> is false, the local durable inbox), integration events on BCC PubSub, one failure policy for every handler.
/// </summary>
public static class TemplateMessaging
{
    /// <summary>The in-request chain (attempt, retry, 5 s cooldown, retry) fits well within the Cloud Run request timeout and the push ack deadline. The push receiver applies it.</summary>
    public static readonly TimeSpan ExecutionTimeout = TimeSpan.FromSeconds(60);

    /// <summary>How long a dead letter is kept before the durability pass deletes it. Listed in the data inventory.</summary>
    public static readonly TimeSpan DeadLetterRetention = TimeSpan.FromDays(7);

    public static void ConfigureTemplateMessaging(this WolverineOptions options, IHostEnvironment environment, IConfiguration configuration)
    {
        // No node agents or timers (Cloud Run bills per request). A cascaded message is stored with the handler's transaction
        // and the scheduled durability pass (POST /internal/durability/pass) sends stale outbox rows and releases due ones.
        options.Durability.Mode = DurabilityMode.ServerlessWithOutbox;
        // The pass lock must hold behind a transaction-mode pooler, where a session lock leaks onto a pooled connection. Safe
        // because no async daemon runs; an open transaction would stall its gap detection.
        options.Durability.UseTransactionScopedDurabilityPassLock = true;
        // The schema never changes at API startup, in any environment (standard 12.6, ADR 0016): the schema step (`jasper db-apply`:
        // the AppHost's schema resource, the test fixture, the deploy) creates the envelope tables, so code that only works because
        // the app built its own tables fails locally too. With None Wolverine only checks that they exist.
        options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
        // The pass finds stale outbox rows by their timestamp. The index is created by the schema step, never by the app.
        options.Durability.EnableOutgoingTimestampIndex = true;
        // Poison messages are moved to wolverine_dead_letters; the durability pass deletes them after a week.
        options.Durability.DeadLetterQueueExpirationEnabled = true;
        options.Durability.DeadLetterQueueExpiration = DeadLetterRetention;
        options.DefaultExecutionTimeout = ExecutionTimeout;
        // Logical deduplication (wolverine_deduplication_hashed comes from the schema step). A handler opts in with [Deduplicated].
        options.Durability.MessageDeduplicationMode = MessageDeduplicationMode.CompareByHash;

        // Two inline retries, then scheduled retries, then the dead letter table plus a Fault<T> message. A scheduled retry is a
        // Pub/Sub redelivery on the GCP push path (its retry policy backoff) and the durability pass locally. Every handler is
        // idempotent (standard 13.8).
        options.Policies.OnAnyException()
            .RetryOnce()
            .Then.RetryWithCooldown(5.Seconds())
            .Then.ScheduleRetry(1.Minutes(), 15.Minutes())
            .Then.MoveToErrorQueue();
        options.PublishFaultEvents();

        // The schema step and code generation send nothing, so they need no messaging settings.
        if (CodeGeneration.IsJasperFxCommand()) return;

        if (BccPubSubSettings.Enabled(configuration)) ConfigureBccPubSub(options, environment, PlatformSettings.Read(configuration, environment));

        if (!MessagingSettings.UsePubSub(configuration))
        {
            // No Pub/Sub transport: internal messages keep Wolverine's conventional local routing, which under
            // ServerlessWithOutbox writes them to the durable inbox for the durability pass to run.
            return;
        }

        var messaging = MessagingSettings.Read(configuration);
        options.Policies.DisableConventionalLocalRouting();
        // Publish only: delivery is a push subscription, nothing pulls. Terraform owns the topic, so nothing is provisioned.
        options.UsePubsub(messaging.ProjectId);
        // One envelope per Pub/Sub message, so each is retried and dead-lettered on its own.
        options.Publish(rule => rule.MessagesImplementing<IInternalMessage>().ToPubsubTopic(messaging.Topic).MessageBatchSize(1));
        // Fault<T> is routed like the message it reports, so a dead letter becomes a handled message on the same topic.
        foreach (var type in TypesImplementing<IInternalMessage>())
            options.PublishMessage(typeof(Fault<>).MakeGenericType(type)).ToPubsubTopic(messaging.Topic).MessageBatchSize(1);
    }

    private static void ConfigureBccPubSub(WolverineOptions options, IHostEnvironment environment, PlatformSettings platform)
    {
        var bccPubSub = options.UseBccPubSub(transport =>
        {
            transport.PublicBaseUrl = platform.PublicBaseUrl;
            transport.AuthenticationSchemes = [PlatformConfiguration.BccPubSubScheme];
            transport.DeliveryVerification = DeliveryVerification.BearerScope;
        });
        // AutoProvision subscribes at startup and runs a renewal loop, which Cloud Run with request-based billing does not allow.
        // Deployed environments register subscriptions through POST /internal/bcc-pubsub/subscriptions instead.
        if (environment.IsDevelopment()) bccPubSub.AutoProvision();

        var publishToBccPubSub = typeof(BccPubSubTransportExtensions).GetMethod(nameof(BccPubSubTransportExtensions.PublishMessageToBccPubSub))!;
        foreach (var type in TypesImplementing<IIntegrationEvent>())
            publishToBccPubSub.MakeGenericMethod(type).Invoke(null, [options]);
        // An app that consumes a platform event adds options.ListenToBccPubSubTopic<TheEvent>(subscriptionId) here and translates it at
        // the edge into its own internal message (standard 13.14).
    }

    public static IEnumerable<Type> TypesImplementing<T>()
    {
        return typeof(T).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(T).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal);
    }
}
