# 13 Messaging and Integration

How services exchange messages and events with each other and with platform services (BCC PubSub, Google Pub/Sub, Cloud Tasks, webhooks), and how receivers handle retries, poison messages and lost deliveries. Messages are a contract between teams and between deploys; an unversioned payload or a non-idempotent handler breaks consumers silently, and a receiver that logs message bodies can leak personal data. Internal domain events and handler flow are covered in [10 Vertical Slices and CQRS](10-vertical-slices-and-cqrs.md).

## Rules

### Message contracts

1. **Integration messages MUST be thin notifications that carry ids and versions, not copies of state; the consumer re-reads what it needs.** A thin message cannot carry stale or excess personal data, and the consumer always acts on current state. Add a timestamp or version so the consumer can ignore out-of-order deliveries.
2. **Integration messages MUST be sealed records implementing a marker interface, and an architecture test MUST check this.** vote-rewrite: `IIntegrationMessage`, tested by `SliceConventionTests.Integration_messages_are_sealed_records`. The marker also lets the receiver resolve types from one list instead of from any type name it is sent.
3. **The message type MUST travel in a transport attribute (CloudEvents `type`, a Pub/Sub `message-type` attribute), and the receiver MUST resolve it against an explicit list of known types.** Never deserialize to a type named by the sender without that list.
4. **Events that leave the service MUST have an explicit, versioned contract: the type name includes a version (`no.bcc.privacy.consent-withdrawn.v1`) or the payload carries a schema version, and the schema is documented where consumers find it.** Put the event list with payload fields, meaning and example in the public integrator docs (`docs/public/`), and keep a serialization test so a renamed property fails the build. A breaking change gets a new version, published side by side until consumers move.
5. **Messages between roles of the same service SHOULD stay compatible across one deploy (add optional fields, never rename or remove in the same release).** During a rollout the old Api can send to the new Processor. A version suffix is not required for these.
6. **Event names MUST be past tense and namespaced by owner: `no.bcc.<service>.<thing>-<happened>`.** Example: `no.bcc.privacy.consent-withdrawn`. Commands to your own processor (`RefreshEligibility`) are imperative and stay inside the service.

### Receiving

7. **Push receivers MUST apply these delivery semantics:**
   - **Poison** (cannot decode, unknown type): acknowledge (2xx) and log message id, type and reason, **never the body**.
   - **Transient failure**: return 5xx so the platform retries with backoff.
   - **Final attempt** (delivery attempt equals the subscription's `max_delivery_attempts`) for a message that has a final-failure handler: record the failure in a new transaction and acknowledge.
   - **Otherwise**: return 5xx and let the subscription move the message to its dead-letter topic.

   A shared constant for the poison log template lets a log-based metric and an alert count poison messages (see [22 Observability and Operations](22-observability-and-operations.md)).
8. **Every message handler MUST be idempotent.** Push delivery is at-least-once and every retry or repair can deliver again. Use deterministic ids (Cloud Task names, stream ids), version checks, or "set state" operations instead of "toggle" or "append" on the consumer side.
9. **Outgoing calls to external systems that are not idempotent MUST be made idempotent on our side before they are retried.** Check current state first or record that the call succeeded; otherwise a retry removes or adds twice (bcc-privacy `docs/tech-debt/core-api-removal-is-not-idempotent.md`).
10. **Each service MUST guarantee that a committed change eventually produces its messages: either a durable outbox or a reconcile loop.** This is a style choice; record it in an ADR.
    - **Durable outbox** (orginn, bcc-privacy): Wolverine stores outgoing messages in the same Postgres transaction and delivers them from a worker or host.
    - **Reconcile** (vote-rewrite, ADR 0002): messages are sent after commit, and a scheduled job compares state with what should exist and repairs lost messages or tasks. Requires idempotent handlers (rule 8) and accepts a short delay.
11. **Dead letters MUST be visible and replayable from an admin-only UI or endpoint, with a runbook and an alert on new dead letters.** See [10 Vertical Slices and CQRS](10-vertical-slices-and-cqrs.md).

### Webhooks and subscriptions

12. **Every inbound webhook MUST authenticate its caller with a mechanism specific to that caller, and anonymous-reachable webhooks MUST be rate limited.** Options: a scoped BCC token (`pubsub#deliver`), a Google OIDC token with its own audience and service-account email per push subscription, or a shared secret compared in constant time. Rate limit any webhook that starts expensive work (bcc-privacy Phrase callback: 100/min, no queue).
13. **Subscriptions that expire MUST be renewed by an explicit scheduled job, not by a timer in a hosted service.** On Cloud Run with scale to zero and CPU only during requests, a `BackgroundService` loop with `Task.Delay(24h)` runs only while an instance happens to be alive. Use Cloud Scheduler calling an internal endpoint (vote-rewrite `RenewCoreSubscriptions`) or a Wolverine recurring message on an always-on worker.
14. **Inbound events from platform services SHOULD be translated at the edge into the service's own messages.** vote-rewrite's `CoreWebhookEndpoint` accepts the Core API CloudEvents envelope, drops what is irrelevant and publishes its own `CorePersonChanged`. The rest of the code then depends on our contract, not on the platform's.

## Example

Not implemented in the template (the sample has no messaging). Source repos: vote-rewrite `api/BccCode.Vote/Infrastructure/PubSubPushEndpoint.cs` and `IntegrationMessages.cs`, `infra/pubsub.tf`; bcc-privacy `api/BccCode.Privacy.Phrase/Endpoints/PhraseWebhookEndpoint.cs`.
