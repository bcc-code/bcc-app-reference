# BCC App Template

A repository template for new BCC applications: .NET 10, Wolverine.HTTP, Aspire, Postgres, Vue 3, Terraform on GCP. It
follows the BCC Software Development Standards and is built from the practices that worked best in orginn, bcc-privacy and
bcc-vote.

It contains two sample apps with the same HTTP contract and frontend, differing only in persistence:

| Variant | Project | Use when |
|---|---|---|
| Marten | `api/BccCode.Template.Api.Marten` | The domain benefits from event sourcing or documents (history, audit, projections). |
| EF Core | `api/BccCode.Template.Api.EfCore` | The domain is relational or reporting-heavy. |

See [ADR 0003](docs/adrs/0003-marten-or-ef-core.md).

## What is included

- **Local environment**: `aspire run` starts Postgres, the schema step, seed data, a mock OIDC provider with test personas,
  the API and the Vite dev server behind one HTTPS origin. `--isolated` for worktrees.
- **Security**: BFF authentication (opaque cookie, server-side sessions, PKCE, PAR, back-channel logout), default-deny
  authorization with an anonymous-route allowlist, CSRF for JSON APIs, security headers and nonce CSP, rate limiting.
- **Sample feature**: personal notes as vertical slices with value types, ProblemDetails, optimistic concurrency and
  ownership checks.
- **Quality guards**: warnings as errors, central package management with lock files, architecture tests with a
  forbidden-API scanner, endpoint policy sweep, OpenAPI drift checks, i18n completeness, axe in E2E tests.
- **CI/CD**: path-aware PR workflow, CodeQL, gitleaks, SBOM, Dependabot with cooldown, build-once-and-promote deploys,
  preview environments, Terraform plan/apply. All actions pinned to SHAs.
- **Infrastructure**: Terraform for Cloud Run, Cloud SQL, migration job, IAM per caller, Workload Identity Federation, KMS,
  monitoring alerts with runbooks.
- **Docs**: ADRs, C4 diagrams, runbooks, data inventory, tech-debt folder, Diátaxis stubs.
- **Agent tooling**: `AGENTS.md`, the `bcc-standards` skill and `standards-reviewer` agent, Aspire MCP, and skills for
  Aspire, screenshots, PRs, permission tests, user docs and ClickUp.

## Create a new app

1. On GitHub, choose **Use this template** and create the repository.
2. Clone it and run the init script (PowerShell 7):

   ```bash
   pwsh scripts/init-app.ps1 -Name MyApp -Persistence marten -WhatIf   # preview
   pwsh scripts/init-app.ps1 -Name MyApp -Persistence marten
   ```

   It deletes the other variant, renames `BccCode.Template` to `BccCode.MyApp` and `template` to `myapp`, and prints the
   remaining steps (regenerate lock files, OpenAPI and client, run the tests).
3. Fill in the project-specific values: `infra/*.tfvars`, GitHub environments and variables (`infra/README.md`), the
   `CODEOWNERS` team, and the Config section of the ClickUp skills.
4. Set up the repository settings listed in [docs/development.md](docs/development.md#repository-settings).
5. Replace the Notes sample with the first real feature, and review `docs/privacy/data-inventory.md`.

## Develop

See [AGENTS.md](AGENTS.md) for commands, rules and gotchas, and [docs/development.md](docs/development.md) for details.

```bash
dotnet tool restore
pnpm --dir frontend install
aspire run
```

## Status

The rules and much of the code were drafted with AI assistance from the source repositories and the standards. Review the
code, the Terraform and especially the privacy and security documents before relying on them in production.
