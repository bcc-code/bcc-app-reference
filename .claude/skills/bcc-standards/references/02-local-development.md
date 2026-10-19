# 02 Local Development

How a developer or an agent runs the whole system on their own machine. The goal is one command, no secrets, no shared cloud resources, and a topology close enough to production that "works locally" means something. A .NET Aspire AppHost does this. See .NET Aspire and HTTPS in Local Development for the principles.

## Rules

### One command, no secrets

1. **`aspire run` from the repo root MUST start everything needed to use the app, after `dotnet tool restore` and `pnpm install`.** `aspire.config.json` points at the AppHost. A new developer should not need a wiki page to get a login screen.
2. **A default local run MUST NOT need any secret, `.env` file or access to a shared cloud environment.** Every credential the app needs locally comes from a mock or emulator, or is a mock-only value checked in for that purpose. Optional modes that talk to real services (orginn `--sandbox=true`, `--bcc-notifications=true`) take their secrets from AppHost parameters / user secrets and are off by default.

### Dependencies run in the AppHost

3. **The AppHost MUST run every dependency the app talks to: database, database UI, frontend dev server, and a local stand-in for every external system.** If one dependency is "use the shared dev instance", local runs and tests break when that instance changes, and agents need credentials.
4. **Every external system MUST have a local stand-in:**
   - **Identity provider:** an OIDC mock (OpenIddict) with named personas that match the seeded test data, listed in `AGENTS.md`/`README.md`. Nobody uses real credentials locally.
   - **Core API and other BCC platform APIs:** the official emulator image with checked-in test data.
   - **Payment and invoicing (e.g. Mastercard eFaktura):** a mock service with a UI to inspect and trigger what the real system would do.
   - **E-mail/SMS:** Mailpit.
   - **GCP services (Pub/Sub, Firestore, Cloud Tasks):** the official or community emulators.
5. **Emulator and helper container images MUST be pinned by digest (`WithImageSHA256`).** A tag like `main` or `latest` changes under you and breaks local runs and E2E without any commit in the repo. bcc-privacy's Core API emulator on tag `main`, and orginn's Mailpit and Structurizr on `latest`, are the counter-examples.

### Isolation for worktrees and agents

6. **The AppHost MUST support `aspire start --isolated` (or `aspire run --isolated`): random ports, a throwaway database, no persistent containers.** Detect it with `DcpPublisher:RandomizePorts`. Without it, two worktrees share fixed ports and the persistent Postgres container, so one instance silently reads and writes the other's data.
7. **`AGENTS.md`/`CLAUDE.md` MUST state that `--isolated` is mandatory in git worktrees and for parallel agents, and that `aspire describe` gives the real URLs.** The rule only works if every agent sees it; see [24 AI Agent Tooling](24-ai-agent-tooling.md).
8. **The sole (non-isolated, non-E2E) instance MUST use fixed, documented ports and MAY use persistent containers and named volumes.** Fixed ports make bookmarks, OIDC redirect URIs and docs stable. Keep the port table in `README.md` or `AGENTS.md`, one range per repo (orginn 15000–15050, privacy 13000+, vote 17000+).

### Seeding and startup order

9. **Test data MUST be seeded by a seeder resource in the AppHost that exposes a readiness signal, and the API MUST wait for it.** A health check that only passes after migrate + seed has committed (orginn `SeedCompletedHealthCheck`) or a `/seed-status` endpoint (bcc-privacy) stops the app and E2E tests from starting against an empty database. See Realistic Test Data.
10. **Seeding and database reset MUST be gated by an allowlist of disposable environments, not a "not prod" check.** A misspelled or new environment name must refuse to seed. orginn `SeedingGate` allows only `dev`, `sandbox`, `preview` when running in GCP.
11. **Startup order MUST mirror production: schema/migrations complete before the API starts.** Use `WaitForCompletion(schemaJob)` for a one-shot schema step. This catches code that only works because the app migrated itself on start, which is not allowed in production ([04 GCP Runtime](04-gcp-runtime.md), [12 Data Access and Migrations](12-data-access-and-migrations.md)).
12. **The dashboard MUST have commands for resetting and seeding data, and one command per scheduled job that Cloud Scheduler or a cron runs in GCP.** `WithHttpCommand` with a description that says what it mimics. Locally nothing else triggers those jobs.

### Topology for tests

13. **The AppHost MUST have an E2E/test flag (`--E2E=true` or `TEST` environment) that starts a lean topology for automated tests.** No dev-only extras (database UI, Structurizr, simulators), a smaller seed profile, static frontend instead of Vite. The same AppHost then serves local runs and [19 Testing Strategy](19-testing-strategy.md).

### HTTPS and origin

14. **The app MUST be served over HTTPS locally on `https://<app>.dev.localhost:<port>` with the ASP.NET dev certificate, and the session cookie MUST keep its `__Host-` name locally.** `*.dev.localhost` resolves to loopback without hosts-file edits and gives each app its own cookie scope. Fix certificate trust rather than adding HTTP fallbacks.
15. **The browser MUST talk to one origin: the API proxies the Vite dev server through YARP.** This matches production (API serves the SPA, see Frontend Hosting Strategy) so cookies, CSRF and CSP behave the same; Vite itself runs plain HTTP behind the proxy, so only one certificate is needed.

### Tooling around the AppHost

16. **`.mcp.json` MUST register the Aspire MCP server (`aspire agent mcp`).** Agents can then list resources, read logs and run dashboard commands instead of guessing.
17. **A devcontainer SHOULD exist** (`.devcontainer/devcontainer.json` with the .NET SDK, Aspire, Node, docker-in-docker, and a post-create script that pins pnpm and restores tools). It gives a working environment on any machine and in Codespaces.
18. **IDE launch configurations MAY be committed** (`.vscode/launch.json`, `.vscode/extensions.json`, Rider `.run/*.run.xml`), but never machine-specific settings files.

## Example

`api/AppHost/` (resources, seeder, `--isolated` handling, E2E flag), `api/OidcMock/` (personas `user`, `user2`, `admin`), `aspire.config.json`, `.mcp.json`. The persistence variant is picked with `Template:Persistence` (`marten` or `efcore`).
