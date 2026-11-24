---
name: bcc-standards
description: Apply the BCC Software Development Standards when writing or changing code, configuration, CI workflows, infrastructure (Terraform) or docs. Use when adding an endpoint, vertical slice, migration, test, workflow, Terraform resource, frontend page or ADR, when touching authentication, authorization, headers, logging or code that handles personal data, when setting up a new repo, and when asked whether something follows the standards or whether a deviation is acceptable.
---

# BCC Software Development Standards

The standards cover how a repo is set up, built, secured, tested, deployed and operated. They are written for a .NET (ASP.NET Core, Wolverine, Marten or EF Core, Postgres) backend, a Vue 3 + TypeScript frontend, .NET Aspire for local development, GitHub Actions for CI and Terraform on Google Cloud.

The rules live in `references/standards/`, one file per standard (01 to 26). Each file has a **Rules** section and an **Examples** section with code. Rule ids are written `<standard>.<rule>`: `16.3` is rule 3 of standard 16. The reasoning behind the rules is in `references/principles/`, and the stack is described in `references/technology/`.

These standards were drafted with AI assistance. Treat a rule as the team's default, and point out when a rule looks wrong instead of silently working around it.

## How to use

1. Find the area you are changing in the table below.
2. Read that standard before you write code: the Rules section, then the Examples section for the pattern to follow.
3. Apply the rules by their keyword:
   - **MUST** is required. The only exception is an ADR in the repo's `docs/adrs/` that says why.
   - **SHOULD** is the default. A deviation needs a short comment next to the code that explains why, or an ADR.
   - **MAY** is optional.
4. When you deviate, leave the comment or ADR in the same change. Do not deviate silently.
5. When a rule seems wrong or outdated, say so in the PR description or in a `docs/tech-debt/` file.
6. Follow the existing code in the repo first. Copy the pattern from the nearest existing slice, test or workflow, then check it against the standard.
7. When reviewing a change, cite rule ids in findings (for example "violates 15.4: anonymous endpoint not on the allowlist").

## Which standard to read

| You are doing | Read |
|---|---|
| Adding a project, package or tool; changing build props, `global.json`, `.slnx` | [01](references/standards/01-repository-layout-and-build.md), [25](references/standards/25-code-quality-guards.md) |
| Changing `aspire run`, the AppHost, seeding, mocks, ports, `--isolated` | [02](references/standards/02-local-development.md) |
| Editing Terraform, IAM, service accounts, secrets, registries | [03](references/standards/03-infrastructure-as-code.md), [04](references/standards/04-gcp-runtime.md) |
| Cloud Run settings, probes, Cloud SQL, Data Protection keys, assets, log format | [04](references/standards/04-gcp-runtime.md), [22](references/standards/22-observability-and-operations.md) |
| Adding or editing a GitHub workflow or composite action | [05](references/standards/05-ci-pipeline.md), [17](references/standards/17-supply-chain-and-security-scanning.md) |
| Deploying, rollback, migrations in a deploy, versioning, release notes | [06](references/standards/06-deployment-and-release.md), [12](references/standards/12-data-access-and-migrations.md) |
| Per-PR preview environments | [07](references/standards/07-preview-environments.md) |
| OpenAPI contract, generated client, Wolverine codegen, translation keys, mappers, log methods | [08](references/standards/08-code-generation-and-contracts.md) |
| Adding or changing an endpoint, DTO, route, error response | [09](references/standards/09-endpoints-and-api-design.md), [11](references/standards/11-domain-modelling-and-errors.md) |
| Adding a vertical slice or handler, folder layout, transactions | [10](references/standards/10-vertical-slices-and-cqrs.md) |
| Business rules, value types, time, error handling | [11](references/standards/11-domain-modelling-and-errors.md) |
| Queries, entities, documents, events, migrations, concurrency | [12](references/standards/12-data-access-and-migrations.md) |
| Messages, webhooks, Pub/Sub, Cloud Tasks, idempotency | [13](references/standards/13-messaging-and-integration.md) |
| Login, sessions, cookies, logout, bearer schemes, service-to-service auth | [14](references/standards/14-authentication-and-sessions.md) |
| Policies, permission checks, ownership, admin access | [15](references/standards/15-authorization-and-permission-checks.md) |
| Headers, CSP, CSRF, CORS, rate limiting, redirects, HTML sanitizing | [16](references/standards/16-http-security-hardening.md) |
| Dependencies, Dependabot, pinning, secret scanning, SAST, SBOM | [17](references/standards/17-supply-chain-and-security-scanning.md) |
| Personal data, logging identifiers, retention, deletion, audit, test data | [18](references/standards/18-personal-data-and-gdpr.md) |
| Writing or fixing tests, Testcontainers, E2E, load tests, accessibility checks | [19](references/standards/19-testing-strategy.md) |
| Screenshots for a PR or for docs | [20](references/standards/20-screenshots-and-visual-documentation.md) |
| Vue, TypeScript, TanStack Query, i18n, frontend lint and tests | [21](references/standards/21-frontend.md) |
| Logging, metrics, traces, health endpoints, alerts, runbooks | [22](references/standards/22-observability-and-operations.md) |
| ADRs, specs, docs layout, tech-debt files, user manual | [23](references/standards/23-documentation-and-decisions.md) |
| `AGENTS.md`, skills, agents, `.mcp.json`, `.claude/` files | [24](references/standards/24-ai-agent-tooling.md) |
| Analyzers, startup assertions | [25](references/standards/25-code-quality-guards.md) |
| Gateway route, backend services, assets bucket, ingress | [26](references/standards/26-gateway-and-frontend-hosting.md), [04](references/standards/04-gcp-runtime.md) |

