# 0002. Repository layout and stack

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADR 0002.

## Context

The BCC standards describe a default stack and layout, and the existing BCC apps follow it with small differences. Using the same shape means people and agents can move between repositories without relearning where things are. The template exists to be copied into new applications, so it should be the smallest complete example of that shape.

## Decision

We will use the BCC default stack and layout:

- **Backend:** .NET 11 and ASP.NET Core under `api/`, with the namespace prefix `BccCode.Template` (renamed by `scripts/init-app.ps1`). Projects: `BccCode.Template.Api` (host and composition root only, see [0025](0025-one-feature-project-with-a-host-only-api.md)), `BccCode.Template` (all feature slices and persistence), `BccCode.Template.TestData` (the seeder), `OidcMock`, `ServiceDefaults`, `AppHost`, and the test projects `BccCode.Template.Tests`, `BccCode.Template.IntegrationTests`, `BccCode.Template.E2eTests` and `BccCode.Template.ArchitectureTests` under `api/tests/`.
- **Persistence:** one EF Core app on Postgres (EF Core, the migrations and the schema step are in [0016](0016-schema-applied-by-a-deploy-step.md)). The template has no second variant.
- **Frontend:** Vue 3, TypeScript (strict) and Vite under `frontend/`, using the BCC component library, Tailwind 4, TanStack Query, vue-router and vue-i18n. pnpm with an exact version pin and the supply-chain settings from the standards.
- **Build:** one `BccCode.Template.slnx` with solution folders, central package management with transitive pinning, NuGet lock files, warnings as errors and nullable enabled for every project, and a pinned SDK in `global.json`.

## Consequences

| | |
|---|---|
| Positive | Same layout and conventions as the other BCC apps; the BCC standards and their examples apply directly. |
| Negative | The tooling (pnpm settings, lock files, analyzers) comes with the template from the start, before an application has much code. |
| Follow-up | Add projects only when there is a separate deployable or a hard dependency boundary (standard 01.2). |

## Alternatives considered

- **A `Backend/` and `Frontend/` split with several layer projects** — it splits one feature across projects, which standard 01.3 tells us to avoid.
- **A `src/` and `tests/` split** — none of the reference repositories use it, and it gives no benefit over `api/` and `frontend/`.
