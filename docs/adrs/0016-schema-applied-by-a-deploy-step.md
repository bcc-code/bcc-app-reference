# 0016. Schema applied by a deploy step

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADR 0018.

## Context

Standards 04.9, 06.6 and 12.6 forbid schema changes at application startup. Several instances starting at once race on the migration, a failed migration leaves instances crash-looping, and the runtime identity would need DDL rights. The standards allow two setups: a database-manager Cloud Run job executed with `--wait`, or the image's own schema command run through the Auth Proxy in the deploy workflow. An earlier version of the template used a Cloud Run migration job.

## Decision

We will apply the schema with the image's `jasper db-apply` command, which runs the EF Core migrations and creates Wolverine's envelope tables, and check it with `jasper db-assert`:

- **Deploy:** a step in `deploy.yml`, after the image is pushed and before the rollout, starts the Cloud SQL Auth Proxy as `sa-gha-cicd`, runs the pushed image by digest with `db-apply`, then runs `REASSIGN OWNED BY CURRENT_USER TO <db owner role>` so the runtime account can use the objects the CI account created. A failure fails the deploy before any traffic moves. The workflow is never cancelled mid-run (`cancel-in-progress: false`).
- **Locally:** the AppHost runs the API project with `db-apply` as a `schema` resource. The seeder and the API wait for it to complete (standard 02.11), so code that only works because the app migrated itself on start fails locally too.
- **Tests:** the integration test fixture runs `db-apply` and `db-assert` through the API's own entry point, so storage registered there is covered. The unit test `MigrationTests` also fails when the EF model has changes no migration covers (the same check as `dotnet ef migrations has-pending-model-changes`).
- **Previews:** `Persistence:Schema` gives each PR preview its own Postgres schema `pr<N>`, and the preview workflow runs the same `db-apply` step ([0027](0027-pr-previews-in-a-preview-project.md)).
- Schema changes are expand-only, so the previous revision runs against the new schema and a traffic rollback stays safe (standard 06.12).

## Consequences

| | |
|---|---|
| Positive | No extra GCP resource. The schema step runs exactly the image that is rolled out. The runtime account needs no DDL rights. |
| Negative | The deploy runner reaches the database through the proxy, so `sa-gha-cicd` is a member of the database admin group. Deploys take a little longer. |
| Follow-up | Prod uses the same step in its deploy job. |

## Alternatives considered

- **A database-manager Cloud Run job** — runs inside GCP with its own service account, but adds a job resource that must be updated to the new image on every deploy, and an alert and runbook for its failure.
- **Migrate at startup** — forbidden by standard 12.6 for the reasons above.
