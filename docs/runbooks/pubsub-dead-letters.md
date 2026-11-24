# Dead letters: Pub/Sub dead letters and push backlog

Where failed messages end up and what to do with them ([ADR 0024](../adrs/0024-wolverine-serverless-with-outbox-and-pubsub-push.md), [ADR 0023](../adrs/0023-pubsub-for-internal-messages-and-bcc-pubsub-for-events.md)). There is no admin endpoint for dead letters in the template, so this needs database access and the GCP console. Locally the same messages run from Wolverine's inbox and a failed one lands in `wolverine_dead_letters` of the local database.

## Alerts

All are in `infra/monitoring.tf` and email the addresses in `alert_emails`.

| Alert | Means |
|---|---|
| `bcc-template <env>: Pub/Sub dead letters` | A message failed all push deliveries and waits in `bcc-template-dead-letter-inspect-<env>` for 7 days |
| `bcc-template <env>: Pub/Sub push backlog` | The oldest message on `bcc-template-messages-push-<env>` has waited more than 15 minutes |
| `bcc-template <env>: pushed messages moved to Wolverine dead letters` | Wolverine logged "was moved to the error queue": the handler failed through its whole failure policy, or the API could not read the message. A row is in `wolverine_dead_letters` and the push was acknowledged |

## Where they are

| Place | What lands there | Kept |
|---|---|---|
| Postgres table `wolverine_dead_letters` (in the app's schema) | A message whose handler failed through the whole failure policy: two retries in the request, then redeliveries, dead-lettered on the fifth delivery. A `Fault<T>` is published for it. Also a message that could not be read | 7 days (`expires`), then the durability pass deletes it |
| Pub/Sub subscription `bcc-template-dead-letter-inspect-<env>` | A push from `bcc-template-messages-push-<env>` that failed 10 times without reaching a handler, for example a 401 or a 500 at startup | 7 days |

Pub/Sub pushes each message to `POST /internal/pubsub/{subscription}`. 204 acknowledges it; 500 (the handler threw) makes Pub/Sub retry with backoff; 400 (not an internal message) is refused.

## Find the failing pushes

```bash
gcloud logging read 'resource.type="cloud_run_revision" AND resource.labels.service_name="bcc-template-api-<env>" AND httpRequest.requestUrl:"/internal/pubsub/" AND httpRequest.status>=400' \
  --project <project> --freshness 1h --limit 20 --format='value(timestamp,httpRequest.status)'
gcloud logging read 'resource.type="cloud_run_revision" AND resource.labels.service_name="bcc-template-api-<env>" AND severity>=ERROR' \
  --project <project> --freshness 1h --limit 50 --format='value(timestamp,jsonPayload.message,textPayload)'
```

Typical causes:

- **401 or 403**: the token was refused. Check that `Internal__PubSubAudience` and `Internal__PubSubServiceAccount` on the service match the push subscription's OIDC settings (`infra/pubsub.tf`), and that the Pub/Sub service agent still has `roles/iam.serviceAccountTokenCreator` on the push account.
- **400**: the message type is not an internal message, or the body is not a push request. A publisher other than the API wrote to the topic, or a message type was renamed while messages of the old name were in flight.
- **500**: the handler threw. The exception is in the log. A database outage or a bug in the handler; fix it and the retries succeed, or redeliver from the dead-letter topic once fixed.
- **Backlog with no errors**: the API cannot keep up, or requests time out. Look at the [latency runbook](api-latency.md) and the instance limits.
- **"was moved to the error queue"**: the handler failed repeatedly, or the message could not be deserialized for its type, usually after a change to the message contract. It is in `wolverine_dead_letters`.

## Look at the Postgres dead letters

Connect to the environment's database as a member of the database admin group (`shared_db.admin_group_id` in `PROPS`), through the Cloud SQL Auth Proxy, as the schema step in `deploy.yml` does.

```sql
select id, message_type, exception_type, exception_message, received_at, sent_at, expires
from wolverine_dead_letters
order by sent_at desc
limit 50;
```

`body` is the serialized envelope. Messages can carry personUids: do not copy it into tickets or chat.

## Decide

1. Find the cause from `exception_type`, `exception_message` and the logs for the same time (Cloud Logging, filter on the message type).
2. Fix the cause: a code fix through a normal deploy, or a data or configuration fix.
3. Get the work done again. Handlers are idempotent (standard 13.8), so repeating the user's or the job's action is safe. Replaying the stored envelope is not possible: under `ServerlessWithOutbox` no agent moves rows marked `replayable` back to the inbox, and the durability pass does not either.
4. Leave the row to expire, or delete it once handled: `delete from wolverine_dead_letters where id = '<id>';`

## Look at the Pub/Sub dead letters

In the console: Pub/Sub, subscription `bcc-template-dead-letter-inspect-<env>`, Messages, Pull (without acknowledging, to look only). Or:

```bash
gcloud pubsub subscriptions pull bcc-template-dead-letter-inspect-<env> --project <project> --limit 5 --format=json
```

Messages are binary Wolverine batches, so the payload is not readable JSON; the attributes and the time tell which message failed. Pulling without `--auto-ack` leaves the messages in the subscription. Bodies can hold personal data: do not copy them out of the project.

A message here usually means the API refused the push (check the request logs of `/internal/pubsub/*` for 401 or 403 and the push service account) or failed before Wolverine took it. Fix the cause, then publish the pulled message (data and attributes unchanged) to `bcc-template-messages-<env>` if the work is still needed, and acknowledge the inspected messages (`--auto-ack`). Messages older than 7 days are gone; repair their effect by hand or with a one-off command.

Every handler must stay idempotent. A message can arrive more than once: Pub/Sub delivers at least once, and the durability pass can resend an outbox row whose first send succeeded but was not recorded.
