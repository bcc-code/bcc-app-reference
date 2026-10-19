---
standard: 24 AI Agent Tooling
assessed: 2026-10-19
commit: e4b2b86
---
# 24 AI Agent Tooling — assessment

The agent setup matches the standard: `CLAUDE.md` imports `AGENTS.md`, the guide holds the required sections and working
rules, skills live in one place, the Aspire MCP server is registered and the agent artefacts are ignored. Slopwatch is removed
by ADR 0006. The one practical gap is that `.claude/launch.json` starts a non-isolated `aspire run`, which conflicts with the
guide's own worktree rule.

**Score: Strong** · ✅ 11 · ⚠️ 2 · ❌ 0 · N/A 1

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | `AGENTS.md` is the single guide; `CLAUDE.md` contains only `@AGENTS.md` | MUST | ✅ | `CLAUDE.md` is the single line `@AGENTS.md`. |
| 2 | `AGENTS.md` has start-here, commands, ports/personas, rules, gotchas, deployment, process | MUST | ✅ | `AGENTS.md:11-22` start here, `:24-44` commands, ports, personas, `:46-71` rules, `:83-94` gotchas, `:117-122` deployment, `:124-128` team process. |
| 3 | Rules written as "do X, because Y, enforced by Z" | SHOULD | ✅ | `AGENTS.md:46-71`, e.g. `:50-51` names `AnonymousRouteGuard` / `AssertTemplateRoutes()`, `:60-61` names the IL scanner. A few name the guard only loosely ("Architecture tests check the slice shape"). |
| 4 | Guide states isolated runs, tech-debt findings, never weaken tests | MUST | ✅ | `AGENTS.md:75-79`. |
| 5 | Guide stays lean, links specs and ADRs | SHOULD | ✅ | 9.5 KB; links ADRs 0003, 0004, `docs/development.md`, `infra/README.md`. |
| 6 | Slopwatch in CI with `--fail-on warning` and empty baseline | MUST | ⚠️ | Deviation (ADR): [ADR 0006](../adrs/0006-no-slopwatch.md) removes Slopwatch; not in `pr.yml` or `.config/dotnet-tools.json`. |
| 7 | Important rules enforced by architecture tests or startup assertions | MUST | ✅ | `api/tests/BccCode.Template.ArchitectureTests/` (14 tests pass, `dotnet test --project …ArchitectureTests`); `AssertTemplateRoutes()` in both `Program.cs`. See standard 25. |
| 8 | `.mcp.json` registers the Aspire MCP server | MUST | ✅ | `.mcp.json` — `aspire` with `["agent", "mcp"]`. |
| 9 | Repo-specific skills for recurring tasks; restricted `allowed-tools` | SHOULD | ✅ | `.claude/skills/`: `pr`, `screenshot`, `permission-tests`, `writing-user-docs`, `clickup-tasks`, `clickup-work`, `bcc-standards`, Aspire skills. `allowed-tools` set on `pr`, `screenshot`, `playwright-cli`, `dotnet-inspect`. |
| 10 | Skills in one location only | MUST | ✅ | No `.agents/` directory. |
| 11 | Commit `.claude/settings.json` with a small allowlist; never `settings.local.json` | MUST | ✅ | `.claude/settings.json` allows build/test/format, `pnpm --dir frontend run *`, `aspire describe` and three doc domains. `.gitignore` lists `.claude/settings.local.json`; not in `git ls-files`. |
| 12 | `.gitignore` covers agent artefacts | MUST | ✅ | `.gitignore` has `.claude/worktrees/`, `.claude/settings.local.json`, `.playwright-mcp/`, `.playwright-cli/`, `.superpowers/`. |
| 13 | `.claude/launch.json` defines the app for the preview pane, no machine paths | SHOULD | ⚠️ | `.claude/launch.json` runs `aspire run` at `https://template.dev.localhost:18000`, no machine paths. But it is not `--isolated`: started from a worktree it attaches to the shared Postgres container, which `AGENTS.md:75-76` forbids. |
| 14 | `copilot-setup-steps.yml` may prepare Copilot | MAY | N/A | Not adopted. |

## Gaps to close

- **13**: Make the preview-pane entry safe in worktrees: add a second configuration with `aspire run --isolated` (the port is
  then dynamic, so the URL must be found with `aspire describe`), or note in `AGENTS.md` that the `template` launch entry is for
  the main checkout only.
- **6**: Deviation recorded in ADR 0006; no action unless the ADR is revisited. Its stated compensations (reviewer checks for
  `#pragma warning disable`, skipped tests, empty catches) are not automated.

## Not verifiable in the repo

- Whether the `clickup-tasks`/`clickup-work` Config sections are filled in for a real app (`README.md:46-47` lists it as a
  setup step).