Most changes touch more than one standard. A new endpoint usually needs 09, 10, 11, 15 and 19; a new workflow needs 05 and 17.

For design questions (how to structure a feature, whether to add an abstraction, how to model a process), read the principles:

| Question | Read |
|---|---|
| Is this code readable, simple, commented well? Is this abstraction needed? | [code-design](references/principles/code-design.md) |
| How to structure features, domain logic, errors, data access, events | [architecture-and-domain](references/principles/architecture-and-domain.md) |
| How to model a system or business process before building it | [modelling](references/principles/modelling.md) |
| Threats, security concepts, why a security rule exists | [security](references/principles/security.md) |
| Docs, decisions, project setup, testing approach, test data, accessibility | [ways-of-working](references/principles/ways-of-working.md) |
| How to use a library or platform in the stack | [backend-dotnet](references/technology/backend-dotnet.md), [backend-go](references/technology/backend-go.md), [frontend-vue](references/technology/frontend-vue.md), [cloud-and-tools](references/technology/cloud-and-tools.md) |

## Rules that apply everywhere

- Comment the why, not the what. Plain, factual prose.
- Prefer readable code over clever code, and do not add abstractions, options or layers before they are needed.
- Never delete, skip or weaken a test to get green. Fix the cause, or stop and report. A test may go when its behaviour is gone or a more real test already covers it; name that test in the commit (24.4).
- Out-of-scope findings go to `docs/tech-debt/`, one file each, instead of widening the change (23.14).
- No secrets, real personal data or machine-specific paths in committed files (17.14, 18.15).
- Do not log direct personal identifiers such as names, emails or phone numbers (18.9).
- Domain code gets the time from `TimeProvider` (or the database clock where that is the source of truth), injected so tests can use a fake clock (11.7).
- Expected failures are values (nullable typed results such as `DomainError`, null to continue), not exceptions (09.2, 09.10, 11.8).
- Every endpoint is protected by default; anonymous routes are on an explicit allowlist (15.3, 15.4).
- An existing resource the caller may not use returns 403; 404 is for one that does not exist, or whose existence is personal data (15.8).
- Data access is expressed as named queries in the feature that needs it, not through generic repositories (12).
- Schema migrations never run at application startup in shared environments (12.6).
- Endpoints and DTOs are task-oriented; never expose database entities over the API (09).

## Baseline checklist for a new repo

- [ ] `.slnx`, `Directory.Build.props` with Nullable and TreatWarningsAsErrors, central package management with transitive pinning, lock files, pinned `global.json` and tool manifest (01)
- [ ] Analyzers and `dotnet format` check (25)
- [ ] `aspire run` starts everything with mocks for every external system, `--isolated` mode, seeder with an environment allowlist, HTTPS on `*.dev.localhost` (02)
- [ ] `AGENTS.md` (and `CLAUDE.md` containing `@AGENTS.md`), `.mcp.json` with the Aspire MCP server, `docs/tech-debt/` (24)
- [ ] BFF authentication, default-deny authorization with an endpoint sweep test (14, 15)
- [ ] Security headers, CSP, cross-origin (CSRF) check (16)
- [ ] Committed OpenAPI document and generated Hey API client with a drift check (08)
- [ ] PR workflow with SHA-pinned actions, gitleaks, SAST (CodeQL or Semgrep); Dependabot; pnpm hardening block (05, 17)
- [ ] Branch protection with required checks; build once and promote; migrations as a deploy step; smoke test (06)
- [ ] Terraform per concern, workload identity federation, one service account per caller, modules pinned (03, 04)
- [ ] Gateway route, backends through the gateway modules, private assets bucket, ingress locked to the load balancer (26)
- [ ] Preview environment workflow, if the app uses previews (07)
- [ ] OpenTelemetry via ServiceDefaults, `/health` and `/alive` wired to probes, alerts with runbooks (22)
- [ ] `docs/adrs/`, `docs/architecture/` with C4 context and container diagrams, `docs/runbooks/`, data inventory (23, 18)

Standards 07 and 13 describe features not every app has. Read them when the app adds per-PR previews or messaging.

## In this repo

- This repo is the template new BCC apps start from, and it follows bcc-ateam, the reference app, wherever the two could differ. When a rule and bcc-ateam disagree, say so in a `docs/tech-debt/` file instead of picking one silently.
- To review a diff against the standards, use the `standards-reviewer` agent.
- This copy of the standards is taken from bcc-ateam. Do not edit the references to fit a change; propose the change to the standards instead.
