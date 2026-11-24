# AGENTS Guide

The guide for agents and developers working in this repository. `CLAUDE.md` only imports this file. Keep it lean: link to
docs, specs and ADRs instead of copying them.

BCC App Template: a template for new BCC applications. .NET 11 backend (Wolverine.HTTP, EF Core on Postgres), Vue 3 frontend (BCC
component library, Tailwind 4, TanStack Query, generated Hey API client), orchestrated with .NET Aspire. One sample app, the
personal Notes, with optimistic concurrency (`version`) and soft removal (archiving); there is no audit trail or history yet
([tech debt](docs/tech-debt/audit-history-not-in-the-template.md)). `scripts/init-app.ps1` turns a copy into a real app: it
renames `BccCode.Template` to `BccCode.<App>` and moves the ports. Standards live in `.claude/skills/bcc-standards`. Wolverine
rules and where this repo applies them are in the `wolverine` skill. Decisions are ADRs in `docs/adrs/`.

## Start Here

- `api/BccCode.Template.Api/Program.cs` is the host's composition root: no business logic. The host holds what only it uses: BFF
  authentication (`Auth/`), authorization conventions and the endpoint `Permission` step (`Configuration/`), security headers and CSP,
  the cross-origin check, ProblemDetails, SPA hosting (`Frontend/`), Pub/Sub and BCC PubSub (`Internal/`, `Messaging/`) and
  Wolverine settings. `Configuration/TemplateWeb.cs` is the entry point (ADR 0025).
- `api/BccCode.Template/` holds the features, one folder per feature and one file per endpoint (`Notes/EditNote.cs` holds
  `EditNoteEndpoint`): `Notes/`, `Users/`, `Admin/`, the `TemplateDb` context with its migrations (`Persistence/`). `api/BccCode.Template.TestData/` is the seeder (`SeedingGate`, health = seeded, `Seeder:Once`).
- Tests mirror the folders: unit tests in `api/tests/BccCode.Template.Tests/` (no Docker), integration tests in
  `api/tests/BccCode.Template.IntegrationTests/` (Alba + Testcontainers Postgres), architecture tests in
  `api/tests/BccCode.Template.ArchitectureTests/`, E2E tests in `api/tests/BccCode.Template.E2eTests/` (Playwright on the AppHost, ADR 0017).
- `api/AppHost/Program.cs` is the local topology: Postgres (+ DBX), the `schema` step (`jasper db-apply`), the `testdata` seeder, the
  `oidc` mock (`api/OidcMock/`, personas in `Personas.cs`), the API, the Vite dev server and the `architecture` Structurizr viewer. The API starts after
  the schema step and the seeder are done. `DurabilityPassLoop.cs` runs the durability pass every few seconds.
