# 0003. .NET Aspire for local development

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADR 0003.

## Context

Developers and agents need to start the whole app with one command, without secrets or shared cloud resources, and several git worktrees must be able to run at the same time without sharing ports or data.

## Decision

We will orchestrate local development with a .NET Aspire AppHost (`api/AppHost`). `aspire run` from the repo root starts Postgres, the schema step, the seeder, the OIDC mock, the API and the Vite dev server, and every later dependency as it is added.

- The default instance uses fixed ports in one range per app (the table is in [AGENTS.md](../../AGENTS.md)); each application made from the template picks its own range so two apps can run side by side.
- `aspire run --isolated` gives random ports and no persistent state. It is required in git worktrees and for parallel agents.
- There is no Pub/Sub emulator: locally internal messages go through Wolverine's durable inbox ([0023](0023-pubsub-for-internal-messages-and-bcc-pubsub-for-events.md)).
- `.mcp.json` registers the Aspire MCP server so agents can read resources and logs.

## Consequences

| | |
|---|---|
| Positive | One command to run everything; local topology close to production; worktrees can run side by side. |
| Negative | Every developer needs the Aspire CLI (restored with `dotnet tool restore`) and Docker. |
| Follow-up | Every external system the app talks to gets a local stand-in in the AppHost (standard 02.4). |

## Alternatives considered

- **Docker Compose plus separate `dotnet run` and `pnpm dev`** — several commands, no dashboard, and no built-in way to randomise ports per worktree.
