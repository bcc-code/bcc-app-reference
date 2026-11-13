# Runbooks

Operational checklists for on-call. Each alert policy in `infra/monitoring.tf` links to one of them, and the runbook says what
to look at first and what to do. Replace `<env>`, `<project>`, `<region>` and `<app>` with the values of the environment.

| Runbook | Alert or situation |
|---|---|
| [API 5xx](api-5xx.md) | `api 5xx ratio above 1 %` |
| [API latency](api-latency.md) | `api latency p95 above 1 s` |
| [Health check failing](health-check-failed.md) | `/health check failing` |
| [Dead letters](pubsub-dead-letters.md) | `Pub/Sub dead letters`, `Pub/Sub push backlog`, `pushed messages moved to Wolverine dead letters` |
| [Durability pass](durability-pass.md) | `durability pass failed`, `Cloud Scheduler job failed` |
| [Rollback](rollback.md) | A deployment breaks the service (standard 06, rule 11) |
| [First deploy](first-deploy.md) | Bringing up a new environment: project, sign-in, secrets, deploy, gateway route, ingress lock |
| [Go live](go-live.md) | Checklist before the first production release (standard 22, rule 17) |

The alert-to-runbook mapping is in `infra/monitoring.tf` (`local.runbook_base_url` plus the file name). When a runbook is
renamed or deleted, change the link in that file in the same pull request. There are no runbooks for a migration job or for
database CPU: the schema is applied by a deploy step ([ADR 0016](../adrs/0016-schema-applied-by-a-deploy-step.md)), which
fails the deploy, and the Cloud SQL instance is the platform's ([ADR 0015](../adrs/0015-shared-platform-postgres-with-iam-auth.md)),
so its capacity is a question for the platform team.

Not written, because the template has no such component: event day or peak load, external dependency outage, projection or
history rebuild, session or access revocation. Add them when the application grows the feature. Standard 22, rule 16 lists them.

Names: Cloud Run service `bcc-template-api-<env>`, Pub/Sub topic `bcc-template-messages-<env>`, Cloud Scheduler job
`template-durability-pass-<env>`, shared Cloud SQL database named by `shared_db.db_name` in `PROPS`. One GCP project per
environment. Logs and metrics are in the project of the environment.

Telemetry: traces are best effort, because the API runs with request-based billing and the batch exporter only has CPU while a
request is in flight ([ADR 0026](../adrs/0026-telemetry-export-through-the-bcc-monitoring-collector.md)). Alerts depend on Cloud
Monitoring metrics and the uptime check, not on traces.