- `frontend/src/` mirrors the backend features (`notes/`, `users/`, `admin/`), plus `layout/`, `components/`, `lib/apiFetch.ts` (the one fetch
  wrapper), `api.ts` (the client's runtime config) and `client/` (generated).
- `docs/`: [development](docs/development/README.md), [ADRs](docs/adrs/), [architecture](docs/architecture/), [runbooks](docs/runbooks/),
  [data inventory](docs/data-protection/data-inventory.md), [manual](docs/manual/), [tech debt](docs/tech-debt/). `infra/` is the Terraform for GCP, one
  file per concern ([infra/README.md](infra/README.md)).
- The BCC Software Development Standards are in the `bcc-standards` skill. Follow them for every change; a deviation needs a `// why`
  comment or an ADR.

## Commands

```powershell
dotnet tool restore
pnpm --dir frontend install      # only for frontend scripts outside Aspire; aspire run installs itself

aspire run                       # Postgres, schema, seeder, OIDC mock, API, Vite, C4 viewer; opens https://template.dev.localhost:16000 (needs Docker)
aspire run --isolated            # in a git worktree, a second checkout or for a parallel agent: always
dotnet build BccCode.Template.slnx    # warnings are errors
dotnet test --project api/tests/BccCode.Template.Tests                # unit tests, no Docker
dotnet test --project api/tests/BccCode.Template.ArchitectureTests    # needs the built assemblies, no Docker
dotnet test --project api/tests/BccCode.Template.IntegrationTests     # needs Docker (Testcontainers)
pnpm --dir frontend build; dotnet test --project api/tests/BccCode.Template.E2eTests   # starts the AppHost with E2E=true; E2E_BROWSER_CHANNEL (chrome, msedge) uses an installed browser
dotnet format BccCode.Template.slnx --verify-no-changes               # CI check; drop the flag to fix
bash .github/scripts/preview-decide.test.sh                           # the preview workflow's deploy/destroy decision

pnpm --dir frontend generate     # jasper openapi writes the OpenAPI document, then openapi-ts regenerates the client
pnpm --dir frontend openapi-ts   # the client only, from the committed document
pnpm --dir frontend type-check
pnpm --dir frontend lint         # oxlint + ESLint, check only, warnings fail
pnpm --dir frontend format       # oxfmt, writes
pnpm --dir frontend format:check # check only (CI)
pnpm --dir frontend knip         # unused files, exports, dependencies
pnpm --dir frontend test         # vitest (includes the i18n completeness test)

./docs/architecture/export.ps1   # C4 SVGs from workspace.dsl (Docker; export.sh on Linux/macOS); CI checks they are current

dotnet run --project api/BccCode.Template.Api -c Release -- jasper codegen write   # Wolverine code for Production (gitignored)
dotnet publish api/BccCode.Template.Api -c Release /t:PublishContainer              # container image; needs Docker
dotnet run --project api/BccCode.Template.Api -- jasper db-apply    # EF migrations, then Wolverine's tables, on ConnectionStrings:template; jasper db-assert checks
dotnet ef migrations add <Name> --project api/BccCode.Template --startup-project api/BccCode.Template.Api
node tools/db-snapshot/update.mjs                                   # regenerate the committed database snapshot after a migration or a seeder change (Docker)
terraform fmt -check -recursive infra
terraform -chdir=infra init -backend=false; terraform -chdir=infra validate                          # the same for infra/preview-shared and infra/preview

# refresh the Aspire skills in .claude/skills (generated, do not edit by hand); deploy, init and migration skills are left out because deploys go through Terraform and CI
aspire agent init --workspace-root . --skill-locations claudecode --skills aspire,aspire-orchestration,aspire-monitoring,dotnet-inspect --mcp --non-interactive
```

Every JasperFx command needs the `jasper` prefix (`-- jasper db-apply`, `-- jasper codegen write`, `-- jasper openapi`); any other
arguments start the host. A command-line tool runs without sign-in, messaging or `Internal:*` settings (`CodeGeneration.IsJasperFxCommand()`).

## Ports (sole instance)

| What | Port |
|---|---|
| API (https), browse at `https://template.dev.localhost:16000` | 16000 |
| Vite (behind the API, do not browse it directly) | 16001 |
| Postgres (persistent container, volume `bcc-template-postgres`) / DBX database UI | 16010 / 16011 |
| OIDC mock (`oidc`, persona picker at `/login`; https / http) | 16020 / 16021 |
| Aspire dashboard / OTLP / resource service | 16050 / 16051 / 16052 |
| Structurizr (C4 diagrams, `architecture` resource) | 16060 |

The slots 16030–16032 and 16040 are kept free for a platform mock, Mailpit and a storage emulator, so every BCC app has the same
layout. `scripts/init-app.ps1 -PortBase <port>` moves the whole range; two apps on one machine need different bases. Personas in the
OIDC mock: `user` (member, owns the seeded notes), `user2` (second member, for ownership tests), `admin` (admin role, second factor)
and `admin-no-mfa` (admin role without one: sign-in ends on the second-factor page).

### Always use `--isolated` in a git worktree and for parallel agents

Fixed ports and the persistent Postgres container belong to the one default instance; a non-isolated run in a worktree attaches to the
shared database and writes into another instance's data without any error. `aspire run --isolated` (or `aspire start --isolated`) uses
random ports, a database volume of its own (`bcc-template-<folder>-postgres`, so a restart skips the seed) and its own browser
address `https://template-<folder>.dev.localhost:<port>`, because cookies are per host name and not per port. Get the real URLs with
`aspire describe`. The `SessionStart` hook (`tools/worktree-cleanup/prune-volumes.mjs --dry-run`) lists the volumes of deleted worktrees; remove them by hand
([isolated mode](docs/development/isolated-mode.md)). The Aspire MCP server only picks up an AppHost under the agent's working
directory; for one in a worktree, call `select_apphost` with its path first. E2E runs (`E2E=true`) also get random ports, start on an
empty database and serve the built `frontend/dist` instead of Vite.

## Contract flow

1. Endpoints are Wolverine.HTTP static classes (`[WolverineGet]` etc.). Operation id = class name without `Endpoint`, tag = feature
   namespace segment (`Configuration/OpenApiNaming.cs`). Do not set them by hand.
2. `pnpm --dir frontend generate` first runs `jasper openapi`, which writes `api/BccCode.Template.Api/BccCode.Template.Api.json`
   (sorted, byte-stable, no database needed). Commit it; CI fails when it is stale, and a test compares the served document with it.
3. The same script then runs `openapi-ts`, which generates `frontend/src/client/` (one client per tag, such as `UsersClient`). Commit it.
   Never edit it by hand.
4. The frontend calls the API only through the generated client, wrapped in query objects per feature (`notes/queries.ts`), and the
   one fetch wrapper `src/lib/apiFetch.ts`, which sends a 401 to `/auth/login?returnUrl=`. Never call the API with a hand-written `fetch`.
5. In development the API proxies Vite (YARP, `Frontend:Mode=Vite`), so the browser talks to one origin. Static mode serves `wwwroot`
   or `Frontend:StaticPath`.

## Conventions

Each rule says why, and which test or guard enforces it (standard 24.3).

- **Every endpoint requires a signed-in person by default.** The fallback policy requires the BCC `personUid` claim, which keys the user's
  data, so do not add `[Authorize]`. `[AllowAnonymous]` is allowed only for routes in `AnonymousRouteGuard` (`/auth/login`,
  `/auth/mfa-required`, `/auth/backchannel-logout`, `/health`, `/alive`); startup fails otherwise (`AssertAnonymousRoutes()`), and
  `SecurityTests` sweeps every route. `/api/admin/**` requires the admin policy by convention; an empty `Auth:AdminRole` denies everyone.
  An administrator's session needs a second factor (`amr` has `mfa`; `AdminMfaMiddleware`).
- **Endpoints follow `LoadAsync` → `Permission` → `Before` → `Validate` → `Post`** (standard 09.2). `Permission` is pure and decides who:
  404 without a body when the note does not exist or is archived, 403 (`Forbidden`, no body) when it exists and the caller may not use it
  (standard 15.8). So a refused write changes nothing, and a stale version or an invalid body is never shown to a caller who may not act.
  `Before` checks the version the client saw (409), `Validate` keeps business rules (`DomainError?`, 400 with an i18n key).
  `PermissionConventionTests` requires `Permission` on every endpoint not on its allowlist; the ownership tests in `NotesApiTests` check
  that nothing was written. The endpoint takes a `CallerAccess` (person uid, roles), loaded once per request.
- **Each endpoint has its own `sealed record` request and response**; never return a stored type (`Endpoints_return_sealed_records_and_not_stored_types`).
  Endpoint classes are static, and there are no `*Service` or `*Repository` slices (architecture tests). Handlers return side effects
  (storage actions, messages) instead of calling the database, so the decision logic is testable without one.
- **Authentication is a BFF**: opaque `__Host-` cookie, server-side session store, PKCE, PAR where BCC Signon offers it, no tokens in the
  browser ([ADR 0019](docs/adrs/0019-bff-sign-in-with-bcc-signon.md), [0020](docs/adrs/0020-server-side-sessions-with-back-channel-logout.md)).
  Logout is a plain form POST to `/auth/logout`, never a GET.
- **CSRF is the cross-origin check, with no tokens** (`CrossOriginProtection`, ADR 0022): an unsafe request must carry
  `Sec-Fetch-Site: same-origin` or `none`, or an `Origin` of our own, or it answers 403. There is no CORS policy and no rate limiting
  middleware: the SPA and the API share one origin, and an endpoint that starts expensive work limits itself with 429 and `Retry-After`.
- **Wolverine runs `ServerlessWithOutbox` in every environment** ([ADR 0024](docs/adrs/0024-wolverine-serverless-with-outbox-and-pubsub-push.md)).
  A message a handler or endpoint returns must implement `IInternalMessage` (our Pub/Sub topic) or `IIntegrationEvent` (BCC PubSub, off until
  `BccPubSub:Enabled`); the architecture tests fail on any other cascaded type. It is stored in the outbox in the request's transaction and
  sent after the commit. With `Messaging:UsePubSub` (true in GCP, set by Terraform) it goes to one Pub/Sub topic and is pushed back to
  `POST /internal/pubsub/{subscription}`; without it (locally and in tests) it goes to the durable local inbox that the durability pass
  runs. Handlers must be idempotent. Scheduled messages (`ScheduleAsync`, `DelayedFor`) are allowed; local queues are not
  (`BannedSymbols.txt`). Nothing runs in the background: no timers, hosted loops or polling listeners (architecture test). A failing
  handler is retried once, again after 5 s, then scheduled (1 min, 15 min), then dead-lettered with a `Fault<T>`. The sample is
  `NoteArchivedMessage`, cascaded by the archive slice.
- **`/internal/**` is for Pub/Sub push and Cloud Scheduler only**: not in the OpenAPI document, no user policy. Each route has its own
  policy (`InternalPubSub`, `InternalScheduler`) on the `GoogleOidc` bearer scheme. Startup fails when an internal setting is missing or a
  route lacks its policy (`InternalRouteGuard`). The push mapper refuses (400) any type that is not an `IInternalMessage`.
  `Internal:AllowAll=true` is for local runs and tests only; the host refuses it outside Development and Test.
- **A row changes only through its entity's methods, which take the time and raise `version`.** `version` is the concurrency token
  (409 on a stale save). Removal is soft (`archived_at`, hidden by a query filter). Endpoints take a `TimeProvider` and use
  `PostgresTime.Now`, which cuts the time to the microseconds Postgres keeps, so what a write returns equals what a read returns.
- **Time comes from `TimeProvider`**, never `DateTime(Offset).Now/UtcNow/Today`. `BannedSymbols.txt` and the banned-API analyzer make it a
  build error (RS0030); the feature project also bans `TimeProvider.System`, which only the host's composition root may use. Not NodaTime
  ([ADR 0029](docs/adrs/0029-timeprovider-as-the-clock.md)).
- **Values are parsed at the boundary**: `NoteContent.TryParse` in `Validate` turns raw strings into value types or a 400 `DomainError`;
  `Post` gets the parsed value from `NoteContent.Parse`.
- **The schema never changes at API startup.** The `schema` resource runs `jasper db-apply` locally, a deploy step in GCP (standard 12);
  `AutoCreate.None` everywhere. Schema changes are additive, so a traffic rollback stays safe.
- **Business values that change without a deploy are stored data**, not constants or `appsettings` (a season's dates, contact details),
  changed on a settings page by an administrator. A value another system owns, a URL or a secret stays configuration.
- **Packages:** central versions in `Directory.Packages.props`; lock files are committed for every project, the AppHost and E2E projects included (Aspire 17 no longer
  adds host-OS packages). A transitive-only pin needs a reason: advisory id and the chain that pulls it in.
- **pnpm is pinned** in `frontend/package.json` (`devEngines`, `onFail: download`); the supply-chain block is in
  `frontend/pnpm-workspace.yaml`, and a package younger than 7 days is rejected on purpose (`minimumReleaseAge`).
- **Analyzers run in the build** (`AnalysisLevel` latest-recommended, `EnforceCodeStyleInBuild`, expression-bodied methods are warnings,
  IDE0022); severities are in `.editorconfig`. A suppression needs a comment with the reason.
- **Never delete, skip or weaken a test to make a build green.** Fix the cause or stop and report. Removing a test is right when the
  behaviour it checks is gone, or another test already checks it on a more real path (an integration test, the CI job that runs the
  image); name that test in the commit message. Do not add production code (an overload, a parameter) only so a unit test can reach it.
- **Never log the user's person uid or other personal data**, and keep the [data inventory](docs/data-protection/data-inventory.md)
  current when a PR adds or changes personal data.
- **Out-of-scope findings go to `docs/tech-debt/`**, one file each, instead of widening the change. Delete the file in the commit that
  fixes it. Subagents return findings to the coordinator, which dedupes and writes the files; parallel agents otherwise write
  near-duplicates.
- Before opening a PR, run the commands above, and use the `standards-reviewer` agent on the diff for changes that touch auth, CI, infra
  or persistence.

## CI

`.github/workflows/ci.yml` (named `CI`) runs on every PR and on pushes to `main`: `backend` (locked restore, build, unit and architecture
tests, `dotnet format --verify-no-changes`), `integration` (Testcontainers Postgres), `e2e` (frontend build, Playwright on the AppHost,
traces uploaded on failure), `codegen` (Wolverine pre-generation, the Release container build, and the image's `db-apply`, `db-assert` and
`/health` against Postgres), `contracts` (OpenAPI and client drift, SBOMs), `frontend` (checks in check mode), `infra` (`terraform fmt`,
`init -backend=false`, `validate` for the three roots, `preview-decide.test.sh`), `architecture` (the C4 DSL validates and the SVGs were
exported from it), `gitleaks`, `sast` (Semgrep CE; CodeQL needs a paid licence on a private repository) and `ci`, which aggregates them
and is the one check to require. The toolchain comes from `.github/actions/setup`; its pnpm version must match `devEngines`.
`supply-chain-security.yml` is separate.

## Deployment

One Cloud Run service (`bcc-template-api-<env>`) in a GCP project from the platform landing zone (`provision-project.yml`), shared Cloud SQL
with IAM authentication, behind the platform gateway; only `/assets/*` comes from a bucket (ADR 0013). Environments: `sandbox`, `prod`,
`dev` (previews) and `preview`.

- `deploy.yml` runs when `CI` succeeds on `main` and the commit changed something deployable (`api/` outside the tests and the
  AppHost, `frontend/` outside the tests, `infra/`, the build files), or manually: build the image once (artifacts, no cloud credentials), apply Terraform for
  sandbox, push, upload assets additively, apply the schema (`db-apply` from the pushed image through the Cloud SQL Auth Proxy, then
  `REASSIGN OWNED`, then `db-assert`), roll out by digest, send traffic `--to-latest`, smoke test. After approval of the `prod` environment,
  `deploy-prod` pushes the same tarball and stops if the digest differs (ADR 0014, 0016).
- Terraform is applied only by CI, through the platform's reusable workflow `terraform.yml@main` (the platform's identity federation trusts
  `refs/heads/main`, so it cannot be pinned to a SHA). `deploy.yml` applies sandbox; `infra.yml` runs plan/apply/destroy by hand and is the
  only way prod infrastructure is applied. Never apply from a laptop.
- Previews (ADR 0027, [docs/pr-previews.md](docs/pr-previews.md)): the `preview` label on a same-repo PR deploys the API, `api/OidcMock` and the
  seeder's data to the landing zone's `dev` project, one Postgres schema `pr<N>` per PR (`Persistence:Schema`), through `preview.yml`.
  Never put real data in one.
- Production runs pre-generated Wolverine code (`TypeLoadMode.Static`, ADR 0012); a handler that only works with runtime compilation fails
  the `codegen` job.
- Traces and metrics go to the BCC monitoring collector (ADR 0026); logs stay on stdout as JSON.
- First deploy of an environment: [first-deploy](docs/runbooks/first-deploy.md). Rollback: [rollback](docs/runbooks/rollback.md). Dead letters:
  [pubsub-dead-letters](docs/runbooks/pubsub-dead-letters.md). Before the first release: [go-live](docs/runbooks/go-live.md).

## Gotchas

- Commit the OpenAPI document together with the regenerated frontend client (`pnpm --dir frontend generate`).
- Aspire's default watch is off (`aspire.config.json`). The AppHost adds the projects by path (`AddDotnetProject`), and
  `dotnet watch` (SDK 11.0.1xx) only starts projects in the AppHost's `ProjectReference` graph, so every start fails with
  "IDE returned unexpected status code 500". Aspire 17's own watch runner fails on Windows too (its log socket refuses the
  connection and DCP reports the same 500, so `schema` and `oidc` never start). Restart a changed service from the dashboard or with
  `aspire resource <name> rebuild`. Aspire plans watch for path-based projects (`aspire run --watch`); turn it on once that ships.
