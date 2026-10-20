# AGENTS Guide

The guide for agents and developers working in this repository. `CLAUDE.md` only imports this file. Keep it lean: link to
docs, specs and ADRs instead of copying them.

This repository is a template for new BCC applications. It holds two sample apps that differ only in persistence:
**Marten** (documents and event sourcing) and **EF Core** (relational). `scripts/init-app.ps1` turns a copy into a real app:
it keeps one variant and renames `BccCode.Template` to `BccCode.<App>`. See [ADR 0003](docs/adrs/0003-marten-or-ef-core.md)
for how to choose.

## Start here

- `api/BccCode.Template.Api.Marten/Program.cs` and `api/BccCode.Template.Api.EfCore/Program.cs`: composition roots.
- `api/BccCode.Template.Web/`: shared HTTP plumbing — BFF authentication, authorization conventions, security headers and
  CSP, CSRF, rate limiting, ProblemDetails, SPA hosting. `TemplateWeb.cs` is the entry point.
- `api/BccCode.Template.Api.*/Features/`: vertical slices (`Notes`, `CurrentUser`, `Admin`). One slice per file.
- `api/AppHost/Program.cs`: the local environment (Postgres, Pub/Sub emulator, `tools/pubsub-setup/`). `api/OidcMock/`: mock identity provider with test personas.
- `frontend/src/`: Vue SPA, organised by feature like the backend slices.
- `docs/`: [development](docs/development.md), [ADRs](docs/adrs/), [architecture](docs/architecture/),
  [runbooks](docs/runbooks/), [privacy](docs/privacy/data-inventory.md), [tech debt](docs/tech-debt/).
- The BCC Software Development Standards are in the `bcc-standards` skill (`.claude/skills/bcc-standards/`). Follow them for
  every change; deviations need a `// why` comment or an ADR.

## Commands

```bash
dotnet tool restore                                  # aspire, dotnet-ef
pnpm --dir frontend install
aspire run                                           # everything: Postgres, Pub/Sub emulator, schema, seed, OIDC mock, API, frontend
aspire run --isolated                                # in a git worktree or a second checkout — always
dotnet build BccCode.Template.slnx                   # also rewrites api/openapi/openapi.json (Debug)
dotnet test --solution BccCode.Template.slnx         # all backend tests (Docker must run: Testcontainers)
dotnet format BccCode.Template.slnx --verify-no-changes
pnpm --dir frontend run generate:api                 # regenerate the TypeScript client from the committed OpenAPI
pnpm --dir frontend run lint && pnpm --dir frontend run format:check && pnpm --dir frontend run type-check
pnpm --dir frontend run test && pnpm --dir frontend run knip
E2E_BASE_URL=https://template.dev.localhost:<port> pnpm --dir frontend run e2e   # against a running app; axe in every flow
```

The AppHost setting `Template:Persistence` (`marten`, default, or `efcore`) picks which API runs. Fixed ports of the
non-isolated instance: API 18000 (`https://template.dev.localhost:18000`), OIDC mock 18002, Postgres 18010, Pub/Sub emulator 18020,
dashboard 18090. The dashboard command "Durability pass" on `api` stands in for Cloud Scheduler.

Personas in the OIDC mock: `user` (member, owns seeded notes), `user2` (second member, for ownership tests), `admin` (admin
role).

## Rules that matter

Each rule says why, and which test or guard enforces it.

- **Every endpoint requires a signed-in user by default.** The fallback policy requires a `sub` claim. `[AllowAnonymous]` is
  allowed only for routes listed in `AnonymousRouteGuard`; startup fails otherwise (`AssertTemplateRoutes()`).
- **`/api/admin/**` requires the admin policy**, applied by convention. An empty `Auth:AdminRole` denies everyone. The
  endpoint policy sweep tests check every route against its prefix.
- **A note the caller cannot see is 404, on reads and writes, and a write to it changes nothing.** Returning 403 would reveal
  that the id exists. Ownership tests in each variant's test project check that nothing was written.
- **Authentication is a BFF**: opaque `__Host-` cookie, server-side session store, PKCE, PAR, no tokens in the browser.
  Logout is `POST /user/logout` with the CSRF token, never a GET. See [ADR 0004](docs/adrs/0004-bff-authentication-with-server-side-sessions.md).
- **Wolverine runs `ServerlessWithOutbox` in every environment** ([ADR 0007](docs/adrs/0007-wolverine-serverless-with-outbox.md)).
  A message a handler or endpoint returns must implement `IIntegrationMessage`: only those are routed (one Pub/Sub topic,
  batch size 1), local routing is off, and the architecture tests fail on any other cascaded type and on `ScheduleAsync`,
  `DelayedFor` or local queues. The message is stored in the outbox in the request's transaction, sent after the commit and pushed
  back to `POST /internal/pubsub/messages`; handlers must be idempotent. The sample is `NoteArchivedMessage`, cascaded by the
  archive slice.
- **`/internal/**` is for Pub/Sub push and Cloud Scheduler only**: not in the OpenAPI document, no user policy. Each route has its
  own policy (`InternalPubSub`, `InternalScheduler`) on the `GoogleOidc` bearer scheme (audience, service account e-mail,
  `email_verified`). Startup fails when an internal setting is missing or a route lacks its policy (`InternalRouteGuard`). The push
  mapper refuses (400) any type that is not an `IIntegrationMessage`. `Internal:AllowAll=true` is for the local emulator only (the
  AppHost sets it); the host refuses it outside Development and Test.
- **Slices are static classes; handlers return side effects** (`IMartenOp` / EF storage actions) instead of calling the
  database, so the decision logic is testable without a database. Architecture tests check the slice shape.
