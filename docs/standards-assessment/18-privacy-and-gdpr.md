---
standard: 18 Privacy and GDPR
assessed: 2026-10-19
commit: e4b2b86
---
# 18 Privacy and GDPR — assessment

Minimisation, logging and test data follow the standard, and a data inventory exists, marked as an unverified AI-assisted
draft. There is no retention purge and no erasure path (the inventory says so), expired session rows are never removed
although the inventory says sessions are deleted at expiry, and edits record no actor. Several of these gaps wait on
decisions by the privacy function that a template cannot make.

**Score: Weak** · ✅ 6 · ⚠️ 5 · ❌ 2 · N/A 2

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | Data inventory in `docs/privacy/` (categories, purpose, storage, retention, access) | MUST | ⚠️ | `docs/privacy/data-inventory.md` covers all headings. Two statements do not match the code: §4 gives "Session expiry" as the deletion path for sessions, but expired rows are kept "until a cleanup job removes them" (`MartenAuthSessions.cs:124`, `EfAuthSessions.cs:29`) and no such job exists; §6 says the Marten events "include the acting owner id", but only `NoteCreated` has it (`Features/Notes/Note.cs:73-78`) |
| 2 | Inventory verified by privacy function, else marked unverified | MUST | ⚠️ | Marked as an AI-assisted draft for review (`data-inventory.md:3-6`). Verification is left to the adopting team |
| 3 | Privacy section in spec/PR for changes to personal data | MUST | ✅ | `.github/pull_request_template.md` "Privacy" section with data, purpose, storage, retention, access and the inventory checkbox; `.claude/skills/pr/SKILL.md:115` |
| 4 | Legal basis and retention agreed with privacy function | MUST | ⚠️ | Recorded as open questions 1, 2, 4, 5 (`data-inventory.md:78-84`) and in `docs/runbooks/go-live.md:35`. Not decided, as expected for a template |
| 5 | Collect only what is used; ids instead of names | MUST | ✅ | Scopes `openid profile`, slim claims (`AuthConfiguration.cs:120-122`, `SessionClaims.cs:33-45`); notes store the `sub`, not the name; responses omit the owner. Tests: `NoteSliceTests.The_response_does_not_expose_the_owner`, `NotesApiTests.Me_returns_the_display_name_and_roles_and_nothing_else`, `SessionClaimsTests` |
| 6 | Retention as a scheduled, tested, idempotent purge job | MUST | ❌ | No purge job for notes (acknowledged in `data-inventory.md:51-58`) and none for expired session rows. `LayerRulesTests.Nothing_runs_in_the_background` currently forbids hosted services, so a purge needs a Cloud Run job or Scheduler-triggered endpoint |
| 7 | Deletion/export paths where they apply; destructive ops guarded | MUST | ❌ | No erasure or export endpoint (`data-inventory.md:51`, open question 4). No approach yet for erasing data from Marten event streams |
| 8 | Tests assert hidden data never appears | MUST | ✅ | The promise is that the owner id is never returned and foreign notes are invisible: `NoteSliceTests.The_response_does_not_expose_the_owner`, `NotesApiTests.Another_users_note_and_a_missing_note_both_answer_404_so_existence_is_not_revealed`, `Me_returns_…`, E2E `notes.spec.ts` "another user does not see the note" (personas `user`/`user2`). See standard 19 rule 13 for the lack of a general probe |
| 9 | No personal identifiers or bodies in logs | MUST | ✅ | The only app log messages are `SeedLog` (count, environment name) in both variants; `LoggingRulesTests` force source-generated log classes. `Microsoft.AspNetCore` logs at Warning (`appsettings.json`). No test inspects log output for identifiers |
| 10 | Poison/rejected messages logged without body | MUST | N/A | The app handles no messages (`WolverineConfiguration.cs`: "The app sends no messages yet") |
| 11 | Capability URLs never logged | MUST | N/A | No capability URLs |
| 12 | No full URLs with query strings in logs | MUST | ✅ | No request-logging middleware; ASP.NET hosting request lines (which include the query) are suppressed by the Warning level. Traces carry `url.query`; OpenTelemetry.Instrumentation.AspNetCore 1.19.0 redacts query values by default as far as I know (verify) |
| 13 | Changes and admin actions record actor and time | MUST | ⚠️ | Marten: `NoteCreated` has owner and time; `NoteEdited`/`NoteArchived` have time only. The actor is the owner because `NoteRules.IsVisibleTo` allows nobody else, but it is not recorded. EF Core: timestamps on the row, no actor history (`data-inventory.md:69-71`). No admin write actions exist |
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
