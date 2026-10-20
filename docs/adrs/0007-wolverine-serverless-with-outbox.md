# ADR 0007: Wolverine ServerlessWithOutbox with Pub/Sub push

- Status: Accepted
- Date: 2026-10-20
- Same decision as bcc-vote-new (ADR 0016), bcc-privacy and orginn. Wolverine 6.51.0 added
  `DurabilityMode.ServerlessWithOutbox`.

## Context

The API runs on Cloud Run with request-based billing (`cpu_idle = true`), so it has no CPU between requests (standard 04,
rule 17). The first version of the template used `DurabilityMode.Serverless` and sent no messages. Standard 13, rule 10
requires that a committed change eventually produces its messages, through a durable outbox or a reconcile loop.

`Serverless` has no outbox: a message is sent after the commit, and when the send fails it is lost. A durable outbox in
`Balanced` mode needs agents and polling, so CPU between requests. `ServerlessWithOutbox` keeps the lean runtime (no node
registration, agents, leader election, local queues or timers) and adds back the transactional outbox. Recovery is
triggered from outside the process by a "durability pass". Wolverine.Http ships a push receiver, and
`WolverineFx.Pubsub.Http` maps Pub/Sub push requests to envelopes.

## Decision

The same configuration in every environment, so local runs and E2E use the push path that runs in GCP.

**Mode.** `Durability.Mode = ServerlessWithOutbox`. A cascaded or published message is stored in the outbox in the
handler's transaction, sent to Pub/Sub inline after the commit, and its row deleted once Pub/Sub accepts it. A failed send
leaves the row for the durability pass. `AutoBuildMessageStorageOnStartup` is `None` outside Development and Test: the
migrate step owns the envelope tables, as it owns the rest of the schema.

**Publishing.** Every message type that leaves a handler implements `IIntegrationMessage` and is routed to one topic,
`MessageBatchSize(1)` so that each message is pushed, retried and dead-lettered on its own. An architecture test fails on
any other cascaded type (standard 10, rule 9). Local routing is off. The sample is `NoteArchivedMessage`, cascaded by the archive
slice in both variants.

**Receiving.** One push subscription delivers to `POST /internal/pubsub/messages`, Wolverine's push receiver. It answers
204 to ack. A mapper refuses (400) any envelope whose type is not an `IIntegrationMessage`, because the receiver would
otherwise run any type with a handler. A handler failure answers 500 (`OnAnyException().RequeueIndefinitely()`), so
Pub/Sub retries with backoff and moves the message to the dead-letter topic after 5 deliveries. A message that cannot be
read goes to Wolverine's dead-letter table, kept 7 days, and is acked. The execution timeout is 570 s, under the 600 s ack
deadline and Cloud Run request timeout.

**Durability pass.** `POST /internal/durability/pass`, called every minute by Cloud Scheduler. It sends outbox rows older
than `OutboxStaleTime` (60 s) and deletes expired envelopes and dead letters. Locally it is the dashboard command
"Durability pass".

**Internal endpoints.** `/internal/**` is outside the OpenAPI document and the user policies. Each route requires its own
policy on the `GoogleOidc` bearer scheme: the token's audience and the calling service account's email must match the
configuration (`Internal:*`). Missing settings fail at startup. `Internal:AllowAll` lets the local emulator push without a
token and is refused at startup outside Development and Test.

**Not used:** Wolverine scheduled or delayed messages. Under this mode they are released at the next pass, a minute or
more late. Delayed work is a Cloud Task, periodic work a Cloud Scheduler job calling an internal endpoint.

## Consequences

| Benefit | Cost |
|---|---|
| A message is sent if and only if its transaction commits; a failed send is retried by the next pass | A Cloud Scheduler job and one API request every minute, in every environment |
| No CPU between requests; background work arrives as HTTP requests | Every handler must be idempotent: Pub/Sub delivers at least once, and a pass can resend a row that was sent |
| Retry, dead-lettering and the receiver are Wolverine code plus small policy and mapper classes | Pub/Sub messages are binary Wolverine batches; a consumer outside Wolverine cannot read them as JSON |
| Unreadable messages are kept for inspection | Their bodies, which can hold personal data, stay in Postgres for 7 days (data inventory) |
| Local runs and E2E exercise the same path as GCP | A Pub/Sub emulator and a setup container in the AppHost |

The pass takes a PostgreSQL session-level advisory lock. Behind a connection pooler in transaction mode the lock and the
unlock can reach different server sessions (see bcc-vote-new ADR 0016, follow-ups). The template connects directly; an app
that adds pooling must check this first.

## Alternatives considered

- **Stay on `Serverless` with a reconcile job.** No outbox; every message type needs its own repair logic.
- **`Balanced` with a durable outbox.** Needs CPU between requests: a worker pool or always-on instances.
- **One topic per message type.** Clearer metrics per type, but a subscription, dead-letter policy and alert for each. An
  app can split topics later by adding routes.