- Release builds use Wolverine static code generation. Generate it first (`jasper codegen write`, see Commands), then build Release. The
  generated folder `api/BccCode.Template.Api/Internal/Generated` is gitignored. .NET 11 container images are Ubuntu 26.04, so the base
  image is `resolute-chiseled-extra` (`ContainerFamily`); `noble-*` tags do not exist for 11.
- Wolverine's outbox and dead-letter tables hold message bodies, so a message with personal data goes into the data inventory. Its relay
  of the `user-name` header is off (`EnableRelayOfUserName`), because it would write display names.
- Wolverine binds the request body for `Before` and `Validate` only when the endpoint method (`Post`) takes the request too. A `Post` that
  has no use for it keeps the parameter and suppresses IDE0060 with a reason (`ArchiveNoteEndpoint`).
- `GenerateDocumentationFile` is on, so the `dotnet format` fix for CS1573 (a doc comment that documents some parameters but not all)
  inserts empty `<param>` tags. Write the missing tags instead of keeping the empty ones.
- The internal message `NoteArchivedMessage` is what leaves the endpoint; the archived note itself is the row with `archived_at` set.
- `Wolverine.Runtime` has an `IInternalMessage` of its own: a file that uses both needs an alias or a qualified name.
- Logout must be a real HTML form POST to `/auth/logout`, because the response redirects through the identity provider; `fetch` cannot
  follow it. The cross-origin check covers it, so the form carries no token.
