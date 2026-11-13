# 02 Local Development

This standard covers how a developer or an agent runs the whole system on their own machine. The goal is one command, no secrets, no shared cloud resources, and a topology close enough to production that "works locally" means something.

A .NET Aspire AppHost provides this. See [HTTPS in local development](../technology/cloud-and-tools.md#https-in-local-development) for the background on certificates and origins.

## Rules

### One command, no secrets

1. **`aspire run` from the repo root MUST start everything needed to use the app, after `dotnet tool restore` and `pnpm install`.** `aspire.config.json` points at the AppHost. A new developer should not need a separate setup guide to get a login screen.
2. **A default local run MUST NOT need any secret, `.env` file or access to a shared cloud environment.** Every credential the app needs locally comes from a mock or emulator, or is a mock-only value checked in for that purpose. Optional modes that talk to real services (a flag such as `--<mode>=true`, for example one that targets the sandbox environment) take their secrets from AppHost parameters / user secrets and are off by default.

### Dependencies run in the AppHost

3. **The AppHost MUST run every dependency the app talks to: database, database UI, frontend dev server, and a local stand-in for every external system.** If one dependency is "use the shared dev instance", local runs and tests break when that instance changes, and agents need credentials.
4. **Every external system MUST have a local stand-in.** The kinds of stand-in, for example when the app uses the system in question:
   - **Identity provider:** an OIDC mock (OpenIddict) with named personas that match the seeded test data, listed in `AGENTS.md`/`README.md`. Nobody uses real credentials locally.
   - **Core API and other BCC platform APIs:** BCC's emulator image with checked-in test data, where one exists for the API.
   - **A payment or invoicing provider:** a mock service with a UI to inspect and trigger what the real system would do.
   - **E-mail/SMS:** Mailpit.
   - **GCP services (for example Pub/Sub, Firestore, Cloud Tasks):** Google's emulators where they exist, and a local fake where they do not.
5. **Emulator and helper container images SHOULD be pinned by digest (`WithImageSHA256`), and the repo MUST say how the pin is updated.** A tag like `main` or `latest` changes under you and breaks local runs and E2E without any commit in the repo. A digest goes stale, and Dependabot does not update digests set in C# AppHost code, so record the update path (for example a comment with the source tag and a recurring task to bump it).

### Isolation for worktrees and agents

6. **The AppHost MUST support `aspire start --isolated` (or `aspire run --isolated`): random ports, a throwaway database, no persistent containers.** Detect it with `DcpPublisher:RandomizePorts`. Without it, two worktrees share fixed ports and the persistent Postgres container, so one instance silently reads and writes the other's data.
7. **`AGENTS.md`/`CLAUDE.md` MUST state that `--isolated` is mandatory in git worktrees and for parallel agents, and that `aspire describe` gives the real URLs.** The rule only works if every agent sees it; see [24 AI Agent Tooling](24-ai-agent-tooling.md).
8. **The sole (non-isolated, non-E2E) instance MUST use fixed, documented ports and MAY use persistent containers and named volumes.** Fixed ports make bookmarks, OIDC redirect URIs and docs stable. Keep the port table in `README.md` or `AGENTS.md`, with one port range per repo that does not overlap the ranges of other repos.

### Seeding and startup order

9. **Test data MUST be seeded by a seeder resource in the AppHost that exposes a readiness signal, and the API MUST wait for it.** A health check that only passes after migrate + seed has committed (for example a `SeedCompletedHealthCheck`) or a `/seed-status` endpoint stops the app and E2E tests from starting against an empty database. Seed realistic data, not a handful of placeholder rows.
10. **Seeding and database reset MUST be gated by an allowlist of disposable environments, not a "not prod" check.** A misspelled or new environment name must refuse to seed. The gate applies to every entry point that seeds or resets (the seeder resource, reset commands, CLI tools). For example, a `SeedingGate` allows only `dev`, `sandbox` and `preview` when running in GCP. See the example below; other standards link here rather than repeat it.
11. **Startup order MUST mirror production: schema/migrations complete before the API starts.** Use `WaitForCompletion(schemaJob)` for a one-shot schema step. This catches code that only works because the app migrated itself on start, which is not allowed in production ([04 GCP Runtime](04-gcp-runtime.md), [12 Data Access and Migrations](12-data-access-and-migrations.md)).
12. **The dashboard MUST have commands for resetting and seeding data, and one command per scheduled job that Cloud Scheduler or a cron runs in GCP.** Use `WithHttpCommand` with a description that says what it mimics. Locally nothing else triggers those jobs.

### Topology for tests

13. **The AppHost MUST have an E2E/test flag (`--E2E=true` or `TEST` environment) that starts a lean topology for automated tests.** No dev-only extras (database UI, Structurizr, simulators), a smaller seed profile, static frontend instead of Vite. The same AppHost then serves local runs and [19 Testing Strategy](19-testing-strategy.md).

### HTTPS and origin

14. **The app MUST be served over HTTPS locally on `https://<app>.dev.localhost:<port>` with the ASP.NET dev certificate, and the session cookie MUST keep its `__Host-` name locally.** `*.dev.localhost` resolves to loopback without hosts-file edits and gives each app its own cookie scope. Fix certificate trust rather than adding HTTP fallbacks.
15. **The browser MUST talk to one origin: the API proxies the Vite dev server through YARP.** This matches production (the API serves the SPA, see [26 Gateway and Frontend Hosting](26-gateway-and-frontend-hosting.md)) so cookies, CSRF and CSP behave the same. Vite itself runs plain HTTP behind the proxy, so only one certificate is needed.

### Tooling around the AppHost

16. **`.mcp.json` MUST register the Aspire MCP server (`aspire agent mcp`).** Agents can then list resources, read logs and run dashboard commands instead of guessing.
17. **A devcontainer SHOULD exist** (`.devcontainer/devcontainer.json` with the .NET SDK, Aspire, Node, docker-in-docker, and a post-create script that pins pnpm and restores tools). It gives a working environment on any machine and in Codespaces.
18. **IDE launch configurations MAY be committed** (`.vscode/launch.json`, `.vscode/extensions.json`, Rider `.run/*.run.xml`), but never machine-specific settings files.

## Examples

### Isolation, fixed ports for the sole instance, digest-pinned emulators (rules 5, 6, 8)
In the AppHost `Program.cs`:

```csharp
var isIsolated = bool.TryParse(builder.Configuration["DcpPublisher:RandomizePorts"], out var randomizePorts) && randomizePorts;
// Fixed ports and a persistent database only for the single default instance. Parallel worktrees (--isolated)
// and E2E runs would otherwise fight over the ports and share one database.
var isSoleInstance = !isE2E && !isIsolated;
int? Port(int port) => isSoleInstance ? port : null;
...
var coreApi = builder.AddContainer("coreapi", "ghcr.io/bcc-code/bcc-core-api/bcc-core-emulator")
    .WithImageSHA256("5d2aae10e208adfa15a4b027df8499485c4501e7f0edcd7706bdf3864dfd2a5f")
    .WithBindMount($"../../test-data/{coreTestData}", "/test-data", isReadOnly: true)
    .WithHttpCommand("/refresh-data", "Reset Core API data", "api", ...);
```

### Dashboard commands for scheduled jobs (rule 12)

```csharp
// Locally there is no Cloud Scheduler: one dashboard command per job.
processor.WithHttpCommand("/internal/scheduled/reconcile", "Reconcile Firestore", "http",
    commandOptions: new HttpCommandOptions { IconName = "ArrowSync", Description = "Runs the reconcile job once, as Cloud Scheduler does every minute in GCP" });
```

### Seeder as a gated resource, startup order (rules 9, 11, 12)

```csharp
var testdata = builder.AddProject<Projects.MyApp_DatabaseManager>("testdata")
    .WithReference(database).WaitFor(database)
    // Without this, WaitFor(testdata) only waits for the process. The seeder's own health check
    // reports healthy only after migrate and seed have committed, so WaitFor means "migrated and seeded".
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithHttpCommand("/reset-and-seed-data", "Reset database with test data", ...);

var api = builder.AddProject<Projects.MyApp_Api>("api")
    .WithReference(database)
    .WaitFor(testdata)
    .WaitForCompletion(wolverineSchema);
```

### Seeding allowlist (rule 10)

```csharp
// Seeding or resetting production cannot be undone, so the decision is an allowlist rather
// than a "not prod" check: a misspelled or newly added environment name refuses instead of
// being treated as safe.
private static readonly HashSet<string> DisposableEnvironments =
    new(StringComparer.OrdinalIgnoreCase) { "dev", "sandbox", "preview" };
```

### Readiness endpoint (rule 9)

The seeder maps `/seed-status`, which returns 503 with the seeder's last error until the seed has committed, then 200. The E2E harness waits on it before starting tests. Waiting on a readiness signal removes the flaky-test cause of tests that start against a half-seeded database.

### Worktree rule in agent docs (rules 6, 7)

`AGENTS.md` and `CLAUDE.md` carry a section "Always use `--isolated` in a git worktree" that explains the failure: fixed ports and the persistent Postgres container are shared, so one worktree can write another's database.

### Other stand-ins (rule 4)

- A mock for the external payment or invoicing service, with a UI and a command such as "Deliver all pending invoices".
- Mailpit for e-mail.
- Emulators or fakes for the GCP services the app uses, such as Pub/Sub, Firestore and Cloud Tasks.
- An `OidcMock` project with a persona picker.
- A `.devcontainer/` folder for the devcontainer.

## Related
- [01 Repository Layout and Build](01-repository-layout-and-build.md) — AppHost project and tool manifest
- [04 GCP Runtime](04-gcp-runtime.md) — schema and jobs that the AppHost mirrors
- [07 Preview Environments](07-preview-environments.md) — disposable environments that seeding allowlists cover
- [12 Data Access and Migrations](12-data-access-and-migrations.md) — schema before API start
- [14 Authentication and Sessions](14-authentication-and-sessions.md) — OIDC mock and `__Host-` cookie
- [19 Testing Strategy](19-testing-strategy.md) — the E2E topology
- [24 AI Agent Tooling](24-ai-agent-tooling.md) — agent instructions and Aspire MCP
- [26 Gateway and Frontend Hosting](26-gateway-and-frontend-hosting.md) — single-origin hosting
- [Cloud and tools](../technology/cloud-and-tools.md#https-in-local-development) — HTTPS in local development
