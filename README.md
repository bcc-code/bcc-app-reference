# BCC App Template

A repository template for new BCC applications: .NET 11, Wolverine.HTTP, EF Core on Postgres, Aspire, Vue 3, Terraform on GCP. It
follows the BCC Software Development Standards and is built from the practices of the BCC A-team application and of orginn,
bcc-privacy and bcc-vote. One sample app, personal notes, shows every layer end to end.

## What is included

- **Local environment**: `aspire run` starts Postgres, the schema step, a seeder, a mock OIDC provider with test personas, the API
  and the Vite dev server behind one HTTPS origin, and a C4 diagram viewer. `--isolated` for worktrees.
- **Security**: BFF authentication (opaque cookie, server-side sessions, PKCE, back-channel logout, a second factor for administrators),
  default-deny authorization with an anonymous-route allowlist and a `Permission` step per endpoint, cross-origin protection instead of
  CSRF tokens, security headers and nonce CSP.
- **Sample feature**: personal notes as vertical slices with value types, typed ProblemDetails, optimistic concurrency, ownership checks
  (403 and 404) and a typed history table per audited table with a `change` table of business steps.
- **Quality guards**: warnings as errors, banned APIs at compile time, central package management with lock files, architecture tests,
  endpoint policy sweep, OpenAPI and client drift checks, i18n completeness, axe in the E2E tests.
- **CI/CD**: one `ci` check (backend, integration, E2E on the AppHost, codegen and image, contracts, frontend, infra, C4, gitleaks,
  Semgrep), build-once deploys through the BCC landing zone, PR previews, Dependabot with cooldown. Actions are pinned to SHAs.
- **Infrastructure**: Terraform for Cloud Run behind the platform gateway, shared Cloud SQL with IAM authentication, Pub/Sub, KMS,
  telemetry to the BCC collector, monitoring alerts with runbooks.
- **Docs**: ADRs, C4 diagrams, runbooks, data inventory, tech-debt folder, Diátaxis stubs.
- **Agent tooling**: `AGENTS.md`, the `bcc-standards` skill and `standards-reviewer` agent, Aspire MCP and skills for Aspire,
  Wolverine, relational slices, screenshots, PRs, permission tests, user docs and ClickUp.

## Create a new app

1. On GitHub, choose **Use this template** and create the repository.
2. Clone it and run the init script (PowerShell 7). Pick a port base whose 100 ports no other BCC app on your machine uses
   (the template uses 16000):

   ```bash
   pwsh scripts/init-app.ps1 -Name MyApp -PortBase 17000 -WhatIf   # preview
   pwsh scripts/init-app.ps1 -Name MyApp -PortBase 17000
   ```

   It renames `BccCode.Template` to `BccCode.MyApp` and `template` to `myapp`, moves the ports, and prints the remaining steps
   (regenerate lock files, the OpenAPI document and the client, run the tests).
3. Provision the project with `provision-project.yml` and follow the [first-deploy runbook](docs/runbooks/first-deploy.md); fill in the
   project-specific values in `infra/` and the Config section of the ClickUp skills, and set the `CODEOWNERS` team.
4. Set up the repository settings in [docs/development/repository-settings.md](docs/development/repository-settings.md).
5. Replace the Notes sample with the first real feature (the `relational-slice` skill describes the pattern), and review
   [the data inventory](docs/data-protection/data-inventory.md).

## Prerequisites

- .NET SDK 11.0.100 (`global.json`)
- Docker
- Node 24 and pnpm 12.17.0 (pnpm downloads the pinned version through `devEngines`)
- A trusted development certificate: `dotnet dev-certs https --trust`
- PowerShell 7 for `scripts/init-app.ps1`

## Run

```bash
dotnet tool restore
pnpm --dir frontend install
aspire run
```

With the template's port base:

| What | URL |
|---|---|
| Aspire dashboard | https://localhost:16050 |
| The app | https://template.dev.localhost:16000 |
| Persona picker (sign-in) | https://localhost:16020/login |
| Database UI (DBX) | http://localhost:16011 |
| C4 diagrams | http://localhost:16060/workspace/1/diagrams |

Sign in by picking a persona: `user`, `user2`, `admin` or `admin-no-mfa` ([personas](docs/development/personas.md)). In a git
worktree or a second checkout start with `aspire run --isolated`; `aspire describe` prints the real URLs
([isolated mode](docs/development/isolated-mode.md)).

## Develop

See [AGENTS.md](AGENTS.md) for commands, rules and gotchas, and [docs/development](docs/development/README.md) for the details by
task. Decisions are in [docs/adrs](docs/adrs/README.md).

## Status

The rules and much of the code were drafted with AI assistance from the source repositories and the standards. Review the code, the
Terraform and especially the privacy and security documents before relying on them in production.
