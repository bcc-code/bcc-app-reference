# 13 Messaging and Integration

How services exchange messages and events with each other and with platform services (BCC PubSub, Google Pub/Sub, Cloud Tasks, webhooks), and how receivers handle retries, poison messages and lost deliveries.

Messages are a contract between teams and between deploys. An unversioned payload or a non-idempotent handler breaks consumers silently, and a receiver that logs message bodies can leak personal data. Internal domain events and handler flow are covered in [10 Vertical Slices and CQRS](10-vertical-slices-and-cqrs.md).

## Rules

### Message contracts

1. **Integration messages that would carry personal data MUST be thin notifications with ids and versions, not copies of state; the consumer re-reads what it needs. Other integration messages SHOULD be thin too.** A thin message cannot carry stale or excess personal data, and the consumer always acts on current state. Add a timestamp or version so the consumer can ignore out-of-order deliveries. Messages without personal data MAY carry state (event-carried state transfer) when external consumers need it and the contract in rule 4 covers it.
2. **Integration messages SHOULD be sealed records implementing a marker interface.** For example, an `IIntegrationMessage` marker. The marker also lets the receiver build the explicit type list that rule 3 requires, instead of resolving any type name it is sent.
3. **The message type MUST travel in a transport attribute (CloudEvents `type`, a Pub/Sub `message-type` attribute), and the receiver MUST resolve it against an explicit list of known types.** Never deserialize to a type named by the sender without that list.
4. **Events that leave the service MUST have an explicit, versioned contract: the type name includes a version (`no.bcc.orders.order-cancelled.v1`) or the payload carries a schema version, and the schema is documented where consumers find it.** Put the event list with payload fields, meaning and example in the public integrator docs, and keep a serialization test so a renamed property fails the build. A breaking change gets a new version, published side by side until consumers move.
5. **Messages between roles of the same service SHOULD stay compatible across one deploy (add optional fields, never rename or remove in the same release).** During a rollout the old Api can send to the new Processor. A version suffix is not required for these.
6. **Event names MUST be past tense and namespaced by owner: `no.bcc.<service>.<thing>-<happened>`, followed by the version suffix from rule 4 (`.v1`) for events that leave the service.** Example: `no.bcc.orders.order-cancelled.v1`. Commands to your own processor (`RefreshOrderSummary`) are imperative and stay inside the service.

### Receiving

