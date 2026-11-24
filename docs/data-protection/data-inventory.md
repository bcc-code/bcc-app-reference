# Template: personal data inventory

**Status: draft for review. Drafted with AI help from the design of the template's Notes sample and not yet verified by a person against the code and the deployed environment, nor by BCC's data protection function.** It is not a compliance statement until that review is done; record the date and the reviewer here when it is.

This inventory follows standard 18. It lists categories of personal data, never real people. When an application is made from the template, replace the Notes sample with the application's own data, keep this structure (one section per store, then anonymization and open questions), and update the inventory in the same pull request as every change to personal data processing.

The legal basis, the roles (controller, processor) and the information given to users are **not decided** for the sample. They are for BCC's privacy function to agree (standard 18, rule 4; open question 1).

## Identifiers

- **personUid** is BCC's person id from BCC Signon (the claim `https://login.bcc.no/claims/personUid`). It is pseudonymous: it does not name a person without BCC's person registry. All data is keyed by it ([ADR 0019](../adrs/0019-bff-sign-in-with-bcc-signon.md)). It is stored in the stores below and is never logged (standard 18.9).
- **Note id** is a random Guid with no personal data in it.

The OIDC `sub` claim is kept only in the session store (section 2), for back-channel logout. It is not copied into notes.

## Stores

### 1. Notes (`note` table)

- **Code:** `api/BccCode.Template/Notes/` and the storage in `api/BccCode.Template/`.
- **Where:** tables in the environment's Postgres database (Cloud SQL, in the region of the project).
- **Fields:**
  - `note`: id, owner personUid, title and body (free text written by the user, which may contain personal data about the user or others, and which the template cannot limit), created, edited and archived times, and `version`.
