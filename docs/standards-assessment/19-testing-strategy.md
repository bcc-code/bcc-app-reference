---
standard: 19 Testing Strategy
assessed: 2026-10-19
commit: e4b2b86
updated: 2026-10-20 (ServerlessWithOutbox, ADR 0007)
---
# 19 Testing Strategy — assessment

The test layers are in place: pure slice tests, Alba + Testcontainers integration tests for both variants, architecture
guards, vitest next to the source, and Playwright E2E with axe on the Aspire E2E topology. The messaging change added a
Pub/Sub emulator to the integration tests and the AppHost, messaging and internal-route tests in both variants, internal
authentication tests and architecture tests for messages. Its tests wait for messages by polling the emulator instead of
Wolverine tracked sessions, because the message leaves the process. Gaps are in isolation (one shared
database per suite), E2E readiness gating, race tests for the Marten variant, the breadth of leak probes, load tests and
coverage reporting.

**Score: Adequate** · ✅ 12 · ⚠️ 7 · ❌ 1 · N/A 0

Commands run on 2026-10-19 (Docker available): `dotnet test --project api/tests/BccCode.Template.Web.Tests` — 78 passed;
`dotnet test --project api/tests/BccCode.Template.Api.Marten.Tests` — 59 passed; `dotnet test --project
api/tests/BccCode.Template.Api.EfCore.Tests` — 63 passed; `pnpm --dir frontend run test` — 36 passed in 9 files. E2E and
architecture tests were not run. Verification of the messaging change on 2026-10-20 (reported by the change, not repeated for
this update): full build with 0 warnings, all backend tests pass, E2E 6/6 in both variants with the Pub/Sub push path.

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | Business rules unit-tested as pure functions, no mocks | MUST | ✅ | `NoteRules` and the slices' `Validate`/`Post` take their inputs; `NoteSliceTests` calls them directly (e.g. `An_edit_checks_visibility_first_then_freshness_then_the_input`, `An_edit_appends_to_the_stream_at_the_version_that_was_read`). No mocking library is referenced |
| 2 | No EF Core InMemory for query behaviour | MUST | ✅ | `UseInMemoryDatabase` appears only in `api/OidcMock/Program.cs:21` (the mock's OpenIddict store, not a test) |
| 3 | Alba + Testcontainers Postgres; clean database per test | MUST | ⚠️ | `AppFixture.cs` in both variant test projects: `AlbaHost.For<Program>` on `postgres:18` via Testcontainers. One database per collection, shared by all tests; isolation comes from a fresh random user per test (`TestUser.New()`), not a template clone or reset. One collection (`AppCollectionDefinition`), so no parallel split |
| 4 | Wait for cascaded messages with tracked sessions | MUST | ⚠️ | The archive slice cascades `NoteArchivedMessage`. `MessagingTests` (both variants) does not use tracked sessions: it pulls from its own emulator subscription in a 100 ms polling loop with a 30 s deadline (`PullAsync`), posts the push itself, and `A_refused_archive_publishes_nothing` waits a fixed `Task.Delay(500)` before asserting that nothing arrived. A tracked session cannot follow a message through Pub/Sub and back, which the class comment explains, but the standard names polling and sleeping as what to avoid. The handler step itself is checked synchronously after the push returns |
| 5 | External services replaced by emulator or substitute | MUST | ✅ | Integration tests replace OIDC with a header scheme (`AppFixture.cs`); E2E signs in through `api/OidcMock` (OpenIddict). Pub/Sub is the official emulator in both places: a Testcontainers `PubSubEmulator` started next to Postgres (`api/tests/*/Infrastructure/PubSubEmulator.cs`, `AppFixture.InitializeAsync`) and a pinned emulator container plus `tools/pubsub-setup/setup.sh` in the AppHost, so local runs and E2E use the push path. Google OIDC tokens for `/internal` are substituted by tokens signed with a local key (`InternalAuthTests`) |
| 6 | Few E2E flows on the Aspire graph, gated on explicit readiness | MUST | ⚠️ | `frontend/e2e/notes.spec.ts` (3 tests), `admin.spec.ts` (3); CI starts `E2E=true` and runs `aspire wait api` (`pr.yml`). The API waits for schema and seed completion, for the Pub/Sub emulator (HTTP health check) and for the completed setup step, and has a `/health` check (`AppHost/Program.cs`), but OIDC discovery is not gated: `WaitFor(oidc)` waits for "running" and the mock has no health check |
| 7 | CI uploads Playwright traces/screenshots on failure | MUST | ✅ | `playwright.config.ts` `trace: 'retain-on-failure'`; `pr.yml` uploads `playwright-report/` and `test-results/` and resource logs on failure |
| 8 | vitest next to source with injected dependencies | SHOULD | ✅ | `frontend/src/lib/templateFetch.test.ts`, `logout.test.ts`, `problems.test.ts`, `router.test.ts`, `features/**/*.test.ts`; `createTemplateFetch(deps)` injects fetch, cookies, redirect and location |
| 9 | k6 load tests with thresholds and correctness check | SHOULD | ❌ | No k6 scripts in the repo |
| 10 | Architecture rules as architecture tests | MUST | ✅ | `api/tests/BccCode.Template.ArchitectureTests/` — `SliceConventionTests` (static slices, no services/repositories, sealed records, feature folders, every cascaded message an integration message, sealed message records, no scheduling, delays or local queues), `ClockRulesTests`, `LayerRulesTests`, `LoggingRulesTests`, each with a non-vacuous guard or a scanner self-test |
| 11 | Projection coverage test for every event | MUST | ✅ | `NoteSliceTests.Every_note_event_is_handled_by_the_note_aggregate` (Marten); the inline snapshot is the only projection |
| 12 | Population tests and endpoint policy sweep | MUST | ⚠️ | Sweep present in both variants (`SecurityTests.Every_api_endpoint_has_the_policy_its_route_requires_and_none_allows_anonymous`, and `InternalRoutesTests.Every_internal_route_has_its_internal_policy_and_none_allows_anonymous_or_gets_the_user_policy` for `/internal/**`); population tests are partial, see standard 15 rule 10 |
| 13 | Leak probes over read models, realtime docs, exports | MUST | ⚠️ | Targeted tests only (`The_response_does_not_expose_the_owner`, `Me_returns_…_nothing_else`, foreign-note 404). No probe that crawls every API response or the stored projection for the owner `sub`. There are no exports or realtime documents |
| 14 | axe WCAG 2.1 A/AA at each main state, reasoned exclusions | SHOULD | ✅ | `frontend/e2e/support.ts:24-32` runs axe with `wcag2a/2aa/21a/21aa` and no exclusions; called 6 times in `notes.spec.ts`, 2 in `admin.spec.ts` |
| 15 | Race/invariant tests for concurrency-critical flows | MUST | ⚠️ | EF Core: `NotesApiTests.An_edit_that_loses_a_race_at_commit_answers_409_and_keeps_the_other_writers_change` and `PersistenceTests.The_note_version_is_a_concurrency_token_that_stops_a_second_writer`. Marten: only the pure `An_edit_appends_to_the_stream_at_the_version_that_was_read`; the 409 mapping (`MapMartenConcurrencyFailuresToConflict`, `Api.Marten/Program.cs:30-31`) has no database test. Neither variant runs many concurrent requests |
| 16 | Time from `TimeProvider`; tests use a fake clock | MUST | ✅ | `ClockRulesTests.The_application_never_reads_the_instance_clock`; `FakeTimeProvider` in `SessionTicketStoreTests` and `NotesApiTests.The_list_holds_only_the_callers_notes_newest_first` |
| 17 | Realistic generated data, fixed seeds, shared personas | MUST | ✅ | Personas `user`, `user2`, `admin` with fixed subjects (`api/OidcMock/Personas.cs`), used by E2E (`e2e/support.ts`), the seeder (`The_seeded_user_is_the_one_the_oidc_mock_signs_in`) and the screenshot skill (`.claude/skills/screenshot/scripts/login.js`); seed notes have fixed ids |
| 18 | Sentence test names; Given/When/Then bodies | MUST/SHOULD | ✅ | e.g. `Every_write_to_an_unknown_or_foreign_note_answers_404_and_stores_nothing`; bodies are arrange/act/assert separated by blank lines |
| 19 | xUnit v3 on Microsoft.Testing.Platform with AwesomeAssertions | MUST | ✅ | `global.json` `"test": { "runner": "Microsoft.Testing.Platform" }`; test projects reference `xunit.v3` and `AwesomeAssertions` (e.g. `BccCode.Template.Api.Marten.Tests.csproj`) |
| 20 | Coverage measured and reported, never a gate | SHOULD/MUST NOT | ⚠️ | Not a gate (no threshold anywhere). Not measured either: no coverage collector in `pr.yml` and no vitest coverage config |

## Gaps to close

- **3**: Give each test a clean database: create a migrated template database once per fixture and `CREATE DATABASE … TEMPLATE`
  per test (or reset per test), in both `api/tests/*/Infrastructure/AppFixture.cs`. Split collections if run time grows.
- **6**: Add an OIDC discovery readiness gate before the first E2E test: a `WithHttpHealthCheck("/.well-known/openid-configuration")`
  on the `oidc` resource in `api/AppHost/Program.cs`, or a Playwright `globalSetup` that polls `/health` and the discovery
  document.
- **15**: Add a Marten integration test that forces a concurrent append and asserts 409 and the other writer's change (the
  Marten counterpart of `RacingWriter`), and a test that fires many concurrent edits at one note and asserts exactly one wins
  per version.
- **13**: Add a leak probe that signs in as each persona, calls every GET endpoint from `EndpointDataSource`, and asserts that no
  response contains any persona's `sub`; optionally scan the stored documents for fields other than `OwnerId` holding it.
- **4**: Where a test only needs to know that a message was handed to the outbox, assert it with a Wolverine tracked session
  on the HTTP call (the outgoing envelope) and keep the emulator round trip for one end-to-end test. Replace the fixed
  `Task.Delay(500)` in `A_refused_archive_publishes_nothing` with an assertion on the outbox or the tracked session, which
  shows directly that nothing was written.
- **12**: See standard 15, rule 10.
- **20**: Collect coverage in CI (`--coverage` with Microsoft.Testing.Platform's coverage extension, `vitest --coverage`) and
  publish it as an artifact or summary, without a threshold.
- **9**: Add a k6 script under `loadtest/` with thresholds and a verification step for the notes write path before the first
  large event.

## Not verifiable in the repo

- Whether the E2E job is stable in CI over time (retries are set to 1 on CI in `playwright.config.ts`); check the Actions
  history once the repo has runs.