7. **Push receivers MUST apply these delivery semantics:**
   - **Poison** (cannot decode, unknown type): acknowledge (2xx) and log message id, type and reason, **never the body**.
   - **Transient failure**: return 5xx so the platform retries with backoff.
   - **Final attempt** (delivery attempt equals the subscription's `max_delivery_attempts`) for a message that has a final-failure handler: record the failure in a new transaction and acknowledge.
   - **Otherwise**: return 5xx and let the subscription move the message to its dead-letter topic.

   A shared constant for the poison log template lets a log-based metric and an alert count poison messages (see [22 Observability and Operations](22-observability-and-operations.md)).
8. **Every message handler MUST be idempotent.** Push delivery is at-least-once and every retry or repair can deliver again. Use deterministic ids (Cloud Task names, stream ids), version checks, or "set state" operations instead of "toggle" or "append" on the consumer side.
9. **Outgoing calls to external systems that are not idempotent MUST be made idempotent on our side before they are retried.** Check current state first or record that the call succeeded; otherwise a retry removes or adds twice.
10. **Each service MUST guarantee that a committed change eventually produces its messages: either a durable outbox or a reconcile loop.** This is a style choice; record it in an ADR.
    - **Durable outbox**: a messaging library such as Wolverine stores outgoing messages in the same Postgres transaction and delivers them from a worker or host.
    - **Reconcile**: messages are sent after commit, and a scheduled job compares state with what should exist and repairs lost messages or tasks. Requires idempotent handlers (rule 8) and accepts a short delay. This fits services that get no CPU outside requests.
11. **Dead letters MUST be visible and replayable from an admin-only UI or endpoint, with a runbook and an alert on new dead letters.** Pub/Sub dead-letter topics and the delivery attempt limit are set up as described in [04 GCP Runtime](04-gcp-runtime.md); handler-level failures are covered in [10 Vertical Slices and CQRS](10-vertical-slices-and-cqrs.md).

### Webhooks and subscriptions

12. **Every inbound webhook MUST authenticate its caller with a mechanism specific to that caller, and anonymous-reachable webhooks MUST be rate limited.** Options: a scoped token issued by the platform that calls the webhook (for example the `pubsub#deliver` scope of BCC PubSub), a Google OIDC token, or a shared secret compared in constant time. For Google OIDC tokens each receiver verifies the token's audience (its own URL) and the calling service account (`email` claim). Rate limit any webhook that starts expensive work (for example 100 requests per minute with no queue).
13. **When the service holds subscriptions that expire (for example a webhook subscription at a platform API), they MUST be renewed by an explicit scheduled job, not by a timer in a hosted service.** On Cloud Run with scale to zero and CPU only during requests, a `BackgroundService` loop with `Task.Delay(24h)` runs only while an instance happens to be alive. Use Cloud Scheduler calling an internal endpoint (for example a `RenewSubscriptions` endpoint) or a Wolverine recurring message on an always-on worker.
14. **Inbound events from platform services SHOULD be translated at the edge into the service's own messages.** For example, a `PlatformWebhookEndpoint` accepts the platform's CloudEvents envelope, drops what is irrelevant and publishes its own `PersonChanged`. The rest of the code then depends on our contract, not on the platform's.

## Examples

### Push receiver (rule 7)
The endpoint decodes the push body against the known type list. Poison messages are acknowledged and logged without the body. Handler failures return 5xx until the final attempt, and only a message with a final-failure handler is recorded and acknowledged on that attempt.

```csharp
switch (PubSubPush.Decode(body, types))
{
    case PushDecodeResult.Poison poison:
        // Never the body: the message may carry a person uid, see the data inventory.
        log.Poison(poison.MessageId, poison.MessageType, subscription, poison.Reason, body.Length);
        return Results.NoContent();

    case PushDecodeResult.Ok ok:
        var final = ok.Message as IHasFinalFailure;
        try
        {
            await bus.InvokeAsync(ok.Message, ct);
            return Results.NoContent();
        }
        catch (Exception e)
        {
            log.HandlerFailed(e, ok.Message.GetType().Name, ok.MessageId, subscription, ok.DeliveryAttempt);
            if (ok.DeliveryAttempt < FinalAttempt || final is null)
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
        ...
```

### Marker interface and type list (rules 1-3)
```csharp
/// <summary>A notification carrying ids and versions only. Delivered by Pub/Sub push to the processor.</summary>
public interface IIntegrationMessage;
...
public static IntegrationMessageTypes FromAssembly(Assembly assembly) => new(assembly.GetTypes()
    .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IIntegrationMessage).IsAssignableFrom(t)));
```

Messages are one-line records, for example `public sealed record OrderChanged(string OrderId) : IIntegrationMessage;`.

### Outbound event with namespaced past-tense type (rules 4, 6)
The naming is the pattern. The contract also needs a version in the type name (or in the payload), a sealed record and published documentation:

```csharp
public sealed record OrderCancelled(string OrderId, DateTimeOffset OccurredAt) : IIntegrationMessage
{
    public const string EventType = "no.bcc.orders.order-cancelled.v1";
    ...
```

### Rate-limited webhook with caller-specific auth (rule 12)
The webhook endpoint uses its own authorization policy (a shared secret compared in constant time) and a named rate limit policy.

```csharp
options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
options.AddFixedWindowLimiter(PartnerWebhookEndpoint.CallbackRateLimitPolicy, o =>
{
    o.PermitLimit = 100;
    o.Window = TimeSpan.FromMinutes(1);
    o.QueueLimit = 0; // reject immediately, do not queue
});
```

### Subscription renewal as a scheduled job (rule 13)
Cloud Scheduler calls an internal endpoint once a day. The endpoint subscribes again with the same subscription id; that call is the renewal.

### Reconcile loop and durable outbox (rule 10)
A reconcile loop is a scheduled endpoint that compares state with the messages and tasks that should exist and repairs the missing ones. A durable outbox is configured in the messaging library, for example with Wolverine:

```csharp
opts.PublishAllMessages().ToPostgresqlQueue(Queues.Messages).UseDurableOutbox();
```

### Infrastructure (rules 7, 11, 12)
Terraform defines push subscriptions with OIDC tokens (the receiver's own URL as audience, and a dedicated service account that the receiver checks in the `email` claim), a retry policy, a dead-letter topic with `max_delivery_attempts = 5`, and a pull subscription used to inspect dead letters.

## Related
- [10 Vertical Slices and CQRS](10-vertical-slices-and-cqrs.md) — internal events, handler flow and dead-letter handling
- [04 GCP Runtime](04-gcp-runtime.md) — Pub/Sub dead-letter topics and delivery attempts
- [12 Data Access and Migrations](12-data-access-and-migrations.md) — transactions that the outbox joins
- [15 Authorization and Permission Checks](15-authorization-and-permission-checks.md) — scopes and webhook policies
- [18 Personal Data and GDPR](18-personal-data-and-gdpr.md) — keeping personal data out of messages and logs
- [22 Observability and Operations](22-observability-and-operations.md) — poison-message metrics and alerts
- [23 Documentation and Decisions](23-documentation-and-decisions.md) — ADRs and integrator docs
- [Security principles](../principles/security.md#rate-limiting) — rate limiting and zero trust