- Configuration keys are `Oidc:*` (not `OIDC`), `Messaging:UsePubSub`, `Messaging:ProjectId`, `Messaging:Topic`, `BccPubSub:Enabled` and, only
  when that is true, `BccPlatform:*`. `Persistence:Schema` puts the app in a schema of its own (previews).
- The persona person uids are written once, in `api/OidcMock/Personas.cs`; the seeder, the integration tests and the E2E tests compile that
  file in as a link. Seeding runs only in the environments `SeedingGate` allows (Development, Test, Sandbox, Preview).
- `dotnet test` uses the Microsoft.Testing.Platform runner (`global.json`): pass `--project` or `--solution`, not a bare path.
- A migration or a seeder change needs `node tools/db-snapshot/update.mjs`: a new database starts from the committed snapshot in `api/BccCode.Template.TestData/Snapshots/`, and `DatabaseSnapshotTests` fails when it is older than the newest migration. The seeder must stay deterministic (fixed ids and dates, no clock).

## Not set up yet

- Erasure and retention: revoking the app role's delete right, the erasure procedure and its tests wait on the open questions in the
  [data inventory](docs/data-protection/data-inventory.md) (standard 18.6, 18.7).
- Integration events to BCC PubSub: wired, off by default (`BccPubSub:Enabled`), and the template has no event of its own.

