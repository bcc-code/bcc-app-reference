# Tests

```bash
dotnet test --solution BccCode.Template.slnx           # all backend tests (Docker must run)
pnpm --dir frontend run test                            # frontend unit tests (vitest)
```

| Project | What it covers | Needs |
|---|---|---|
| `api/tests/BccCode.Template.Tests` | Unit tests: `Permission`, `Validate` and `Handle` called directly, value types, the entity methods | Nothing |
| `api/tests/BccCode.Template.IntegrationTests` | The real host with Alba against Testcontainers Postgres: endpoints, the endpoint policy sweep, ownership (403 for a foreign note, 404 for an unknown one, and nothing written), push receivers, the committed OpenAPI document | Docker |
| `api/tests/BccCode.Template.ArchitectureTests` | Slice shape (static endpoint classes, sealed records, no `*Service` or `*Repository`), message markers, layering between the feature project and the host, no background work, logging rules | Nothing (reads the built assemblies) |
| `api/tests/BccCode.Template.E2eTests` | .NET Playwright against the AppHost (`E2E=true`), with an axe check of every main state | Docker, a built `frontend/dist` |

The integration tests sign in through real cookie sessions (`AsUser`, `As(personUid)`). There is no Pub/Sub emulator: messaging runs on
Wolverine's local inbox, and push bodies are posted to the receivers ([ADR 0017](../adrs/0017-integration-and-e2e-tests.md)).

## How the integration fixtures work

- `PostgresFixture` (assembly fixture, one per run) starts one `postgres:18` container with a tmpfs data directory, `fsync=off`,
  `synchronous_commit=off`, `full_page_writes=off` and `max_connections=1000`. It applies the schema once to a template database
  with `jasper db-apply` (the EF migrations, then Wolverine's tables) and marks it `IS_TEMPLATE`.
- `AppFixture` (class fixture, one per test class) clones the template with `CREATE DATABASE ... TEMPLATE` and starts its own Alba host
  on the clone. Classes run in parallel because they share only the server. The clone is dropped when the class is done.
- `ResetDataAsync` runs before every test. In one transaction with `session_replication_role = replica` it deletes the rows of the tables
  that hold any (notes and history, changes, sign-in sessions, Wolverine's inbox, outbox and dead letters) and restarts the sequences
  that handed out a value. It keeps `data_protection_keys` (a running host holds its key ring in memory and does not write the keys
  again), the migration history and Wolverine's node tables.

### Write a test class

```csharp
public sealed class MyTests(AppFixture fixture) : AppTest(fixture)
{
    [Fact]
    public async Task Something()
    {
        var result = await AsUser(s => s.Get.Url("/api/notes"));
    }
}
```

- Derive from `AppTest`; it brings the class fixture, `Host`, `Fixture`, the reset and the persona helpers. Do not add a collection:
  the class then runs in parallel with the others.
- Everything in the class shares one host and one database. A test that needs other settings or services starts its own host with
  `Fixture.StartHostAsync(settings, services)`, on the same database, and disposes it.
- Make up your own users (`TestUser.New()`) rather than rely on rows from another test.
- A test that changes process-wide state (environment variables, as `SchemaCommands` does, or anything else static) goes in
  `[Collection(SerialCollectionDefinition.Name)]`. xUnit runs that collection alone after the parallel classes. `SchemaNameTests` is the only
  member now: it runs `db-apply` in the test process with the schema setting in environment variables.

### Timings

On the developer machine used for the change, the 113 integration tests took about 40 s before (one serial collection, one host, a
container with default settings) and about 26 s after (wall time of `dotnet test`, container start and image already pulled). Most of
what is left is `MessagingTests`, which waits for Wolverine's retry delays, and the container start.

## Run the E2E tests

```bash
pnpm --dir frontend run build
dotnet test --project api/tests/BccCode.Template.E2eTests
```

The harness starts the AppHost with random ports, waits for the schema step, the seeder's health check and the API, and uploads
traces and screenshots from CI when a test fails.

## What CI runs

The checks are defined in `.github/workflows/ci.yml`; run the same commands before you push:

- `backend`: locked `dotnet restore`, `dotnet build`, unit and architecture tests, and `dotnet format BccCode.Template.slnx --verify-no-changes`,
  then a check that the committed OpenAPI document and the generated client are current ([contract flow](contract-flow.md)). On a
  frontend-only change only the restore, the contract check and the SBOM check run.
- `frontend`: `lint`, `format:check`, `type-check`, `test`, `knip`, `build`.
- `codegen`: Wolverine code generation and a Release build ([ADR 0012](../adrs/0012-pre-generated-wolverine-code-in-production.md)).
- `architecture`: the C4 DSL validates and `docs/architecture/export.sh --check` passes.
- Terraform `fmt` and `validate` for `infra/`, `infra/preview` and `infra/preview-shared`, and `bash .github/scripts/preview-decide.test.sh`.
- `sast`: gitleaks and Semgrep; the supply-chain workflow is separate.
- `integration` and `e2e`: the integration tests and the Playwright tests above, each in a job of its own.
- `ci`: aggregates all jobs and is the one check to require.

Never delete, skip or weaken a test to make CI green. Fix the cause, or stop and report.
