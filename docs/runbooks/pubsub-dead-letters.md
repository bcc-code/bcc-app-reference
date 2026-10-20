# Pub/Sub dead letters and push backlog

Alerts:

- `template <env>: Pub/Sub dead letters`: a message failed 5 push deliveries and waits in `template-dead-letter-inspect-<env>`
  for 7 days.
- `template <env>: Pub/Sub push backlog`: the oldest message on `template-messages-push-<env>` has waited more than
  15 minutes.
- `template <env>: pushed messages moved to Wolverine dead letters`: the API could not read a message, stored it in its own
  dead-letter table and acknowledged it. It does not reach the Pub/Sub dead-letter topic.

How it works: [ADR 0007](../adrs/0007-wolverine-serverless-with-outbox.md). Pub/Sub pushes each message to
`POST /internal/pubsub/messages`. 204 acknowledges it; 500 (the handler threw) makes Pub/Sub retry with backoff; 400 (not an
integration message) or 5 failed deliveries move it to the dead-letter topic.

1. Find the failing pushes in the API log:

   ```bash
   gcloud logging read 'resource.type="cloud_run_revision" AND resource.labels.service_name="template-api-<env>" AND httpRequest.requestUrl:"/internal/pubsub/" AND httpRequest.status>=400' \
     --project <project> --freshness 1h --limit 20 --format='value(timestamp,httpRequest.status)'
   gcloud logging read 'resource.type="cloud_run_revision" AND resource.labels.service_name="template-api-<env>" AND severity>=ERROR' \
     --project <project> --freshness 1h --limit 50 --format='value(timestamp,jsonPayload.message,textPayload)'
   ```

2. Typical causes:
   - **401 or 403**: the token was refused. Check that `Internal__PubSubAudience` and `Internal__PubSubServiceAccount` on
     the service match the push subscription's OIDC settings (`infra/pubsub.tf`), and that the Pub/Sub service agent still
     has `roles/iam.serviceAccountTokenCreator` on the push account.
   - **400**: the message type is not an `IIntegrationMessage`, or the body is not a push request. A publisher other than
     the API wrote to the topic, or a message type was renamed while messages of the old name were in flight.
   - **500**: the handler threw. The exception is in the log. A database outage or a bug in the handler; fix it and the
     retries succeed, or redeliver from the dead-letter topic once fixed.
   - **Backlog with no errors**: the API cannot keep up, or requests time out (600 s). Look at the
     [latency runbook](api-latency.md) and instance limits.
   - **"was moved to the error queue"**: the message could not be deserialized for its type, usually after a change to the
     message contract. It is in the `wolverine_dead_letters` table of the message store.

3. Look at a dead letter. Messages are binary Wolverine batches, so the payload is not readable JSON; the attributes and the
   time tell which message failed:

   ```bash
   gcloud pubsub subscriptions pull template-dead-letter-inspect-<env> --project <project> --limit 5 --format=json
   ```

   Pulling without `--auto-ack` leaves the messages in the subscription. Bodies can hold personal data: do not copy them
   out of the project.

4. Redeliver after the fix: publish the pulled message (data and attributes unchanged) to `template-messages-<env>`, then
   acknowledge it on the inspect subscription. Handlers are idempotent, so a redelivery of an already handled message is
   harmless. Messages older than 7 days are gone; repair their effect by hand or with a one-off command.

5. Every handler must stay idempotent. A message can arrive more than once: Pub/Sub delivers at least once, and the
   durability pass can resend an outbox row whose first send succeeded but was not recorded.
