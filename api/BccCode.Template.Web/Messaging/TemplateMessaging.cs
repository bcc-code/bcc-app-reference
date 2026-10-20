using BccCode.Template.Web.Http;
using Google.Api.Gax;
using JasperFx;
using Wolverine;
using Wolverine.Pubsub;

namespace BccCode.Template.Web.Messaging;

/// <summary>
/// The Wolverine settings both persistence variants share: <c>ServerlessWithOutbox</c> and Pub/Sub. A variant adds its own
/// message store and transactions around this call (see <c>WolverineConfiguration</c>).
/// </summary>
public static class TemplateMessaging
{
    /// <summary>Just under the 600 s push ack deadline and Cloud Run request timeout. The push receiver applies it; Wolverine's default is 60 s.</summary>
    public static readonly TimeSpan ExecutionTimeout = TimeSpan.FromSeconds(570);

    /// <summary>How long a dead letter is kept before the durability pass deletes it. Listed in the data inventory.</summary>
    public static readonly TimeSpan DeadLetterRetention = TimeSpan.FromDays(7);

    /// <summary>
    /// No agents, polling or local queues, so the API needs no CPU between requests. A cascaded or published message is stored in
    /// the outbox with the handler's transaction, sent to Pub/Sub after the commit and pushed back to the API. A failed send is
    /// left for the durability pass (<c>POST /internal/durability/pass</c>, Cloud Scheduler in GCP).
    /// </summary>
    public static void ConfigureTemplateMessaging(this WolverineOptions options, IHostEnvironment environment, IConfiguration configuration)
    {
        options.Durability.Mode = DurabilityMode.ServerlessWithOutbox;
        // The schema never changes at API startup (standard 12): the schema step creates the envelope tables. With None Wolverine
        // only checks that they exist, where the JasperFx Production default (CreateOrUpdate) diffs them on every cold start.
        options.AutoBuildMessageStorageOnStartup = environment.IsDevelopmentOrTest() ? AutoCreate.CreateOrUpdate : AutoCreate.None;
        // Poison pushes are moved to wolverine_dead_letters; the durability pass deletes them after a week.
        options.Durability.DeadLetterQueueExpirationEnabled = true;
        options.Durability.DeadLetterQueueExpiration = DeadLetterRetention;
        options.DefaultExecutionTimeout = ExecutionTimeout;
        // The durability pass looks up outbox rows older than OutboxStaleTime by their timestamp.
        options.Durability.EnableOutgoingTimestampIndex = true;
        options.Policies.DisableConventionalLocalRouting();   // nothing runs on in-process queues
        PushFailurePolicies.Apply(options);

        // Code generation and the schema step run with the transports stubbed, and need no Pub/Sub settings.
        if (CodeGeneration.IsCommandLineTool() || CodeGeneration.IsGeneratingOpenApi()) return;

        var messaging = MessagingOptions.From(configuration);
        var projectId = string.IsNullOrWhiteSpace(messaging.PubsubProjectId)
            ? throw new InvalidOperationException($"{MessagingOptions.ProjectIdKey} must be configured.")
            : messaging.PubsubProjectId.Trim();

        // Publishing only: delivery is a push subscription, nothing listens by pulling. PUBSUB_EMULATOR_HOST selects the emulator.
        var pubsub = options.UsePubsub(projectId).UseEmulatorDetection(EmulatorDetection.EmulatorOrProduction);
        if (environment.IsDevelopmentOrTest()) pubsub.AutoProvision();   // Terraform owns the topics in GCP

        // A durable sender batches the envelopes of one destination into one Pub/Sub message; a batch of 1 keeps one push per
        // message, so each is retried and dead-lettered on its own.
        options.Publish(rule => rule.MessagesImplementing<IIntegrationMessage>().ToPubsubTopic(messaging.Topic).MessageBatchSize(1));
    }
}
