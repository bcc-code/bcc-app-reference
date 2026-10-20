---
standard: 18 Privacy and GDPR
assessed: 2026-10-19
commit: e4b2b86
updated: 2026-10-20 (ServerlessWithOutbox, ADR 0007)
---
# 18 Privacy and GDPR — assessment

Minimisation, logging and test data follow the standard, and a data inventory exists, marked as an unverified AI-assisted
draft. The inventory now lists Wolverine's outbox and dead-letter tables, which hold message bodies (dead letters for 7 days,
deleted by the durability pass), and rejected pushes are logged without their body. There is no retention purge and no erasure path (the inventory says so), expired session rows are never removed
although the inventory says sessions are deleted at expiry, and edits record no actor. Several of these gaps wait on
decisions by the privacy function that a template cannot make.

**Score: Weak** · ✅ 7 · ⚠️ 5 · ❌ 2 · N/A 1

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | Data inventory in `docs/privacy/` (categories, purpose, storage, retention, access) | MUST | ⚠️ | `docs/privacy/data-inventory.md` covers all headings. Two statements do not match the code: §4 gives "Session expiry" as the deletion path for sessions, but expired rows are kept "until a cleanup job removes them" (`MartenAuthSessions.cs:124`, `EfAuthSessions.cs:29`) and no such job exists; §6 says the Marten events "include the acting owner id", but only `NoteCreated` has it (`Features/Notes/Note.cs:73-78`). The message outbox and dead letters are listed (§3 and §4): bodies as the app defines them, dead letters 7 days, Pub/Sub's own retention, and a rule that a message type with personal data must be named there |
| 2 | Inventory verified by privacy function, else marked unverified | MUST | ⚠️ | Marked as an AI-assisted draft for review (`data-inventory.md:3-6`). Verification is left to the adopting team |
| 3 | Privacy section in spec/PR for changes to personal data | MUST | ✅ | `.github/pull_request_template.md` "Privacy" section with data, purpose, storage, retention, access and the inventory checkbox; `.claude/skills/pr/SKILL.md:115` |
| 4 | Legal basis and retention agreed with privacy function | MUST | ⚠️ | Recorded as open questions 1, 2, 4, 5 (`data-inventory.md:85-91`) and in `docs/runbooks/go-live.md:35`. Not decided, as expected for a template |
| 5 | Collect only what is used; ids instead of names | MUST | ✅ | Scopes `openid profile`, slim claims (`AuthConfiguration.cs:120-122`, `SessionClaims.cs:33-45`); notes store the `sub`, not the name; responses omit the owner. Tests: `NoteSliceTests.The_response_does_not_expose_the_owner`, `NotesApiTests.Me_returns_the_display_name_and_roles_and_nothing_else`, `SessionClaimsTests` |
| 6 | Retention as a scheduled, tested, idempotent purge job | MUST | ❌ | No purge job for notes (acknowledged in `data-inventory.md:52-66`) and none for expired session rows. Wolverine's dead letters are the only data with a scheduled deletion: the durability pass, called every minute by Cloud Scheduler, deletes them after 7 days (`DeadLetterQueueExpiration`, `Messaging/TemplateMessaging.cs:33-34`). `LayerRulesTests.Nothing_runs_in_the_background` forbids hosted services; the durability pass (`/internal/durability/pass`, `InternalScheduler` policy, `infra/scheduler.tf`) is now the pattern a purge endpoint can follow |
| 7 | Deletion/export paths where they apply; destructive ops guarded | MUST | ❌ | No erasure or export endpoint (`data-inventory.md:56`, open question 4). No approach yet for erasing data from Marten event streams |
| 8 | Tests assert hidden data never appears | MUST | ✅ | The promise is that the owner id is never returned and foreign notes are invisible: `NoteSliceTests.The_response_does_not_expose_the_owner`, `NotesApiTests.Another_users_note_and_a_missing_note_both_answer_404_so_existence_is_not_revealed`, `Me_returns_…`, E2E `notes.spec.ts` "another user does not see the note" (personas `user`/`user2`). See standard 19 rule 13 for the lack of a general probe |
| 9 | No personal identifiers or bodies in logs | MUST | ✅ | The app log messages are `SeedLog` (count, environment name), `NotesLog.Archived` (note id, not the owner) in both variants, and `IntegrationPushMapper.Refused` (envelope id, count, type); `LoggingRulesTests` force source-generated log classes. `Microsoft.AspNetCore` logs at Warning (`appsettings.json`). No test inspects log output for identifiers |
| 10 | Poison/rejected messages logged without body | MUST | ✅ | A push whose type is not an integration message is logged with envelope id, envelope count and type only (`api/BccCode.Template.Web/Internal/IntegrationPushMapper.cs`, `Refused`). An unreadable payload is stored in `wolverine_dead_letters` (listed in the inventory) and logged by Wolverine itself; that library line is not checked here, see "Not verifiable" |
| 11 | Capability URLs never logged | MUST | N/A | No capability URLs |
| 12 | No full URLs with query strings in logs | MUST | ✅ | No request-logging middleware; ASP.NET hosting request lines (which include the query) are suppressed by the Warning level. Traces carry `url.query`; OpenTelemetry.Instrumentation.AspNetCore 1.19.0 redacts query values by default as far as I know (verify) |
| 13 | Changes and admin actions record actor and time | MUST | ⚠️ | Marten: `NoteCreated` has owner and time; `NoteEdited`/`NoteArchived` have time only. The actor is the owner because `NoteRules.IsVisibleTo` allows nobody else, but it is not recorded. EF Core: timestamps on the row, no actor history (`data-inventory.md:74-78`). No admin write actions exist |
| 14 | Audit trail append-only, separate from app tables | SHOULD | ⚠️ | Marten's event store is append-only; EF Core has no audit trail |
| 15 | Seed/test data generated, never copied from production | MUST | ✅ | `Seeding/SeedNotes.cs` (fixed ids, invented text), `Seeding/SeedingGate.cs` allowlist of disposable environments; OIDC mock personas in `api/OidcMock/Personas.cs`. Tests: `SeedingGateTests` (both variants), `Seeding_adds_the_notes_once`, `The_seeded_user_is_the_one_the_oidc_mock_signs_in` |

