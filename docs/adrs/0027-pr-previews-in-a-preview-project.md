# 0027. PR previews run in a preview project, one Postgres schema per PR

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADR 0037 (Proposed there), without the platform mock and the files bucket the template does not have.

## Context

Reviewers want to try a pull request on a real URL before it is merged, with test data and without touching sandbox. [Standard 07](../../.claude/skills/bcc-standards/references/standards/07-preview-environments.md) describes how: a `preview` label, a `decide` job, an `infra/preview/` Terraform root applied per PR, an OIDC mock per preview, and teardown from `main`. It puts each preview's database on a preview Cloud SQL instance that the app owns (07.5, 07.6). This app uses the platform's shared Cloud SQL instance through the landing zone ([0015](0015-shared-platform-postgres-with-iam-auth.md)), which gives one database per environment, and the app may not create an instance of its own. If previews ran in the sandbox project, they would share sandbox's database and the identity that can reach it.

## Decision

We will run previews in a GCP project of their own, the landing zone's `dev` environment, and give each PR one Postgres schema in that environment's single database. The app has no other use for `dev`.

- **Project.** `provision-project.yml` creates the `dev` environment (with `use-shared-db: true`). Nothing in it is shared with sandbox: no database, service account, topic or key. A preview workflow that runs PR code can at most reach other previews. The platform's workflows accept only `prod`, `staging`, `dev` and `sandbox`, so a landing-zone environment named `preview` would need a platform change.
- **Shared part.** `infra/preview-shared/` (component `main`, environment `dev`) holds what all previews use: the enabled APIs, Artifact Registry `ar-<app>-dev` with a cleanup policy on the `pr` tag prefix (07.18), the Data Protection key, and the CI account's membership in the database's admin group.
- **Per PR.** `infra/preview/` is applied into component `pr<N>`. It holds the API and the OIDC mock (`api/OidcMock`) as Cloud Run services named `<app>-<thing>-pr<N>`, a runtime service account in the database's admin group, a Pub/Sub topic with a push subscription and dead-letter topic, a durability-pass scheduler job, and the per-PR secrets.
- **Schema.** The setting `Persistence:Schema` (default `public`) gives a preview its schema `pr<N>`. The EF Core tables and Wolverine's envelope tables live in the same schema, and Wolverine derives its advisory locks from the schema name, so previews do not share those locks. The fixed migration locks are only taken by `db-apply`, so two previews applying at once wait for each other.
- **Lifecycle.** On first deploy and on reopen the workflow drops and recreates the schema, runs `db-apply` and `REASSIGN OWNED` as `deploy.yml` does, and runs the seeder once (07.7, `Seeder:Once`). On a push it runs `db-apply` only. On teardown it drops the schema, then destroys the Terraform workspace. The seeder runs from its image on the runner through the Auth Proxy, so it needs no Cloud Run job.
- **Mocks.** A preview never calls BCC Signon (07.8). The API runs as environment `preview`, with the production checks on (static Wolverine code, required settings, HTTPS metadata), so the OIDC mock gets a confidential sign-in client with a per-PR secret. The mock is pinned to one instance because it keeps its authorization codes in memory; its signing keys are in the preview's schema, so a cold start keeps them. It is deployed to previews only.
- **Ingress.** The `run.app` URL with ingress open to the internet, the documented exception in 04.2 and 07.10. Every route of the API is behind the app's (mock) login (07.19). The OIDC mock has a public `run.app` URL of its own, because the browser has to reach its login page. Anyone with a preview's URL can sign in as any persona, so its data counts as public. There is no gateway route and no Signon client per preview.

This follows 07.5, which places previews in the dev project, and deviates from 07.6, which gives each preview its own database on an instance the app owns.

## Consequences

| | |
|---|---|
| Positive | Previews cannot reach sandbox data: they live in another project. No gateway or Signon change per PR. One database for any number of previews keeps within the landing zone's limit. The API runs with the same settings checks as sandbox, so a preview also tests the production configuration path. |
| Negative | Previews are separated, not isolated: every preview's runtime account is in the same admin group and could read another preview's schema. That is acceptable because previews hold generated test data only (07.20). A third landing-zone environment to keep, and `dev` cannot also host an ordinary deployment of the app. The CI account of the preview project comes from the landing zone and may hold more than the preview needs (07.21). |
| Follow-up | Ask the platform team for the limit on connections per database and user, and whether the preview project's CI account can be narrowed. |

## Alternatives considered

- **A preview Cloud SQL instance owned by the app (07.6).** Isolates previews from each other at the database level, but the app may not create an instance outside the landing zone's shared one.
- **A landing-zone environment named `preview`.** Clearer than borrowing `dev`, but the platform's workflows accept only `prod`, `staging`, `dev` and `sandbox`.
- **Schemas in the sandbox database.** No new project, but preview code would run with an identity that can reach sandbox's own schema.
- **Cloud Run revision tags on the sandbox service.** Tags route through `run.app`, which internal ingress blocks, and a tagged revision shares the Pub/Sub push target with the serving one.
