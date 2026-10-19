---
assessed: 2026-10-19
commit: e4b2b86
updated: 2026-10-19 (gaps 1–4 of the index fixed)
---
# Standards assessment — index

This folder scores the template against the BCC Software Development Standards, one document per standard (01–26), in the
same order as the standards themselves. Each document lists every rule with a status, the evidence (file and line, or the
test that proves it), the gaps to close and what cannot be checked from the repository.

The rules come from `.claude/skills/bcc-standards/references/`. The assessment was made with AI assistance by reading the
code, running the tests and the static checks, and validating Terraform and workflows. Nothing was deployed to GCP or run on
GitHub, so findings about deployed behaviour are inferred from the code and need human verification. Re-run the assessment
when the template changes; the `standards-reviewer` agent can check a single diff.

Updated the same day after gaps 1–4 below were fixed in the working tree. Standards 03, 04, 05, 06, 07, 08 and 16 were
revised; the others still describe commit `e4b2b86`. Verification after the fixes: full build with 0 warnings,
`dotnet format --verify-no-changes` clean, 216/216 backend tests, E2E 6/6, `actionlint` clean, `terraform fmt` and
`terraform validate` pass for `infra/` and `infra/preview/`. Still nothing deployed.

Status: ✅ follows · ⚠️ partly, or a deviation recorded in an ADR · ❌ does not follow · N/A does not apply.

## Scorecard

| # | Standard | Score | ✅ | ⚠️ | ❌ | N/A |
|---|---|---|---|---|---|---|
| 01 | [Repository layout and build](01-repository-layout-and-build.md) | Strong | 10 | 5 | 0 | 1 |
| 02 | [Local development](02-local-development.md) | Adequate | 13 | 1 | 3 | 1 |
| 03 | [Infrastructure as code](03-infrastructure-as-code.md) | Adequate | 15 | 5 | 0 | 0 |
| 04 | [GCP runtime](04-gcp-runtime.md) | Adequate | 15 | 0 | 1 | 3 |
| 05 | [CI pipeline](05-ci-pipeline.md) | Adequate | 10 | 4 | 0 | 1 |
| 06 | [Deployment and release](06-deployment-and-release.md) | Adequate | 10 | 2 | 1 | 2 |
| 07 | [Preview environments](07-preview-environments.md) | Weak | 13 | 7 | 1 | 0 |
| 08 | [Code generation and contracts](08-code-generation-and-contracts.md) | Adequate | 9 | 3 | 2 | 1 |
| 09 | [Endpoints and API design](09-endpoints-and-api-design.md) | Strong | 9 | 3 | 0 | 1 |
| 10 | [Vertical slices and CQRS](10-vertical-slices-and-cqrs.md) | Adequate | 5 | 2 | 1 | 4 |
| 11 | [Domain modelling and errors](11-domain-modelling-and-errors.md) | Strong | 9 | 1 | 0 | 1 |
| 12 | [Data access and migrations](12-data-access-and-migrations.md) | Adequate | 8 | 2 | 1 | 4 |
| 13 | [Messaging and integration](13-messaging-and-integration.md) | Absent (by design) | 1 | 0 | 0 | 13 |
| 14 | [Authentication and sessions](14-authentication-and-sessions.md) | Strong | 13 | 0 | 1 | 4 |
| 15 | [Authorization and permission checks](15-authorization-and-permission-checks.md) | Strong | 9 | 1 | 0 | 3 |
| 16 | [HTTP security hardening](16-http-security-hardening.md) | Strong | 14 | 2 | 0 | 2 |
| 17 | [Supply chain and security scanning](17-supply-chain-and-security-scanning.md) | Adequate | 9 | 2 | 2 | 1 |
| 18 | [Privacy and GDPR](18-privacy-and-gdpr.md) | Weak | 6 | 5 | 2 | 2 |
| 19 | [Testing strategy](19-testing-strategy.md) | Adequate | 12 | 6 | 1 | 1 |
| 20 | [Screenshots and visual documentation](20-screenshots-and-visual-documentation.md) | Adequate | 10 | 0 | 2 | 1 |
| 21 | [Frontend](21-frontend.md) | Strong | 17 | 3 | 0 | 3 |
| 22 | [Observability and operations](22-observability-and-operations.md) | Weak | 9 | 3 | 4 | 1 |
| 23 | [Documentation and decisions](23-documentation-and-decisions.md) | Adequate | 5 | 6 | 1 | 3 |
| 24 | [AI agent tooling](24-ai-agent-tooling.md) | Strong | 11 | 2 | 0 | 1 |
| 25 | [Code quality guards](25-code-quality-guards.md) | Strong | 10 | 3 | 0 | 1 |
| 26 | [Gateway and frontend hosting](26-gateway-and-frontend-hosting.md) | Strong | 13 | 1 | 1 | 0 |
| | **Total** | 10 Strong · 12 Adequate · 3 Weak · 1 Absent | **265** | **69** | **24** | **55** |

