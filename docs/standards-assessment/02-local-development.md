---
standard: 02 Local Development
assessed: 2026-10-19
commit: e4b2b86
---
# 02 Local Development — assessment

The AppHost gives a one-command local run without secrets, with schema and seed steps that the API waits for, an OIDC mock
with personas, `--isolated` and E2E topologies, fixed documented ports and a single HTTPS origin. Missing: container images
pinned by digest, dashboard commands for reset and seed, and a devcontainer. `aspire run` was not started for this
assessment (per the method); the E2E job in `pr.yml` starts the same AppHost.

**Score: Adequate** · ✅ 13 · ⚠️ 1 · ❌ 3 · N/A 1

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | `aspire run` from root starts everything | MUST | ✅ | `aspire.config.json` points at `api/AppHost/AppHost.csproj`; commands in `AGENTS.md:27-30`, `docs/development.md:15-19`. CI starts the AppHost the same way (`pr.yml:159-160`). Not run here. |
| 2 | Default run needs no secret, `.env` or shared cloud | MUST | ✅ | No `AddParameter` in `api/AppHost/Program.cs`; the mock registers a public PKCE client (`api/OidcMock/ClientSeeder.cs:6`), so `OIDC:ClientSecret` is null locally (`api/BccCode.Template.Web/Auth/AuthConfiguration.cs:112`). |
| 3 | AppHost runs every dependency (DB, DB UI, frontend, stand-ins) | MUST | ✅ | Postgres `Program.cs:10`, Dbx UI `:16`, Vite `:89`, OIDC mock `:62`. No other external systems in the sample. |
| 4 | Local stand-in for every external system | MUST | ✅ | OIDC mock with personas `user`, `user2`, `admin` listed in `AGENTS.md:43-44` and `docs/development.md:53-57`. No Core API, payments, e-mail or GCP messaging in the sample. |
| 5 | Emulator/helper images pinned by digest | MUST | ❌ | Postgres uses tag `18` (`api/AppHost/Program.cs:10`, `WithImageTag("18")`); the Dbx container (`:16`) uses the integration's default tag. No `WithImageSHA256` anywhere. |
| 6 | Supports `--isolated` (random ports, throwaway DB) | MUST | ✅ | `Program.cs:4` reads `DcpPublisher:RandomizePorts`; fixed ports and the persistent volume only when `isSoleInstance` (`:7-14`). |
| 7 | AGENTS/CLAUDE.md: `--isolated` mandatory; `aspire describe` gives URLs | MUST | ⚠️ | `AGENTS.md:30,75-76` state `--isolated` for worktrees. `aspire describe` is explained only in `docs/development.md:22,42`, not in `AGENTS.md`/`CLAUDE.md` (`grep describe` finds nothing there). |
| 8 | Sole instance uses fixed documented ports; may persist | MUST | ✅ | Ports 18000/18010/18011 in `Program.cs:10,16,43`, OIDC 18002 in `api/OidcMock/Properties/launchSettings.json`, dashboard 18090 in `api/AppHost/Properties/launchSettings.json`; table in `AGENTS.md:40-41`. Persistent volume `bcc-template-postgres` (`Program.cs:13`). |
| 9 | Seeder resource with readiness signal; API waits | MUST | ✅ | `seed` resource runs the API host with `seed` (`Program.cs:42`); the API uses `WaitForCompletion(seed)` (`:46`), so it starts only after the seed command exited successfully. |
| 10 | Seeding gated by allowlist of disposable environments | MUST | ✅ | `Seeding/SeedingGate.cs:10` (Development, Test, Sandbox, Preview) in both variants; tests `SeedingGateTests.Disposable_environments_may_be_seeded` and `Anything_else_is_refused_including_a_misspelled_name` (`api/tests/*/PersistenceTests.cs`). |
| 11 | Schema completes before the API starts | MUST | ✅ | `seed` waits for `schema` and `api` waits for `seed` (`Program.cs:40-46`); comment at `:41`. |
| 12 | Dashboard commands for reset/seed and per scheduled job | MUST | ❌ | No `WithHttpCommand`/`WithCommand` in `api/AppHost/Program.cs`. Re-seeding means restarting the `seed` resource; there is no reset. No scheduled jobs exist, so only the reset/seed part applies. |
| 13 | E2E flag with a lean topology | MUST | ✅ | `E2E=true` (`Program.cs:3`): no Dbx (`:15`), static built frontend instead of Vite (`:80-86`), random ports, no persistent volume. Same seed as normal runs. Used by `pr.yml:145`. |
| 14 | HTTPS on `https://<app>.dev.localhost:<port>`, `__Host-` cookie | MUST | ✅ | `WithHttpsEndpoint` (`Program.cs:43`), dashboard URL `https://template.dev.localhost:<port>` (`:77`), registered as OIDC redirect host (`:65`). Cookie name `__Host-template` (`api/BccCode.Template.Web/Auth/AuthConfiguration.cs:24`) in every environment. |
| 15 | One origin: API proxies Vite through YARP | MUST | ✅ | `FrontendConfiguration.cs:17-23,54` (`AddReverseProxy`, `MapReverseProxy`), route in `appsettings.Development.json` (`ReverseProxy`). |
| 16 | `.mcp.json` registers Aspire MCP | MUST | ✅ | `.mcp.json` (`aspire agent mcp`). |
| 17 | Devcontainer | SHOULD | ❌ | No `.devcontainer/`. |
| 18 | IDE launch configs may be committed | MAY | N/A | None committed (`.vscode/`, `.run/` absent). Optional. |

## Gaps to close

- **5**: Pin Postgres and Dbx by digest in `api/AppHost/Program.cs` (`.WithImageTag("18").WithImageSHA256("<digest>")`, same
  for the Dbx container through its builder callback), and add a note on how to update the digests.
- **12**: Add dashboard commands in `api/AppHost/Program.cs`, for example a `WithCommand`/`WithHttpCommand` on the API or
  `seed` resource that resets the database and re-runs the seed, with a description of what it does. Add one per scheduled
  job when the app gets one.
- **7**: Add one line to `AGENTS.md` ("Working rules for agents") that `aspire describe` gives the real URLs of an isolated
  instance.
- **17**: Add `.devcontainer/devcontainer.json` with the .NET SDK from `global.json`, Node 26, docker-in-docker and a
  post-create script (`corepack`/pnpm 12.11.2, `dotnet tool restore`, `pnpm --dir frontend install`).

## Not verifiable in the repo

- Rule 1 and 14 end to end: run `dotnet tool restore && pnpm --dir frontend install && aspire run` on a clean machine and
  check the login screen at `https://template.dev.localhost:18000` with a trusted dev certificate.
- Rule 6: that the OIDC mock's launch-profile ports (18002/18003) are randomised under `--isolated`; check with two
  isolated instances and `aspire describe`.
