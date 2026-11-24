# 0017. Integration tests with Testcontainers, E2E tests with Playwright on the AppHost

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADR 0019.

## Context

With a database, query behaviour, the schema step and the host's wiring cannot be checked by unit tests alone. Standard 19 requires integration tests that run the real host with Alba against Testcontainers Postgres (19.3), and a small set of Playwright E2E tests on the real Aspire graph with readiness gates and accessibility checks (19.6, 19.7, 19.13). An earlier version of the template ran the E2E tests with `@playwright/test` from the frontend folder.

## Decision

- **Unit tests** stay in `api/tests/BccCode.Template.Tests`, without Docker.
- **Integration tests** go in `api/tests/BccCode.Template.IntegrationTests`: xUnit v3, Alba and Testcontainers Postgres. One Postgres container per run (tmpfs, `fsync=off`) holds a template database that `db-apply` migrated once; each test class clones it and starts its own Alba host, so classes run in parallel, and only tests that change process-wide state share a serial collection (see [tests](../development/tests.md)). The tests sign in through real cookie sessions in the session store (`AsUser`, `As(personUid)`), and each test starts from empty data. Messaging uses Wolverine's local inbox, so no Pub/Sub emulator is needed ([0023](0023-pubsub-for-internal-messages-and-bcc-pubsub-for-events.md)). Push bodies are posted to the receivers, so the push path is tested too.
- **E2E tests** go in `api/tests/BccCode.Template.E2eTests`: xUnit v3, .NET Playwright and `Aspire.Hosting.Testing`. The harness starts the AppHost with `E2E=true` (built `frontend/dist`) and waits for the schema step, the seeder's health check (healthy only after the seed has committed) and the API's health check before the first test. Each main state gets an axe WCAG 2.1 A/AA and 2.2 AA check. CI uploads traces and screenshots when a test fails.
- **Architecture tests** in `api/tests/BccCode.Template.ArchitectureTests` check the slice shape, the message markers, the layering and that no code runs between requests. Forbidden APIs (the clock, local queues) are build errors through `BannedSymbols.txt`.
- The CI workflow runs them as separate jobs, required through the aggregate `ci` job.

## Consequences

| | |
|---|---|
| Positive | Real Postgres in tests, so queries, triggers and the schema step are checked as they run in production. One AppHost serves local runs and E2E. |
| Negative | Integration and E2E jobs need Docker and take minutes, not seconds. E2E tests need a built frontend first. |
| Follow-up | An application adds a flow to the E2E tests with every page it builds. |

## Alternatives considered

- **`@playwright/test` in `frontend/e2e`** — closer to the Vue code, but it cannot wait on Aspire resource health directly.
- **EF Core InMemory or a mocked session** — forbidden for query behaviour by standards 12.15 and 19.2. Postgres behaviour such as the concurrency token's `WHERE` clause and unique keys is only checked on the real database.
