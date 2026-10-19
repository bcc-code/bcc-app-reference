---
name: aspire
description: >-
  Aspire router for this repo. Use for anything involving the Aspire AppHost (`api/AppHost/`),
  the Aspire CLI (`aspire start/stop/wait/describe/ps/logs/otel/add/docs`), resources, the
  dashboard, custom resource commands, `--isolated` runs in git worktrees, or finding frontend
  URLs for Playwright. Routes to aspire-orchestration (lifecycle), aspire-monitoring (logs,
  traces, state) and aspire-deployment (publish, deploy). Not for non-Aspire projects or
  ordinary build/test tasks.
license: MIT
metadata:
  author: Microsoft
  version: "0.0.3"
  adapted-for: bcc-app-template
---

# Aspire

Use this skill when the task involves the Aspire distributed application in this repo: operating
the AppHost or its resources through the Aspire CLI rather than falling back to ad-hoc `dotnet`,
`docker`, or shell workflows.

The AppHost is `api/AppHost/`, configured by `aspire.config.json` at the repo root. The
persistence variant that runs is picked with `Template:Persistence` (`marten` or `efcore`).
Read `AGENTS.md` for the commands, ports and test personas (`user`, `user2`, `admin`) of this repo.
In a git worktree always start with `--isolated` (see aspire-orchestration).

## Triage first

- **"Something's wrong", "show me what's happening", "why is my app misbehaving"** → observe
  **runtime** state first: route to [aspire-monitoring](../aspire-monitoring/SKILL.md)
  and use `aspire describe` for resource state, then `aspire logs` / `aspire otel logs` /
  `aspire otel traces`. Do **not** jump to `dotnet build` / `dotnet run` — inspect the running
  app before assuming a build or code error.
- **"Better agent support for Aspire"** → the repo already registers the Aspire MCP server in
  `.mcp.json` (`aspire agent mcp`). Do not add `copilot-setup-steps.yml` or other scaffolding for this.
- **TypeScript AppHost migration (`apphost.ts`)** → not relevant here; this repo uses a C# AppHost.

## Default Workflow

1. Identify the AppHost (`api/AppHost/`).
2. Route lifecycle work to [aspire-orchestration](../aspire-orchestration/SKILL.md): prefer `aspire_apphost_start` when the VS Code tool is available; use `aspire start --non-interactive --isolated --apphost <path>` in worktrees.
3. `aspire wait <resource>` before interacting with any resource.
4. Inspect state with `aspire describe`, `aspire otel logs`, `aspire logs`, `aspire otel traces` and `aspire export` before making code changes.
5. Before adding integrations, use `aspire integration search <query>` when the package is unknown, then `aspire add <package>` when ready to change the AppHost.
6. When code changes, decide whether the AppHost model changed or only one resource changed. Restart through `aspire-orchestration` after AppHost changes; otherwise prefer resource commands, runtime watch/HMR, dashboard actions, or IDE-managed debugging.

## Key Rules

- For agent AppHost lifecycle requests, identify one exact AppHost and route to `aspire-orchestration`. Agents use the editor lifecycle tool or `aspire start` for detached, noninteractive, exact-target and worktree-isolated execution.
- When VS Code exposes `aspire_apphost_start` or `aspire_apphost_stop`, load the deferred contracts and prefer the matching tool, subject to the orchestration skill's worktree and stop-result rules; use start mode `run` unless the user explicitly asks to attach a debugger.
- If several AppHosts are discovered and the target is unclear, ask which one before taking any lifecycle action.
- **Always** `aspire wait <resource>`, **never** manual HTTP polling.
- Use `aspire ps` for running AppHost processes and `aspire describe` for resource state and endpoints. Never generate removed 13.5 forms such as `aspire ps --resources` or `aspire ps --include-hidden`.
- Use `aspire resource <resource-name> <command>` for resource operations such as `stop`, `start`, or `rebuild` when available.
- Treat `aspire stop --force` as data-destructive: it permanently removes persistent resources without another prompt. Use it only after explicit confirmation for one exact AppHost.
- Do not stop or restart the whole AppHost just because one resource changed.
- Use `features.defaultWatchEnabled` only for Aspire default watch; do not treat it as per-resource rebuild, restart, or hot reload.
- Prefer a resource's own framework/runtime hot reload, HMR, or watch workflow when it already handles the change.
- **Always** `aspire docs search <topic>` before editing unfamiliar AppHost APIs.
- **Always** `aspire docs api search <query> --language csharp` for API reference before editing AppHost code.
- **Always** `--non-interactive` for agent execution.
- Use `aspire integration list --format Json` and `aspire integration search <query> --format Json` for read-only integration discovery.
- **Never** install the obsolete Aspire workload.
- Local runs follow standard 02 (mocks for every external system, no secrets, pinned image digests). Check the `bcc-standards` skill before changing AppHost resources.