The application code is the strongest part: authentication, authorization, HTTP hardening, endpoints, domain modelling and
the frontend each have their security and design rules backed by tests. The gaps are mostly in what has not run yet: the
deployed runtime, preview environments, observability and privacy operations.

## Gaps to close first

Ordered by risk. Items 1–4 were confirmed by the coordinator against the code; the rest come from the individual documents.

1. ~~**The deployed API will not start** (04).~~ Fixed: `infra/cloudrun_api.tf` sets `Frontend__StaticPath = "/app/wwwroot"`
   in `local.api_env`, which the migration job merges, and `infra/preview/main.tf` sets it on the preview container. Not
   deployed, so startup in GCP is still unverified.
2. ~~**The sandbox preview account can take over other service accounts** (03.11, 07).~~ Fixed: the preview account lost
   `roles/iam.serviceAccountAdmin` and the project-wide `serviceAccountUser`; it may act only as a shared runtime account
   created by the main Terraform. Project-wide `roles/run.admin` remains (03.11).
3. ~~**`/index.html` is served as a static file before authentication** (16.8).~~ Fixed: `UseStaticFiles` skips
   `/index.html` in any letter case; `SecurityTests.The_built_index_html_is_never_served_as_a_static_file` covers it in both
   variants.
4. ~~**Preview images are built without Wolverine pre-generated code** (08.8–9, 07).~~ Fixed: `preview.yml` runs
   `codegen write` before publishing, and the `backend` job in `pr.yml` generates the code and builds Release for every API
   project.
5. **Container images are not pinned by digest and no chiseled base is set** (17.8, 04.8). The deploy step name claims the
   base image is set in the project file; it is not.
6. **No traces leave deployed environments** (22.3). No OTLP endpoint or collector is configured for Cloud Run.
7. **Retention and erasure are missing** (18.6–7), and the data inventory claims session rows are deleted at expiry, which
   no code does.
8. **Rollback leaves traffic pinned** (06.11). `deploy.yml` does not use `--to-latest`, so the next deploy does not take
   traffic back after a rollback, contrary to `docs/runbooks/rollback.md`.
9. **The Marten projection has no rebuild path** (10.12, 12.14), and the runbook index says none is needed.
10. **Missing runbooks** for the external OIDC provider outage and session revocation (22.16).

## Claims in the template's own docs that are wrong

The documents above list these where they belong; collected here so they are fixed together.

- `deploy.yml`: the step name says the chiseled base image is set in the project file.
- `docs/pr-previews.md`, `preview.yml`: say the preview API works; it has no database or settings. `preview.yml` also says the
  seeder does not exist.
- `docs/privacy/data-inventory.md`: sessions deleted at expiry; events record the acting owner (only `NoteCreated` does).
- `docs/runbooks/README.md`: traces are "best effort" (they are not exported); no projection rebuild needed; no external
  dependency runbook needed.
- `docs/runbooks/rollback.md`: the next deploy restores traffic to the latest revision.
- `AGENTS.md`: "axe in every flow" (two E2E tests skip it).
- `docs/README.md`: every AI-drafted document is marked (most are not).
- `docs/tech-debt/README.md`: "none yet", while known issues sit in TODO comments.
- `infra/README.md`: `infra.yml` runs on pushes to `main` (it has no push trigger).
