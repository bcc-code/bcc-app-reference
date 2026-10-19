---
name: bcc-standards
description: Apply the BCC Software Development Standards when writing or changing code, configuration, CI workflows, infrastructure (Terraform) or docs in this repo. Use when adding an endpoint, vertical slice, migration, test, workflow, Terraform resource, frontend page or ADR, when touching auth, headers, logging or privacy-sensitive code, and when asked whether something follows the standards or whether a deviation is acceptable.
---

# BCC Software Development Standards

> Status: draft, produced with AI assistance from code audits of three BCC repos (orginn, bcc-privacy, vote-rewrite) and checked by humans. Verify a rule before treating it as decided. Decisions to adopt or change a rule are recorded as ADRs in `docs/adrs/`.

The full standards live in a personal Obsidian vault that is not available in this repo. The rules are carried in `references/`, one file per standard (01 to 26), with the numbering kept. Rule ids are written `<standard>.<rule>`, for example `16.3` is rule 3 of standard 16.

## How to use

1. Find the area you are changing in the table below.
2. Read that reference file before you write code. Read the **Rules** section; the **Example** section says where this template implements it.
3. Apply the rules by their keyword:
   - **MUST** is required. The only exception is an ADR in `docs/adrs/` that says why.
   - **SHOULD** is the default. A deviation needs a short `// why` comment next to the code, or an ADR.
   - **MAY** is optional.
4. When you deviate, leave the comment or ADR in the same change. Do not deviate silently.
5. When a rule seems wrong or outdated, say so in the PR description or a `docs/tech-debt/` file. Do not edit the references to fit your change.
6. To review a diff against the standards, use the `standards-reviewer` agent.

Follow the existing code first: this template already implements most rules, so copy the pattern from the nearest existing slice, test or workflow and check it against the reference.

## Which reference to read

| You are doing | Read |
|---|---|
| Adding a project, package, tool or changing build props, `global.json`, `.slnx` | [01](references/01-repository-layout-and-build.md), [25](references/25-code-quality-guards.md) |
| Changing `aspire run`, the AppHost, seeding, mocks, ports, `--isolated` | [02](references/02-local-development.md) |
| Editing Terraform, IAM, service accounts, secrets, registries | [03](references/03-infrastructure-as-code.md), [04](references/04-gcp-runtime.md) |
| Cloud Run settings, probes, Cloud SQL, Data Protection keys, assets, logs format | [04](references/04-gcp-runtime.md), [22](references/22-observability-and-operations.md) |
| Adding or editing a GitHub workflow or composite action | [05](references/05-ci-pipeline.md), [17](references/17-supply-chain-and-security-scanning.md) |
| Deploying, rollback, migrations in a deploy, versioning, release notes | [06](references/06-deployment-and-release.md), [12](references/12-data-access-and-migrations.md) |
| Per-PR preview environments | [07](references/07-preview-environments.md) |
| Changing the OpenAPI contract, generated client, Wolverine codegen, translation keys, mappers, log methods | [08](references/08-code-generation-and-contracts.md) |
| Adding or changing an endpoint, DTO, route, error response | [09](references/09-endpoints-and-api-design.md), [11](references/11-domain-modelling-and-errors.md) |
| Adding a vertical slice or handler, folder layout, transactions | [10](references/10-vertical-slices-and-cqrs.md) |
| Business rules, value types, time, error handling | [11](references/11-domain-modelling-and-errors.md) |
| Queries, entities, documents, events, migrations, concurrency | [12](references/12-data-access-and-migrations.md) |
| Messages, webhooks, Pub/Sub, Cloud Tasks, idempotency | [13](references/13-messaging-and-integration.md) |
| Login, sessions, cookies, logout, bearer schemes, service-to-service auth | [14](references/14-authentication-and-sessions.md) |
| Policies, permission checks, ownership, admin access | [15](references/15-authorization-and-permission-checks.md) |
| Headers, CSP, CSRF, CORS, rate limiting, redirects, HTML sanitizing | [16](references/16-http-security-hardening.md) |
| Dependencies, Dependabot, pinning, secret scanning, SAST, SBOM | [17](references/17-supply-chain-and-security-scanning.md) |
| Personal data, logging identifiers, retention, deletion, audit, test data | [18](references/18-privacy-and-gdpr.md) |
| Writing or fixing tests, Testcontainers, E2E, load tests, accessibility checks | [19](references/19-testing-strategy.md) |
| Screenshots for a PR or for docs | [20](references/20-screenshots-and-visual-documentation.md) |
| Vue, TypeScript, TanStack Query, i18n, frontend lint and tests | [21](references/21-frontend.md) |
| Logging, metrics, traces, health endpoints, alerts, runbooks | [22](references/22-observability-and-operations.md) |
| ADRs, specs, docs layout, tech-debt files, user manual | [23](references/23-documentation-and-decisions.md) |
| `AGENTS.md`, skills, agents, `.mcp.json`, `.claude/` files | [24](references/24-ai-agent-tooling.md) |
| Analyzers, architecture tests, banned APIs, startup assertions | [25](references/25-code-quality-guards.md) |
| Gateway route, backend services, assets bucket, ingress | [26](references/26-gateway-and-frontend-hosting.md), [04](references/04-gcp-runtime.md) |