## Routing

| Task | Route To |
|------|----------|
| Start, stop, wait, restart, rebuild | [aspire-orchestration](../aspire-orchestration/SKILL.md) |
| Deploy, publish, destroy, pipeline steps | [aspire-deployment](../aspire-deployment/SKILL.md) (production deploys of this repo use Terraform and GitHub Actions instead) |
| Logs, traces, metrics, dashboard, browser logs | [aspire-monitoring](../aspire-monitoring/SKILL.md) |
| Diagnose a running app | [aspire-monitoring](../aspire-monitoring/SKILL.md): start with `aspire describe`, then `aspire logs` / `aspire otel logs` / `aspire otel traces`; **investigate before editing code** |
| Edit the AppHost (resources, commands, endpoints, Interaction Service) | [references/csharp-apphosts.md](references/csharp-apphosts.md) |
| Find the frontend URL for a Playwright run | [references/playwright-handoff.md](references/playwright-handoff.md), then the `playwright-cli` skill |

## Sub-Skills

- **aspire-orchestration**: lifecycle (start, stop, wait, resource commands, default watch/HMR, file-lock recovery), `aspire ps`/`describe`, `aspire stop --force` safeguards, CLI upgrades (`aspire update --self`).
- **aspire-monitoring**: `aspire logs`, `aspire otel`, `aspire describe`, `aspire export`, `aspire dashboard run`, dashboard features.
- **aspire-deployment**: `aspire deploy`, `aspire publish`, `aspire destroy`, `aspire do <step>` and the supported targets.

## Prerequisites

| Requirement | Install |
|-------------|---------|
| .NET SDK (version pinned in `global.json`) | https://dotnet.microsoft.com/download |
| Aspire CLI (pinned in `.config/dotnet-tools.json` as `aspire.cli`) | `dotnet tool restore` |

## References

- [aspire-13-5-breaking-changes.md](references/aspire-13-5-breaking-changes.md): 13.5 compatibility notes, breaking-change scrub, current CLI/AppHost behavior.
- [aspire-13-3-breaking-changes.md](references/aspire-13-3-breaking-changes.md): 13.3 breaking changes to scrub from agent-generated code, scripts and CI snippets.
- [csharp-apphosts.md](references/csharp-apphosts.md): C# AppHost authoring patterns.
- [agent-workflows.md](references/agent-workflows.md): worktrees, code changes, investigation, integrations, secrets, Playwright handoff.
- [app-commands.md](references/app-commands.md): app lifecycle, update, restore, docs and integration discovery commands.
- [resource-management.md](references/resource-management.md): resource wait and resource-command guidance.
- [monitoring.md](references/monitoring.md): app state, logs, traces, search filtering, dashboard links, export workflows.
- [playwright-handoff.md](references/playwright-handoff.md): Playwright handoff after Aspire endpoint discovery.
- [deployment.md](references/deployment.md), [tools-and-configuration.md](references/tools-and-configuration.md), [typescript-apphosts.md](references/typescript-apphosts.md): further notes.
