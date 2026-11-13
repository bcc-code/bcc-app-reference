# Tests

```bash
dotnet test --solution BccCode.Template.slnx           # all backend tests (Docker must run)
pnpm --dir frontend run test                            # frontend unit tests (vitest)
```

| Project | What it covers | Needs |
|---|---|---|
| `api/tests/BccCode.Template.Tests` | Unit tests: `Permission`, `Validate` and `Handle` called directly, value types, the `Change` object | Nothing |
| `api/tests/BccCode.Template.IntegrationTests` | The real host with Alba against Testcontainers Postgres: endpoints, the endpoint policy sweep, ownership (403 for a foreign note, 404 for an unknown one, and nothing written), history triggers, push receivers, the committed OpenAPI document | Docker |
| `api/tests/BccCode.Template.ArchitectureTests` | Slice shape (static endpoint classes, sealed records, no `*Service` or `*Repository`), message markers, layering between the feature project and the host, no background work, logging rules | Nothing (reads the built assemblies) |
| `api/tests/BccCode.Template.E2eTests` | .NET Playwright against the AppHost (`E2E=true`), with an axe check of every main state | Docker, a built `frontend/dist` |

The integration fixture applies the schema as `db-apply` does and signs in through real cookie sessions (`AsUser`, `As(personUid)`),
and every test starts from empty data. There is no Pub/Sub emulator: messaging runs on Wolverine's local inbox, and push bodies are
posted to the receivers ([ADR 0017](../adrs/0017-integration-and-e2e-tests.md)).

## Run the E2E tests

```bash
pnpm --dir frontend run build
dotnet test --project api/tests/BccCode.Template.E2eTests
```

The harness starts the AppHost with random ports, waits for the schema step, the seeder's health check and the API, and uploads
traces and screenshots from CI when a test fails.

## What CI runs

The checks are defined in `.github/workflows/ci.yml`; run the same commands before you push:

- Backend: locked `dotnet restore`, `dotnet build`, unit and architecture tests, and `dotnet format BccCode.Template.slnx --verify-no-changes`.
- Frontend: `lint`, `format:check`, `type-check`, `test`, `knip`, `build`, and a check that the committed OpenAPI document and the
  generated client are current ([contract flow](contract-flow.md)).
- `codegen`: Wolverine code generation and a Release build ([ADR 0012](../adrs/0012-pre-generated-wolverine-code-in-production.md)).
- `architecture`: the C4 DSL validates and `docs/architecture/export.sh --check` passes.
- Terraform `fmt` and `validate` for `infra/`, `infra/preview` and `infra/preview-shared`, and `bash .github/scripts/preview-decide.test.sh`.
- `gitleaks`, `sast` (Semgrep) and the supply-chain workflow.
- `integration` and `e2e`: the integration tests and the Playwright tests above, each in a job of its own.
- `ci`: aggregates all jobs and is the one check to require.

Never delete, skip or weaken a test to make CI green. Fix the cause, or stop and report.
