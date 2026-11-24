# Messaging locally

The API runs Wolverine in `ServerlessWithOutbox` mode in every environment ([ADR 0024](../adrs/0024-wolverine-serverless-with-outbox-and-pubsub-push.md)).
A message a handler or endpoint returns must implement `IInternalMessage` (or `IIntegrationEvent`): only those are routed, and
the architecture tests fail on any other cascaded type and on local queues.

## What happens locally

`Messaging:UsePubSub` decides the transport, not the environment ([ADR 0023](../adrs/0023-pubsub-for-internal-messages-and-bcc-pubsub-for-events.md)).
Terraform sets it in GCP. Locally and in the integration tests it is off, so there is no emulator and no setup container:

1. A handler returns a message (the sample is the archive slice's message). Wolverine stores it in the outbox in the request's
   transaction, together with the data and the change.
2. Internal messages are routed to Wolverine's durable local inbox. The AppHost calls the durability pass
   (`POST /internal/durability/pass`) every few seconds, which runs the inbox rows. The handler logs that the note was archived.
3. A message cascaded from such a handler waits for the next pass, so a chain of N messages takes N passes.

The dashboard command **Durability pass** on the `api` resource runs one pass by hand. Scheduled messages (`ScheduleAsync`,
`DelayedFor`) are released by the same pass, so they can be up to a pass late.

The local path skips Google Pub/Sub push. Push is exercised in the integration tests, which post push bodies to
`POST /internal/pubsub/{subscription}`, and in sandbox. `Internal:AllowAll=true` is for local runs only: the API refuses it outside
Development and Test, and the AppHost sets it.

## Handlers

Every handler is idempotent: Pub/Sub delivers at least once, and a pass can resend an outbox row whose first send succeeded. Write a
handler so that running it twice gives the same result. Where messages end up when they fail:
[dead-letter runbook](../runbooks/pubsub-dead-letters.md).
