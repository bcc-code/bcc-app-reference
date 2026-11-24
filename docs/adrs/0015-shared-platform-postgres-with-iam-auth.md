# 0015. Shared platform Postgres with IAM authentication

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADR 0017.

## Context

The app needs Postgres in GCP. The BCC platform landing zone gives a project its own database on a shared Cloud SQL instance (`use-shared-db: true` in Provision Project). Standard 04.4 requires the Cloud SQL Auth Proxy with IAM authentication when a service uses Cloud SQL. An earlier version of the template ran its own instance per environment.

## Decision

We will use a database on the platform's shared Cloud SQL instance.

- Provision Project runs with `use-shared-db: true`. The landing zone creates the database and its owner role and publishes `shared_db.db_name`, `shared_db.connection_name` and `shared_db.admin_group_id` in the environment's `PROPS`.
- `infra/database.tf` adds the API's runtime service account and `sa-gha-cicd` to the shared database's admin group. The landing zone grants that group `roles/cloudsql.client` and `roles/cloudsql.instanceUser`, so this project grants no Cloud SQL role.
- The Cloud Run service runs a Cloud SQL Auth Proxy sidecar with `--auto-iam-authn`. The app connects to `127.0.0.1:5432` as its service account, with no password.
- The tables belong to this service only (standard 12.3). Other systems integrate through the API or messages.

## Consequences

| | |
|---|---|
| Positive | No instance to run, patch, back up or pay for per environment. No database passwords or secrets. |
| Negative | Capacity, maintenance windows, Postgres version and backups are shared with other apps and owned by the platform team. A noisy neighbour or an instance outage affects this app too. The connection goes straight to Cloud SQL, with no managed pooler. Wolverine's durability pass lock is transaction-scoped (`UseTransactionScopedDurabilityPassLock = true`), so a transaction-mode pooler would also work. |
| Follow-up | Revisit a dedicated instance if the load or isolation needs of an application outgrow the shared one. If the platform turns on pooling in transaction mode, give the message store a direct connection first. |

## Alternatives considered

- **A dedicated Cloud SQL instance in `infra/`** — full control and isolation, but an always-on instance per environment and our own backups, upgrades and maintenance windows. Not needed for the expected load.
