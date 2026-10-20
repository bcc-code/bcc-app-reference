---
standard: 13 Messaging and Integration
assessed: 2026-10-19
commit: e4b2b86
updated: 2026-10-20 (ServerlessWithOutbox, ADR 0007)
---
# 13 Messaging and Integration — assessment

Wolverine runs `DurabilityMode.ServerlessWithOutbox` with Google Pub/Sub ([ADR 0007](../adrs/0007-wolverine-serverless-with-outbox.md)).
The archive slice in both variants cascades a sample `NoteArchivedMessage`, which is stored in the outbox in the request's
transaction, published to one topic after the commit and pushed back to `POST /internal/pubsub/{subscription}`. The push
receiver only runs `IIntegrationMessage` types, the internal routes accept Google OIDC tokens of one service account each,
and Cloud Scheduler calls a durability pass that sends what failed to send. Dead letters have a Pub/Sub dead-letter topic, an
inspect subscription, alerts and a runbook. Gaps: the sample message carries no version or timestamp, a push of an unknown
type is refused with 400 instead of being acknowledged (recorded in the ADR), and dead letters are replayed with `gcloud`
rather than from an admin-only endpoint.

**Score: Adequate** · ✅ 5 · ⚠️ 3 · ❌ 0 · N/A 6

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | Integration messages are thin (ids, versions) | MUST | ⚠️ | `NoteArchivedMessage(Guid NoteId)` in `Features/Notes/NoteArchivedMessage.cs` of both variants carries the id only, no copy of state. It has no version or timestamp, so a consumer cannot ignore an out-of-order delivery. |
| 2 | Sealed records with marker interface, architecture-tested | MUST | ✅ | Marker `api/BccCode.Template.Web/Messaging/IIntegrationMessage.cs`; `SliceConventionTests.Integration_messages_are_sealed_records` (`api/tests/BccCode.Template.ArchitectureTests/SliceConventionTests.cs:96-104`) checks every implementer is sealed and a record, and that at least one exists. |
| 3 | Message type in a transport attribute, resolved from known list | MUST | ✅ | Wolverine puts the type in the `message-type` attribute or in each envelope of a batch. `Internal/IntegrationPushMapper.cs` resolves it against the `IIntegrationMessage` types of the API and Web assemblies (`TypesIn`) and refuses the whole request otherwise, also when one envelope of a batch is not allowed. Test: `MessagingTests.A_push_of_a_type_that_is_not_an_integration_message_is_refused_and_not_handled` (both variants: single message, batch, and a batch with an allowed and a refused envelope). |
| 4 | Outgoing events have a versioned, documented contract | MUST | N/A | No event leaves the service: the API publishes to its own topic and the push comes back to itself. ADR 0007 notes that the Pub/Sub messages are binary Wolverine batches that an outside consumer cannot read as JSON; an app that publishes for others needs a separate contract. |
| 5 | Role-to-role messages compatible across one deploy | SHOULD | N/A | One role. The same concern applies between revisions (a message published by the old revision can be pushed to the new one); `docs/runbooks/pubsub-dead-letters.md` names a renamed message type as a cause of 400s. |
| 6 | Event names past tense, `no.bcc.<service>.<thing>-<happened>` | MUST | N/A | No external event names. The internal message and events are past tense (`NoteArchivedMessage`, `NoteCreated`, `NoteEdited`, `NoteArchived`). |
| 7 | Push receiver semantics: poison ack, 5xx retry, final attempt | MUST | ⚠️ | Wolverine's push receiver (`Internal/InternalEndpoints.cs:41-50`) answers 204 on success; a handler failure answers 500 (`Messaging/PushFailurePolicies.cs`, `OnAnyException().RequeueIndefinitely()`), so Pub/Sub retries with backoff and dead-letters after 5 deliveries (`infra/pubsub.tf`, `max_delivery_attempts = 5`). A payload that cannot be read goes to Wolverine's dead-letter table and is acked (204). No final-failure handler exists, so the final-attempt case does not arise. Deviation: a push whose type is not an `IIntegrationMessage` is answered 400, not acked, so Pub/Sub redelivers it 5 times and dead-letters it; ADR 0007 records this. The refusal is logged with envelope id, count and type, not the body (`IntegrationPushMapper.Refused`). Tests: `MessagingTests.A_handler_failure_answers_500_so_that_pub_sub_redelivers_and_nothing_is_dead_lettered_here`, `An_unreadable_payload_is_dead_lettered_and_acknowledged` (both variants). |
| 8 | Every message handler idempotent | MUST | ✅ | The sample `NoteArchivedHandler` only logs. The requirement is stated on the marker (`IIntegrationMessage.cs`), in `AGENTS.md` and in both runbooks. Nothing checks it automatically, which the rule does not require. |
| 9 | Non-idempotent external calls made idempotent before retry | MUST | N/A | No outgoing calls to external systems. |
| 10 | Durable outbox or reconcile loop, recorded in an ADR | MUST | ✅ | Durable outbox under `ServerlessWithOutbox` (`Messaging/TemplateMessaging.cs:26-56`), decided in ADR 0007; the durability pass runs from Cloud Scheduler every minute (`infra/scheduler.tf`). Tests: `MessagingTests.An_archived_note_is_published_and_the_pushed_message_is_handled`, `A_message_whose_send_failed_stays_in_the_outbox_until_the_durability_pass_sends_it`, `A_refused_archive_publishes_nothing` (both variants). |
| 11 | Dead letters visible and replayable, runbook, alert | MUST | ⚠️ | Visible: dead-letter topic and a 7-day inspect subscription (`infra/pubsub.tf`), alerts `Pub/Sub dead letters`, `Pub/Sub push backlog` and `pushed messages moved to Wolverine dead letters` (`infra/monitoring.tf`), runbook `docs/runbooks/pubsub-dead-letters.md`. Replay is by an operator with `gcloud` (pull, republish, ack), not from an admin-only UI or endpoint. Messages in Wolverine's own dead-letter table have no replay path; the runbook only says where they are, and the durability pass deletes them after 7 days. |
| 12 | Inbound webhooks authenticate the caller; anonymous ones rate limited | MUST | ✅ | `POST /user/backchannel-logout` validates the IdP's signed logout token and is rate limited (`Auth/AuthEndpoints.cs:55-67`, `Auth/BackchannelLogoutValidator.cs`; tests `BackchannelLogoutValidatorTests`, `SecurityTests.The_anonymous_routes_are_rate_limited_with_a_retry_after_and_a_problem`). The Pub/Sub push and the durability pass require a Google OIDC token with their own audience and service-account email (`Internal/InternalAuthConfiguration.cs`; tests `InternalAuthTests`, `InternalRoutesTests.Without_AllowAll_a_call_with_no_token_and_a_signed_in_user_are_both_401`). They are not anonymous, so no rate limit is required. |
| 13 | Expiring subscriptions renewed by a scheduled job | MUST | N/A | No expiring subscriptions: the push and inspect subscriptions set `expiration_policy { ttl = "" }` (`infra/pubsub.tf`). Periodic work uses Cloud Scheduler calling an internal endpoint, and `LayerRulesTests.Nothing_runs_in_the_background` still fails on any `IHostedService`. |
| 14 | Platform events translated at the edge | SHOULD | N/A | No inbound platform events. |

