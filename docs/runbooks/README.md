# Runbooks

Operational checklists for on-call. Each alert policy in `infra/monitoring.tf` links to one of them, and the runbook says what
to look at first and what to do. Replace `<env>`, `<project>` and `<region>` with the values of the environment.

| Runbook | Alert or situation |
|---|---|
| [API 5xx](api-5xx.md) | `api 5xx ratio above 1 %` |
| [API latency](api-latency.md) | `api latency p95 above 1 s` |
| [Health check failing](health-check-failed.md) | `/health check failing` |
| [Migration failed](migration-failed.md) | `migration job failed` |
| [Database CPU](database-cpu.md) | `database CPU above 80 %` |
| [Pub/Sub dead letters](pubsub-dead-letters.md) | `Pub/Sub dead letters`, `Pub/Sub push backlog`, `pushed messages moved to Wolverine dead letters` |
| [Durability pass](durability-pass.md) | `durability pass failed`, `Cloud Scheduler job failed` |
| [Rollback](rollback.md) | A deployment breaks the service (standard 06, rule 11) |
| [Go live](go-live.md) | Checklist before the first production release (standard 22, rule 17) |

Not written, because the template has no such component: event day or peak load, external dependency outage,
projection rebuild (the Marten variant has an inline projection only), session or access revocation. Add them when the
application grows the feature. Standard 22, rule 16 lists them.

Names: Cloud Run service `template-api-<env>`, job `template-migrate-<env>`, Cloud SQL instance `template-<env>`, Pub/Sub
topic `template-messages-<env>`, Cloud Scheduler job `template-durability-pass-<env>`. One GCP
project per environment. Logs and metrics are in the project of the environment.

Telemetry: traces are best effort, because the API runs with request-based billing and the batch exporter only has CPU while a
request is in flight (standard 22, rule 3). Alerts depend on Cloud Monitoring metrics and the uptime check, not on traces.