- **Time comes from `TimeProvider`, never `DateTime.Now`/`UtcNow`.** The IL forbidden-API scanner in the architecture tests
  fails the build otherwise.
- **Values are parsed at the boundary**: `NoteContent.Parse` in `Validate` turns raw strings into value types or a 400 with
  i18n keys (`errors.*`).
- **The schema never changes at API startup.** Locally the `schema`/`migrate` resource runs first and the API waits for it;
  in GCP a Cloud Run job runs it before the new revision (standard 12).
- **`api/openapi/openapi.json` is the contract and is identical for both variants.** Debug builds rewrite it; CI fails when
  it changes or when the generated frontend client is stale. Both variants have a test comparing the served document with
  the committed file.
- **The frontend calls the API only through the generated client** and the one fetch wrapper that handles CSRF refresh and
  401 → login.
- **Never log the user's `sub` or other personal data.** See [the data inventory](docs/privacy/data-inventory.md).

## Working rules for agents

- In a git worktree, start the app with `aspire run --isolated` (or `aspire start --isolated`). A non-isolated run attaches
  to the shared persistent Postgres container and writes into another instance's database without any error.
- Out-of-scope findings go to `docs/tech-debt/` as one file each instead of widening the change. Subagents return their
  findings to the coordinator, which dedupes and writes the files; parallel agents otherwise write near-duplicates.
- Never delete, skip or weaken a test to make CI green. Fix the cause, or stop and report.
- Before opening a PR, run the commands above, and use the `standards-reviewer` agent on the diff for changes that touch
  auth, CI, infra or persistence.

## Gotchas

- A Debug build rewrites `api/openapi/openapi.json`. Commit it together with the regenerated frontend client.
- Release builds use Wolverine static code generation. Generate it first:
  `ASPNETCORE_ENVIRONMENT=Development ConnectionStrings__template="Host=localhost;Database=x" dotnet run --project <api> --no-build -- codegen write`,
  then build Release. The generated folder is gitignored.
- The command-line tools (`codegen`, `resources`, `migrate`, `seed`) run without Pub/Sub or `Internal:*` settings
  (`CodeGeneration.IsCommandLineTool()`); the schema step creates the envelope tables, including the outgoing `timestamp` column
  and index. Wolverine's outbox and dead-letter tables hold message bodies, so a message with personal data goes into the data inventory.
- The Marten domain event `NoteArchived` and the integration message `NoteArchivedMessage` are different things: the event is
  stored in the stream, the message leaves the service.
- Marten's `MartenOps.Append(id, expectedVersion, ...)` takes the version the stream will have **after** the append.
- Logout must be a real HTML form POST (field `__RequestVerificationToken`), because the response redirects through the
  identity provider; `fetch` cannot follow it.
- The seed subject in `appsettings.Development.json` must match `api/OidcMock/Personas.cs`; a test checks it.
- Seeding runs only in the environments allowed by `SeedingGate` (Development, Test, Sandbox, Preview).
- The AppHost and E2E test projects have no NuGet lock file: the Aspire SDK pulls packages for the host OS.

## Skills and agents

Project skills are in `.claude/skills/` and agents in `.claude/agents/`.

| Name | Use when |
|---|---|
| `bcc-standards` (skill) | Writing or changing code, config, CI, infra or docs, adding an endpoint, slice, migration, test or workflow, or checking whether something follows the BCC standards. Rules 01–26 are in `references/`. |
| `standards-reviewer` (agent) | Reviewing the current diff or a path against the standards. Read-only. Returns findings by severity with file:line and rule id. |
| `aspire` | Anything about the AppHost or Aspire CLI. Routes to the three skills below. |
| `aspire-orchestration` | Starting, stopping, waiting for or restarting the app, `--isolated` worktree runs, file-lock and port problems. |
| `aspire-monitoring` | Reading resource state, logs, traces and metrics of the running app. |
| `aspire-deployment` | `aspire publish`/`deploy` experiments. Production deploys use Terraform and GitHub Actions. |
| `dotnet-inspect` | Looking up .NET package and API facts instead of guessing. |
| `playwright-cli` | Driving a browser from the command line for checks, tests and captures. |
| `screenshot` | Capturing screenshots with committed scripts, for PRs, bug reports and docs images. |
| `pr` | Opening a pull request, with before/after screenshots when the frontend changed. |
| `permission-tests` | Writing or reviewing authorization tests: endpoint policy sweep, role population tests, ownership 404 tests. |
| `writing-user-docs` | Writing end-user documentation in `docs/user-manual/` (Diátaxis). |
| `clickup-tasks` | Filing work as ClickUp tasks. Fill in the skill's Config section first. |
| `clickup-work` | Starting work from a ClickUp task id or link, and keeping the task in step with the PR. |

## Deployment

GCP: Cloud Run API, Cloud SQL Postgres, a Cloud Run job for migrations, frontend assets in a private bucket behind the BCC
platform gateway. `deploy.yml` builds the image once after green CI on `main`, deploys and smoke-tests sandbox, then promotes
the same digest to prod after approval. Infrastructure is Terraform in `infra/`. Details: [development](docs/development.md),
[infra/README.md](infra/README.md), [rollback](docs/runbooks/rollback.md).

## Team process

- Branch names: `<type>/<short-description>` (for example `feat/note-sharing`, `fix/archive-409`), or the ClickUp task id
  when the work comes from a task.
- Work items live in ClickUp (`clickup-tasks`, `clickup-work` skills). Architecture decisions are ADRs in `docs/adrs/`.
