# 0024. Wolverine ServerlessWithOutbox with Pub/Sub push

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADR 0026, and the template's earlier ADR on the same mode (the same decision as bcc-vote-new, bcc-privacy and orginn).

## Context

The API runs on Cloud Run with request-based billing (`cpu_idle = true`), so it has no CPU between requests (standard 04.17). Nothing may run in the background. Standard 13.10 requires that a committed change eventually produces its messages, through a durable outbox or a reconcile loop.

`Serverless` has no outbox: a message is sent after the commit, and when the send fails it is lost. A durable outbox in `Balanced` mode needs agents and polling, so CPU between requests. `ServerlessWithOutbox` keeps the lean runtime (no node registration, agents, leader election, local queues or timers) and adds back the transactional outbox. Recovery is triggered from outside the process by a durability pass.

## Decision

**Mode.** `Durability.Mode = ServerlessWithOutbox` in every environment, so local runs and tests use the same mode as GCP. A cascaded message is stored in the outbox in the handler's EF Core transaction, sent after the commit inside the request, and its row deleted once the send is accepted. A failed send stays in the outbox. `AutoBuildMessageStorageOnStartup` is `None` outside Development and Test: the schema step owns the envelope tables ([0016](0016-schema-applied-by-a-deploy-step.md)).

**Publishing and receiving.** Every message that leaves a handler implements `IInternalMessage` (or `IIntegrationEvent`, [0023](0023-pubsub-for-internal-messages-and-bcc-pubsub-for-events.md)) and is routed to one topic with `MessageBatchSize(1)`, so each message is pushed, retried and dead-lettered on its own. Architecture tests fail on any cascaded type without a marker, and on local queues. The push receiver answers 204 to acknowledge. The push mapper refuses (400) any type that is not an internal message. The sample is the message the archive slice cascades.

**Durability pass.** `POST /internal/durability/pass` (`MapWolverineDurabilityEndpoints`) sends stale outbox rows, releases due scheduled messages, runs recovered inbox rows and deletes expired envelopes and dead letters. Cloud Scheduler calls it every minute in GCP; locally an AppHost loop calls it every few seconds. Handlers time out after 60 seconds, so two in-request retries fit in the 300-second request timeout and stay below Wolverine's 5-minute inbox stale time. Billing stays request-based: each pass costs only its own request time.

**Scheduled and delayed work.** Scheduled messages (`ScheduleAsync`, `DelayedFor`) are allowed for per-item follow-ups. Pub/Sub cannot schedule, so Wolverine keeps them as scheduled inbox rows and the pass forwards them when due, up to about a minute late. Recurring jobs are Cloud Scheduler jobs that call an internal endpoint (13.13), not in-process cron.

**Failures.** One policy for every handler: retry once, retry with a cooldown, schedule retries, then move to the error queue, and publish `Fault<T>` so a process can turn a dead letter into a named failure. The first retries happen inside the push request; after that Pub/Sub redelivers a failed push with the subscription's backoff and Wolverine counts the delivery attempt. Wolverine dead-letters on the fifth delivery; the subscription's `max_delivery_attempts` of 10 leaves room, so the Pub/Sub dead-letter topic only catches pushes that never reached a handler. Every handler is idempotent (13.8). See the [dead-letter runbook](../runbooks/pubsub-dead-letters.md).

## Consequences

| | |
|---|---|
| Positive | A message is sent if and only if its transaction commits. Scheduled messages, retries and `Fault<T>` work without background CPU. Local runs and tests use the same mode. |
| Negative | One request per minute per environment, so an instance rarely scales to zero; retries and scheduled messages are up to a minute late. Every handler must be idempotent. Message bodies, which can hold personal data, stay in Postgres in the outbox and dead letters for 7 days (data inventory). The pass lock is transaction-scoped (`UseTransactionScopedDurabilityPassLock = true`), so the pass also works behind a transaction-mode connection pooler ([0015](0015-shared-platform-postgres-with-iam-auth.md)). |
| Follow-up | A separate processor service can take the same topic if handlers grow long enough to slow user requests. Open deviations from standard 13: **13.11**, dead letters have no admin endpoint or UI ([tech debt](../tech-debt/dead-letters-have-no-admin-endpoint.md)); **13.7**, the push mapper answers 400 for an unknown message type on purpose instead of acknowledging it, so the message is retried and dead-lettered, where it stays visible, rather than silently dropped. |

## Alternatives considered

- **`Serverless` with a reconcile job per message type** — no outbox and no pass, but every message type needs its own repair logic, and follow-ups need Cloud Tasks.
- **`Balanced` with a durable outbox** — needs CPU between requests: instance-based billing or a worker pool, for a load that does not need it.
- **One topic per message type** — clearer metrics per type, but a subscription, dead-letter policy and alert for each. An app can split topics later by adding routes.
