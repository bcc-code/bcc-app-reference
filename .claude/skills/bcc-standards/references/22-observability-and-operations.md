# 22 Observability and Operations

This standard covers telemetry (logs, metrics, traces), health endpoints, alerting and the operational documents an on-call developer needs. The goal is that a problem in production is detected by an alert rather than a user, and that the alert leads straight to a log query and a runbook.

## Rules

### Telemetry

1. **Every host MUST call the shared ServiceDefaults that configure OpenTelemetry logs, metrics and traces** for ASP.NET Core, HttpClient, Npgsql, Wolverine, Marten (where used) and the .NET runtime.
2. **The OTLP exporter MUST be enabled when `OTEL_EXPORTER_OTLP_ENDPOINT` is set**, and Terraform MUST set `OTEL_SERVICE_NAME` and `deployment.environment.name` per service.
3. **Traces MUST be exported in deployed environments.** On Cloud Run with request-based billing (`cpu_idle = true`, serverless style) the batch exporter does not get CPU between requests, so spans are lost. A service in that style MUST use a solution that works without background CPU, for example a non-batching (simple) exporter for the span volume we have, or an OpenTelemetry Collector sidecar in the Cloud Run service. Services with always-on CPU use the normal batch exporter.
4. **Noise MUST be filtered at the source with a `TraceFilteringProcessor`** that drops health checks, Wolverine/Marten polling and proxy housekeeping spans, without hiding real database spans inside requests.
5. **Traces MUST be correlated across BCC systems with the BCC propagator and the session id as baggage**, so a trace survives the load balancer and can be joined with sign-on and other services.
6. **Sampling SHOULD be configured per environment.** `AlwaysOnSampler` is fine locally and in sandbox; production SHOULD use a parent-based ratio sampler so cost and volume stay predictable.

### Logging

7. **On Cloud Run, logs MUST be structured JSON for Cloud Logging, with trace correlation fields and Error Reporting tagging for exceptions**, enabled when `K_SERVICE` is set. The formatter MUST have tests, since a broken format silently disconnects logs from traces.
8. **Log messages SHOULD be source-generated `[LoggerMessage]` methods in one log class per area, and an architecture test SHOULD check that every log class is registered.** Fixed templates make log-based metrics and queries stable. Logging rules for personal data are in [18 Privacy and GDPR](18-privacy-and-gdpr.md) and Don't Log Personal Identifiers.

### Health and resilience

9. **Every service MUST expose `/health` (all checks, including the database and other dependencies) and `/alive` (self only), with output caching and a request timeout.** Without the cache, a probe storm becomes a database load problem.
10. **Cloud Run probes MUST be wired: startup probe (TCP or `/health`) and liveness on `/alive`.** Liveness must never include the database; a Cloud SQL outage would otherwise restart every instance at once.
11. **Outgoing `HttpClient`s MUST use the standard resilience handler** (`AddStandardResilienceHandler`) from ServiceDefaults.

### Alerts and dashboards

12. **Dashboards and alert policies MUST be defined in Terraform, and every alert's documentation MUST link a runbook.** Alerts clicked in the console drift and get lost on re-creation.
13. **Alert recipients MUST be configured for every deployed environment before go-live.** An alert policy without a notification channel alerts nobody.
14. **Log-based metric filters MUST reference message templates defined as constants in code, and a test MUST check that each filter matches its template.** Rewording a log message otherwise turns its alert off without any error.
15. **The target platform for dashboards is Grafana** (Grafana). The repos use Cloud Monitoring today; that is accepted until a decision on the shared Grafana setup is made. New alerting SHOULD be written so it can move (Terraform, log-based metrics on stable templates).

### Operational documents

16. **Each service MUST have runbooks in `docs/runbooks/`** for at least: event day or peak load, rollback, dead letters, external dependency outage, projection rebuild (event-sourced modules) and session or access revocation. Alerts link to these.
17. **Each service MUST have a go-live checklist** (`docs/internal/go-live.md` or `docs/runbooks/go-live.md`) that covers alert recipients, probes, secrets, data migration and rollback before the first production release.

## Example

`api/ServiceDefaults/` (OpenTelemetry, health endpoints, resilience handler, structured logging) and `docs/runbooks/`. Source repos for alerts as code: vote-rewrite `infra/monitoring.tf` and `MonitoringFilterTests`.
