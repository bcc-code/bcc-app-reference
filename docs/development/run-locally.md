# Run locally

## Prerequisites

- .NET SDK pinned in `global.json` (11.0.100 with `latestPatch`).
- Aspire CLI, restored with `dotnet tool restore`.
- Node and pnpm as pinned in `frontend/package.json` (`devEngines`).
- Docker (Postgres and the integration tests run in containers).
- A trusted development certificate: `dotnet dev-certs https --trust`.
- Terraform 1.15 (`required_version ~> 1.15`), only to read or validate `infra/`. Terraform is applied only from CI.

## Start the app

```bash
dotnet tool restore
pnpm --dir frontend install
aspire run
```

`aspire run` starts everything the application needs: Postgres, the schema step (`jasper db-apply`), the seeder, the OIDC mock,
the API, the frontend dev server and the C4 workspace viewer. No secret or `.env` file is needed, and there is no Pub/Sub
emulator ([messaging locally](messaging-locally.md)). `aspire describe` prints the real URLs and ports of the running
resources, and the dashboard lists them. The fixed ports of the sole instance are in the ports table in [AGENTS.md](../../AGENTS.md).

The seeder waits for the schema step and runs once (`Seeder:Once`); the API waits for both. Open the app and pick the provider on the sign-in page (BCC Signon or Microsoft Entra, both served by the mock), then pick a
[persona](personas.md).

The browser talks to one HTTPS origin. The API proxies the Vite dev server, so cookies, the cross-origin check and the content
security policy behave as in production ([ADR 0004](../adrs/0004-api-serves-the-frontend-on-one-origin.md)).

Worktrees, parallel agents and second checkouts must use [isolated mode](isolated-mode.md).

## Frontend only

```bash
pnpm --dir frontend run dev        # Vite alone; open the app through the API's origin, not Vite's port
pnpm --dir frontend run lint && pnpm --dir frontend run format:check && pnpm --dir frontend run type-check
pnpm --dir frontend run test && pnpm --dir frontend run knip
```