## Gaps to close

- **1**: Add a version or timestamp to `NoteArchivedMessage` in both variants (for example the stream version or the archive
  time), so the sample shows how a consumer ignores an out-of-order delivery.
- **11**: Add an admin-only endpoint under `/api/admin/` (or a page) that lists, replays and discards dead letters, at least
  for Wolverine's own dead-letter table, which today can only be read with SQL until the pass deletes it. Pair with standard
  10 rule 12.
- **7**: Either acknowledge an unknown message type and log it (as the standard says), or keep the 400 and leave ADR 0007 as
  the recorded deviation. The current choice puts such messages on the Pub/Sub dead-letter topic, where they are visible.

## Not verifiable in the repo

- Rule 12: that the deployed `/user/backchannel-logout` is registered with the identity provider and reachable only as
  intended (check the client configuration at the IdP and the gateway routes).
- Rules 7, 12: that real Pub/Sub push requests and Cloud Scheduler calls reach the API under
  `INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER` and that real Google-signed tokens pass the `GoogleOidc` scheme and policies
  (`aud`, `email`, `email_verified`). The tests use locally signed tokens and the emulator, which sends no tokens. The manual
  checks in `infra/README.md` cover this after the first sandbox deploy.
- Rule 7: that Wolverine's log line for a dead-lettered push carries no message body (it is library code).
