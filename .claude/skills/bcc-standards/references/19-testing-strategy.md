# 19 Testing Strategy

This standard sets which kinds of tests we write, what each layer is for and how the test infrastructure is built. Most business rules should be checked by fast tests without mocks, the HTTP pipeline and database by integration tests against real Postgres, and only a few critical flows end to end. Guard tests turn architecture and security rules into failing builds, which matters most when agents write much of the code.

## Rules

### Layers

1. **Business rules MUST be unit-tested as pure functions, without mocks.** Put decisions in static `Validate`/`Post`/`*BusinessLogic` functions that take their inputs (including `now`) as parameters, and call them directly from the test. A mock-heavy test checks the wiring, not the rule, and breaks on every refactor.
2. **EF Core InMemory MUST NOT be used to test query behaviour.** It differs from Postgres in translation, constraints, transactions and collation, so a passing test says little. Query logic belongs in integration tests against Testcontainers Postgres.
3. **Integration tests MUST run the real host with Alba against Testcontainers Postgres.** Each test gets a clean database: either a migrated template database cloned per test (`CREATE DATABASE ... TEMPLATE`) or a reset per test. Split slow suites into parallel collections, each with its own container, balanced by run time.
4. **Integration tests that trigger cascaded messages MUST wait for them with Wolverine tracked sessions** (`ExecuteAndWaitAsync`/`TrackActivity`). Sleeping or polling makes tests slow and flaky.
5. **External services MUST be replaced in integration tests: by the official emulator where one exists, otherwise by a substitute.** Emulators (Core API, Firebase, Pub/Sub, Cloud Tasks) catch contract mistakes that a hand-written fake hides.
6. **A small set of E2E tests MUST cover the critical user flows on the real Aspire graph with Playwright.** Start the AppHost with a lean E2E topology flag (`--E2E=true` or `--environment=TEST`) and gate the first test on explicit readiness checks: `/health`, OIDC discovery, the Vite dev server and a seed-status endpoint. Resource state "Running" is not enough; the privacy repo traced flaky tests to this (#404).
7. **CI MUST upload Playwright traces and screenshots when an E2E test fails.** Without them a CI-only failure cannot be diagnosed.
8. **Frontend logic SHOULD be tested with vitest next to the source file**, with dependencies (fetch, cookies, navigation, realtime sources) injected so tests need no browser globals. See [21 Frontend](21-frontend.md).
9. **Load tests SHOULD be written in k6 with thresholds and a correctness check, versioned with the code, and run before major releases and events.** The check compares what the system stored with what k6 sent (vote's `/api/loadtest/verify`), so a fast but wrong system fails.

### Guard tests

10. **Architecture rules MUST be architecture tests.** Static slices, no `*Service`/`*Repository`, sealed messages, no direct clock and similar rules are listed in [25 Code Quality Guards](25-code-quality-guards.md).
11. **Event-sourced projections MUST have a coverage test: every event is handled by every projection or listed as ignored with a reason.** Marten skips unhandled events without an error. Applies only to event-sourced modules.
12. **Authorization MUST be guarded by permission population tests and an endpoint policy sweep** as described in [15 Authorization and Permission Checks](15-authorization-and-permission-checks.md).
13. **Privacy guarantees MUST have leak probes** that crawl read models, realtime documents and exports for personal data that should not be there. See [18 Privacy and GDPR](18-privacy-and-gdpr.md).
14. **E2E flows SHOULD run axe WCAG 2.1 A/AA checks at each main state, with an explicit exclusion list where each entry carries a reason.** An empty list is the goal; an exclusion is only for markup we cannot change.
15. **Concurrency-critical flows MUST have race or invariant tests** that run many concurrent requests and assert the invariant afterwards (vote: 5,000 concurrent votes against a close or reset).

### Conventions

16. **Time MUST come from `TimeProvider` (or a database clock) and tests MUST use a fake clock.** Direct `DateTime.UtcNow`/`DateOnly.Today` gives tests that fail at certain times of day (privacy has one that fails between midnight and 02:00 CEST). See [11 Domain Modelling and Errors](11-domain-modelling-and-errors.md).
17. **Test data MUST be realistic, generated with fixed seeds, and use named personas shared by local dev, E2E and screenshots.** Fixed seeds make failures reproducible; shared personas mean a bug seen locally can be replayed in a test.
18. **Test names MUST read as sentences, and test bodies SHOULD follow Given/When/Then** as described in Testing Strategy. Example: `A_close_in_the_middle_of_5000_votes_counts_exactly_the_accepted_ones`.
19. **Test projects MUST use xUnit v3 on Microsoft.Testing.Platform with AwesomeAssertions.** Select the runner in `global.json` (`"test": { "runner": "Microsoft.Testing.Platform" }`).
20. **Coverage SHOULD be measured and reported, but MUST NOT be a merge gate.** A threshold rewards tests that execute code without asserting anything.

## Example

`api/tests/BccCode.Template.Api.Marten.Tests/`, `api/tests/BccCode.Template.Api.EfCore.Tests/` (unit, Testcontainers integration), `api/tests/BccCode.Template.ArchitectureTests/` (guards), `frontend/` (vitest next to source), Playwright E2E with axe in `frontend/e2e/`, run against `aspire run` (the AppHost also has an `E2E=true` static topology).