Most changes touch more than one area. A new endpoint usually needs 09, 10, 11, 15 and 19; a new workflow needs 05 and 17.

## Baseline checklist

The short list from the standards index for a repo created from this template. Each item names the reference with the detail.

- [ ] `.slnx`, `Directory.Build.props` with Nullable and TreatWarningsAsErrors, central package management with transitive pinning, lock files, pinned `global.json` and tool manifest (01)
- [ ] Analyzers, `dotnet format` check, architecture test project (25; this template has no Slopwatch, see ADR 0006)
- [ ] `aspire run` starts everything with mocks for every external system, `--isolated` mode, seeder with an environment allowlist, HTTPS on `*.dev.localhost` (02)
- [ ] `AGENTS.md` (and `CLAUDE.md` containing `@AGENTS.md`), `.mcp.json` with the Aspire MCP server, `docs/tech-debt/` (24)
- [ ] BFF authentication, default-deny authorization with an endpoint sweep test (14, 15)
- [ ] Security headers, CSP, CSRF middleware, rate limiting (16)
- [ ] Committed OpenAPI document and generated Hey API client with a drift check (08)
- [ ] PR workflow with SHA-pinned actions, gitleaks, CodeQL; Dependabot; pnpm hardening block (05, 17)
- [ ] Branch protection with required checks; build once and promote; migrations as a deploy step; smoke test (06)
- [ ] Terraform per concern, workload identity federation, one service account per caller, modules pinned (03, 04)
- [ ] Gateway route, backends through the gateway modules, private assets bucket, ingress locked to the load balancer (26)
- [ ] Preview environment workflow (07)
- [ ] OpenTelemetry via ServiceDefaults, `/health` and `/alive` wired to probes, alerts with runbooks (22)
- [ ] `docs/adrs/`, `docs/architecture/` with C4 context and container diagrams, `docs/runbooks/`, data inventory (23, 18)

## Rules of thumb that apply everywhere

- Comment the why, not the what. Plain, factual prose.
- Never delete, skip or weaken a test to get green. Fix the cause or stop and report (24.4).
- Out-of-scope findings go to `docs/tech-debt/`, one file each, instead of widening the change (23.14).
- No secrets, real personal data or machine-specific paths in committed files (17.14, 18.15).
- Time comes from `TimeProvider`, never `DateTime.UtcNow` (11.7, 11.8).
- Expected failures are values and ProblemDetails, not exceptions (09.10, 11.9).
- Every endpoint is protected by default; anonymous routes are on an explicit allowlist (15.3, 15.4).
- Schema migrations never run at API startup (12.6).

## Where this template differs from the standards

- Host and feature code share one project per persistence variant (deviation from 01.2); an ADR in `docs/adrs/` records why.
- No Slopwatch (deviation from 24.6 and 25.9, and the Slopwatch mentions in 01.13 and 05): `docs/adrs/0006-no-slopwatch.md`. Do not report its absence; do look for disabled tests, suppressed warnings and empty catch blocks in review.
- Standards 07, 13 and 26 describe features the template does not include. Read them when the app adds that feature; the Example section names the source repo.
- Paths in the Example sections show where the template places each rule. If a path does not exist, search the repo before assuming the rule is unmet.