## Skills and agents

Project skills are in `.claude/skills/` and agents in `.claude/agents/`.

| Name | Use when |
|---|---|
| `bcc-standards` (skill) | Writing or changing code, config, CI, infra or docs, adding an endpoint, slice, migration, test or workflow, or checking whether something follows the BCC standards. |
| `standards-reviewer` (agent) | Reviewing the current diff or a path against the standards. Read-only. Returns findings by severity with file:line and rule id. |
| `wolverine` | Writing or reviewing a handler, endpoint, message, middleware, error policy or messaging configuration. |
| `aspire`, `aspire-orchestration`, `aspire-monitoring`, `dotnet-inspect` | Generated by `aspire agent init` (see Commands); do not edit by hand. AppHost and CLI, starting and stopping the app (`--isolated` runs), resource state, logs and traces, and looking up .NET package and API facts. |
| `playwright-cli` | Driving a browser from the command line for checks, tests and captures. |
| `screenshot` | Capturing screenshots with committed scripts, for PRs, bug reports and docs images. |
| `pr` | Opening a pull request, with before/after screenshots when the frontend changed. |
| `permission-tests` | Writing or reviewing authorization tests: policy sweep, `PermissionConventionTests`, role population tests, ownership 403 and 404 tests. |
| `writing-user-docs` | Writing end-user documentation in `docs/manual/` (Diátaxis). |
| `clickup-tasks` | Filing work as ClickUp tasks. Fill in the skill's Config section first. |
| `clickup-work` | Starting work from a ClickUp task id or link, and keeping the task in step with the PR. |

## Team process

- Branch names: `<type>/<short-description>` (for example `feat/note-sharing`, `fix/archive-409`), or the ClickUp task id when the work
  comes from a task. A session that starts on a generated `claude/…` branch renames it to that form before the first push
  (`git branch -m <new-name>`), so the remote never gets the generated name.
- Commit messages: an imperative sentence as the subject, with no type prefix; the body says why.
- Pull request descriptions: Summary, Changes, Review notes, Testing, plus Privacy (`.github/pull_request_template.md`).
- Work items live in ClickUp (`clickup-tasks`, `clickup-work` skills). Architecture decisions are ADRs in `docs/adrs/`.
