# 19 Testing Strategy

This standard sets which kinds of tests we write, what each layer is for and how the test infrastructure is built. Most business rules should be checked by fast tests without mocks, the HTTP pipeline and database by integration tests against real Postgres, and only a few critical flows end to end. Guard tests turn security rules into failing builds, which matters most when agents write much of the code.

## Rules

### Layers

1. **Business rules MUST be unit-tested as pure functions, without mocks.** Put decisions in static `Validate`/`Post`/`*BusinessLogic` functions that take their inputs (including `now`) as parameters, and call them directly from the test. A mock-heavy test checks the wiring, not the rule, and breaks on every refactor.
2. **EF Core InMemory MUST NOT be used to test query behaviour.** It differs from Postgres in translation, constraints, transactions and collation, so a passing test says little. Query logic belongs in integration tests against Testcontainers Postgres.
3. **Integration tests MUST run the real host with Alba against Testcontainers Postgres.** Each test gets a clean database: either a migrated template database cloned per test (`CREATE DATABASE ... TEMPLATE`) or a reset per test. Split slow suites into parallel collections, each with its own container, balanced by run time.
4. **Integration tests that trigger cascaded messages MUST wait for them with Wolverine tracked sessions** (`ExecuteAndWaitAsync`/`TrackActivity`). Sleeping or polling makes tests slow and flaky.
5. **External services MUST be replaced in integration tests: by an emulator where one exists, otherwise by a substitute.** Emulators (the Pub/Sub emulator, BCC's own Core API emulator and, for apps that use it, the Firebase emulator) catch contract mistakes that a hand-written fake hides. Where no emulator exists (for example Cloud Tasks), use a small in-memory substitute that records the calls.
6. **A small set of E2E tests MUST cover the critical user flows on the real Aspire graph with Playwright.** Start the AppHost with a lean E2E topology flag (`--E2E=true` or `--environment=TEST`) and gate the first test on explicit readiness checks: `/health`, OIDC discovery, the Vite dev server and a seed-status endpoint. Resource state "Running" is not enough; tests that start before seeding has committed fail intermittently.
7. **CI MUST upload Playwright traces and screenshots when an E2E test fails.** Without them a CI-only failure cannot be diagnosed.
8. **Frontend logic MUST be tested with vitest next to the source file**, with dependencies (fetch, cookies, navigation, realtime sources) injected so tests need no browser globals. See [21 Frontend](21-frontend.md).
9. **Load tests SHOULD be written in k6 with thresholds and a correctness check, versioned with the code, and run before major releases and events.** The check compares what the system stored with what k6 sent (for example a verify step that queries the database after the run), so a fast but wrong system fails.

### Guard tests

10. **Event-sourced projections MUST have a coverage test: every event is handled by every projection or listed as ignored with a reason.** Marten skips unhandled events without an error. Applies only to event-sourced modules.
11. **Authorization MUST be guarded by permission population tests and an endpoint policy sweep** as described in [15 Authorization and Permission Checks](15-authorization-and-permission-checks.md).
12. **Where a feature promises that personal data is hidden, it MUST have leak probes** that crawl read models, client-readable stores and exports for personal data that should not be there. This matches [18 Personal Data and GDPR](18-personal-data-and-gdpr.md) rule 8. See [18 Personal Data and GDPR](18-personal-data-and-gdpr.md).
13. **E2E flows MUST run axe WCAG 2.1 A/AA checks at each main state, and SHOULD also include the WCAG 2.2 AA rules, with an explicit exclusion list where each entry carries a reason.** 2.1 AA is the floor, because Norwegian accessibility regulation refers to it; 2.2 is the current W3C version. An empty list is the goal; an exclusion is only for markup we cannot change.
14. **Concurrency-critical flows MUST have race or invariant tests** that run many concurrent requests and assert the invariant afterwards (for example many concurrent submissions against a close or reset action).

### Conventions

15. **Time MUST come from `TimeProvider` (or a database clock) and tests MUST use a fake clock.** Direct use of `DateTime.Now`, `DateTime.UtcNow`, `DateTime.Today`, `DateTimeOffset.Now` or `DateTimeOffset.UtcNow` gives tests that fail at certain times of day (when the UTC and local dates differ). See [11 Domain Modelling and Errors](11-domain-modelling-and-errors.md).
16. **Test data MUST be realistic and generated with fixed seeds. It SHOULD use named personas shared by local dev, E2E and screenshots.** Fixed seeds make failures reproducible; shared personas mean a bug seen locally can be replayed in a test.
17. **Test names MUST read as sentences, and test bodies SHOULD follow Given/When/Then.** Example: `A_close_in_the_middle_of_many_submissions_counts_exactly_the_accepted_ones`.
18. **Test projects MUST use xUnit v3 on Microsoft.Testing.Platform with AwesomeAssertions.** Select the runner in `global.json` (`"test": { "runner": "Microsoft.Testing.Platform" }`).
19. **Coverage SHOULD be measured and reported, but MUST NOT be a merge gate.** A threshold rewards tests that execute code without asserting anything.

## Examples

### Pure unit test of a slice (rule 1)
```csharp
[Fact]
public void Creating_starts_a_draft_project_stream_with_the_initial_defaults()
{
    var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(AppClaims.PersonUid, Samples.AdminUid)], "test"));

    var (result, start) = CreateProjectEndpoint.Post(Samples.CreateProject(), new DbNow(DateTimeOffset.UnixEpoch), user);

    start.StreamKey.Should().Be(result.ProjectId);
    start.Events.Should().ContainSingle().Which.Should().BeEquivalentTo(new ProjectCreated(...));
}
```
The same shape applies to a `*BusinessLogic` test: call the static function directly with its inputs.

### Template database per test (rule 3)
```csharp
/// <summary>Clones the migrated template into a new database.</summary>
public Task CreateDatabaseFromTemplateAsync(string databaseName) =>
    ExecuteAdminAsync($"CREATE DATABASE \"{databaseName}\" TEMPLATE \"{TemplateDbName}\";");
```

### Parallel collections (rule 3)
The fixture documents its split: "The postgres tests are split into three collections, each with its own container, so that they run in parallel. They are balanced by run time." Emulator fixtures (Core API, Firebase, Pub/Sub) sit next to it.

### Awaiting cascades (rule 4)
```csharp
var tracked = await Host.ExecuteAndWaitAsync(async () =>
{
    result = await Host.Scenario(configuration);
}, 15000);
```

### E2E readiness gates (rule 6)
```csharp
await WaitForHttpReadyAsync("api", "/health");
await WaitForHttpReadyAsync("oidc", "/.well-known/openid-configuration");
await WaitForHttpReadyAsync("frontend", "/");
// /seed-status returns 503 until the seeded records exist and 200 once seeding commits. ...
// Without this gate, tests that start early fail intermittently.
await WaitForHttpReadyAsync("database-testdata", "/seed-status");
```

### Projection coverage guard (rule 10)
An `IntentionallyIgnored` dictionary maps `(Projection, Event)` to a reason. One test fails on any uncovered pair, a second fails on stale or reason-less entries.

### Accessibility in E2E (rule 13)
```csharp
private static readonly string[] Tags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

/// The only place where a violation may be left out. An entry is a CSS selector that axe skips, with the reason it is
/// ours to leave: third-party markup we cannot change. Empty means nothing is excluded.
private static readonly (string Selector, string Reason)[] Exclusions = [];
```
The helper waits for running animations to finish and confirms a finding with a second scan before failing.

### Load test thresholds (rule 9)
Keep scenarios in `loadtest/scenarios/`, shared options in `loadtest/lib/options.js` and a verify query in `loadtest/verify/verify.sql`.
```js
thresholds: {
  'http_req_duration{name:submit}': ['p(95)<300', 'p(99)<1000'],
  checks: ['rate>0.999'],
  dropped_iterations: ['count==0'],
},
```

### Race tests (rule 14)
An integration test fires thousands of concurrent requests while a close or reset runs, then asserts that the stored count equals the number of accepted requests.

## Related
- [11 Domain Modelling and Errors](11-domain-modelling-and-errors.md) — time and pure functions
- [15 Authorization and Permission Checks](15-authorization-and-permission-checks.md) — policy sweep and permission tests
- [18 Personal Data and GDPR](18-personal-data-and-gdpr.md) — leak probes and test data
- [20 Screenshots and Visual Documentation](20-screenshots-and-visual-documentation.md) — shared personas
- [21 Frontend](21-frontend.md) — vitest and frontend logic
- [25 Code Quality Guards](25-code-quality-guards.md) — analyzers
- [05 CI Pipeline](05-ci-pipeline.md) — test jobs and artifacts