- **Purpose:** to let a signed-in person keep personal notes; in the template, to demonstrate the structure.
- **Who can read it:** the owner only. Another person's note answers 403 and an unknown or archived one 404, both without a body ([ADR 0022](../adrs/0022-default-deny-authorization-and-request-hardening.md)): a note id is a random Guid and its existence is not personal data. Tests with two personas check that a refused write stored nothing. Administrators get note counts only (`GET /api/admin/stats`, no identifiers). Database access is for the admin group of the shared database, through the Cloud SQL Auth Proxy.
- **Retention:** not decided (open question 2). Archiving hides a note from the user but does not delete it. Removal is soft (`archived_at`): the app never deletes a domain row. The database does not forbid it yet (see [Anonymization and erasure](#anonymization-and-erasure)).
- **Deletion:** by an erasure job that an application adds (see [Anonymization and erasure](#anonymization-and-erasure)). The template has no scheduled purge, no erasure procedure and no erasure endpoint yet (open question 4).
- **Special categories:** not collected by design. Free text in notes can still contain them; the user interface should tell users not to store such data in notes (open question 3).

The template keeps no email address, phone number or address: the API requests the scopes `openid profile` only and keeps only the claims listed in section 2.

### 2. Sign-in sessions (session table)

- **Code:** `api/BccCode.Template.Api/Auth/`, [ADR 0019](../adrs/0019-bff-sign-in-with-bcc-signon.md) and [ADR 0020](../adrs/0020-server-side-sessions-with-back-channel-logout.md).
- **Where:** an EF Core table in Postgres.
- **Fields:** a random session key (the cookie value `__Host-<app>`); the OIDC `sub`, `sid` and issuer in plain text; a Data Protection-encrypted ticket with personUid, display name, `sub`, `sid`, issuer and roles; the expiry time.
- **Purpose:** keeping the user signed in (BFF cookie session), and ending sessions on back-channel logout.
- **Who can read it:** only the app; the ticket is encrypted with keys protected by Cloud KMS.
- **Retention:** 8 hours, sliding.
- **Deletion:** on logout, on back-channel logout from BCC Signon, and expired rows when a new session is stored. Expired rows are not read. An expired row stays until someone signs in again.

### 3. Data Protection keys (key table)

- **Code:** `api/BccCode.Template.Api/DataProtection/`, [ADR 0021](../adrs/0021-data-protection-keys-in-postgres-with-kms.md).
- **Where:** an EF Core table in Postgres. In GCP each key is wrapped with a Cloud KMS key; locally and in tests the keys are unencrypted in the throwaway database.
- **Personal data:** none. Listed because the keys decrypt the session tickets.

### 4. Wolverine messages, Pub/Sub and dead letters

- **Where:** Wolverine's durable inbox, outbox and dead-letter tables (`wolverine_incoming_envelopes`, `wolverine_outgoing_envelopes`, `wolverine_dead_letters`) in the app's schema; our Google Pub/Sub topic and its dead-letter topic in GCP ([ADR 0023](../adrs/0023-pubsub-for-internal-messages-and-bcc-pubsub-for-events.md), [ADR 0024](../adrs/0024-wolverine-serverless-with-outbox-and-pubsub-push.md)).
- **Fields:** the envelope body as the app defines it. The sample message cascaded by the archive slice holds only the note id. Wolverine's relay of the `user-name` header is off (`EnableRelayOfUserName`, checked by a test), so an envelope carries no display name or actor. A message type that carries personal data must say so here. An optional `IIntegrationEvent` to BCC PubSub carries ids only; the template sends none.
- **Retention:** dead letters 7 days (`DeadLetterQueueExpiration`); Pub/Sub message retention 7 days (`infra/pubsub.tf`); outbox rows until the message is sent, and a row whose send keeps failing stays.
- **Deletion:** automatic at the end of retention, by the durability pass and Pub/Sub. A message in flight is not reached by erasure; it holds an id at most.

### 5. Logs and traces

- **Where:** logs in Cloud Logging in our project (stdout JSON). Traces and metrics go over OTLP to the BCC monitoring collector ([ADR 0026](../adrs/0026-telemetry-export-through-the-bcc-monitoring-collector.md)), which writes the traces to Cloud Trace in our project and in the platform's central monitoring project, and the metrics to Managed Prometheus in the central project. Locally, everything goes to the Aspire dashboard.
- **Fields:** logs carry the route template, method, status, trace id and internal request id, and no personal identifier: not the personUid, `sub`, the display name, note titles or bodies, or URLs with query strings (standard 18, rules 9 and 12). No identifier is logged on purpose, so there are no exceptions to list. Traces hold request paths with note ids, SQL text without parameter values, and Wolverine message types and ids. The request span of a signed-in user holds the BCC Signon session id (`sid`) as `session.id`, which Signon can link to the person; it is also sent as baggage to `*.bcc.no` hosts only. Metrics hold no personal identifiers.
- **The OTLP header secret:** the collector's key (`Authorization=AuthKey <key>`) is in Secret Manager as `<app>-otel-exporter-otlp-headers`, written by Terraform write-only, so it is not in state. It is not personal data, and it is never logged or copied into tickets.
- **Retention:** logs: set by the platform's Cloud Logging buckets, not by this repository (open question 5). Traces: 30 days, Cloud Trace's fixed retention, in both projects. Metrics: set by bcc-monitoring.
- **Who can read it:** our project's members; for traces and metrics also whoever has access to the central monitoring project (open question 5).
- **Deletion:** by the retention only.

### 6. Browser

- The browser holds the session cookie and what the page shows. The SPA keeps no personal data in local storage. No token reaches the browser.

### 7. Local development, previews and tests only

- The seeder (`api/BccCode.Template.TestData`) and the OIDC mock use generated personas only (`user`, `user2`, `admin`, with made-up personUids). The seeder refuses to run outside Development, Test, Sandbox and Preview (`SeedingGate`), and runs once (`Seeder:Once`). Previews hold generated test data only and are public in practice ([pr-previews](../pr-previews.md)). Integration tests use Testcontainers Postgres that goes away with the container. Never use production data here (standard 18, rule 15).

## Anonymization and erasure

The design erases rather than anonymizes: a note that has reached the end of its retention is deleted. **The template implements none of the steps below yet**; an application builds them with its first retention rule (open questions 2 and 4). The template keeps no history or audit trail of notes ([tech debt](../tech-debt/audit-history-not-in-the-template.md)); an application that adds one decides how erasure reaches it.

- The app's database role should not be able to delete domain rows. Today it can: the template only never issues a delete. A business removal is soft (`archived_at`, hidden by an EF query filter).
- Erasure is a scheduled job (Cloud Scheduler calling an `/internal/` endpoint) that runs a stored procedure. The procedure deletes the object's rows in one transaction, so nothing is left that carries the note's text.
- A test per table should check that the erased data is gone and that other people's data is untouched.

**Not reached:** sessions (8 hours); in-flight messages and dead letters (7 days); logs and traces (platform retention); backups (Cloud SQL backup retention, open question 6).

## Open questions

For the privacy team.

1. **Legal basis and roles** for processing note content and the owner's personUid, and what users are told.
2. **Retention** for active and archived notes, and whether archiving should lead to deletion after a period.
3. **Special categories.** Whether a notice about not storing special-category data in notes is enough, or the feature needs further limits.
4. **Erasure on request.** Who triggers erasure for one person's notes (an admin action, a self-service request or an operator task), and how a request is verified.
5. **Logs and traces.** The retention of the platform's Cloud Logging buckets and of the metrics in bcc-monitoring, and who can read traces in the central monitoring project, where they hold the Signon `sid` of signed-in users.
6. **Backups.** Cloud SQL backup retention and its relation to deletion requests.