## Gaps to close

- **1**: Correct `docs/privacy/data-inventory.md`: §4 must say expired session rows are not removed (or the gap below must be
  closed first); §6 must say only the create event names the owner.
- **6**: Add a scheduled purge for expired `AuthSession` rows/documents in both variants (a command run by Cloud Scheduler or a
  Cloud Run job, idempotent, with a test), and prepare the same shape for note retention once the period is agreed. Adjust
  `LayerRulesTests` if the chosen mechanism is in-process.
- **7**: Once open question 4 is answered, add an admin-only, config-guarded erasure path for one user's notes in both variants
  and decide in an ADR how the Marten variant erases events (delete the stream or crypto-shredding).
- **13**: Put the actor (`sub`) in `NoteEdited` and `NoteArchived`, or in Marten event metadata, and add an equivalent for the
  EF Core variant (insert-only audit table), so the trail does not rely on the ownership rule.
- **2, 4**: For an adopting team: get the inventory, legal basis and retention reviewed by BCC's privacy function and record the
  outcome in the inventory and an ADR.

## Not verifiable in the repo

- Cloud SQL backup retention and its relation to deletion requests (`infra/database.tf` sets it; the policy decision is
  outside the repo).
- That deployed log output in Cloud Logging contains no identifiers; check a sample of production logs and trace attributes.
- Rule 10: that Wolverine's own log lines for a dead-lettered or failed push ("was moved to the error queue", handler
  exceptions) carry no message body. Check one in sandbox with a message that holds test data.
- Rule 1: how long Pub/Sub keeps an unacknowledged message on the push subscription (the topic and subscription use the
  default retention; only the dead-letter inspect subscription sets 7 days in `infra/pubsub.tf`).
