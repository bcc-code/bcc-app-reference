---
standard: 13 Messaging and Integration
assessed: 2026-10-19
commit: e4b2b86
---
# 13 Messaging and Integration — assessment

The template has no messaging: no integration messages, no message handlers, no Pub/Sub or Cloud Tasks, and Wolverine runs
in serverless mode with no transports. This is by design (the standard's own example says the sample has no messaging), so
almost every rule is N/A. The one inbound server-to-server call, OIDC back-channel logout, is authenticated and rate limited.

**Score: Absent** (by design; nothing to fix until the app sends or receives messages) · ✅ 1 · ⚠️ 0 · ❌ 0 · N/A 13

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | Integration messages are thin (ids, versions) | MUST | N/A | No integration messages. `Durability.Mode = Serverless` and the comment "The app sends no messages yet" in `Configuration/WolverineConfiguration.cs` (Marten `:26-28`, EF Core `:29-31`). |
| 2 | Sealed records with marker interface, architecture-tested | MUST | N/A | No integration messages, so no marker interface or test. |
| 3 | Message type in a transport attribute, resolved from known list | MUST | N/A | No receiver. |
| 4 | Outgoing events have a versioned, documented contract | MUST | N/A | No events leave the service; Marten note events stay internal. |
| 5 | Role-to-role messages compatible across one deploy | SHOULD | N/A | One role only. |
| 6 | Event names past tense, `no.bcc.<service>.<thing>-<happened>` | MUST | N/A | No external event names. Internal event types are past tense (`NoteCreated`, `NoteEdited`, `NoteArchived`). |
| 7 | Push receiver semantics: poison ack, 5xx retry, final attempt | MUST | N/A | No push receiver. |
| 8 | Every message handler idempotent | MUST | N/A | No message handlers. |
| 9 | Non-idempotent external calls made idempotent before retry | MUST | N/A | No outgoing calls to external systems that are retried. |
| 10 | Durable outbox or reconcile loop, recorded in an ADR | MUST | N/A | Nothing is sent. The choice is deferred to an ADR by the comment in both `WolverineConfiguration.cs` files; `LayerRulesTests.Nothing_runs_in_the_background` blocks hosted services until then. |
| 11 | Dead letters visible and replayable, runbook, alert | MUST | N/A | No dead letters. `docs/runbooks/README.md:16` lists the dead-letter runbook as not written for this reason. |
| 12 | Inbound webhooks authenticate the caller; anonymous ones rate limited | MUST | ✅ | `POST /user/backchannel-logout` (`api/BccCode.Template.Web/Auth/AuthEndpoints.cs:55-67`) validates the IdP's signed logout token: signature, issuer, audience, lifetime, `events` claim (`Auth/BackchannelLogoutValidator.cs:19-57`; tests in `BackchannelLogoutValidatorTests.cs`). It is anonymous and has `RequireRateLimiting(RateLimitPolicies.Anonymous)` (`AuthEndpoints.cs:67`); tested by `SecurityTests.The_anonymous_routes_are_rate_limited_with_a_retry_after_and_a_problem` (both variants). |
| 13 | Expiring subscriptions renewed by a scheduled job | MUST | N/A | No subscriptions. `LayerRulesTests.Nothing_runs_in_the_background` (`api/tests/BccCode.Template.ArchitectureTests/LayerRulesTests.cs:134-136`) fails on any `IHostedService`, which prevents the timer-in-a-hosted-service pattern. |
| 14 | Platform events translated at the edge | SHOULD | N/A | No inbound platform events. |

## Gaps to close

- None now. When the app adds messaging, the template offers no starting point for rules 1–3, 7 and 10–11: no
  `IIntegrationMessage` marker with its architecture test in `api/tests/BccCode.Template.ArchitectureTests/SliceConventionTests.cs`,
  no push receiver, no delivery-style ADR. A team will have to build these from the source repos named in the standard.

## Not verifiable in the repo

- Rule 12: that the deployed `/user/backchannel-logout` is registered with the identity provider and reachable only as
  intended (check the client configuration at the IdP and the gateway routes).
