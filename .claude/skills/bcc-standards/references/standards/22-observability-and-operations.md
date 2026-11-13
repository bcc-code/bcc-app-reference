# 22 Observability and Operations

This standard covers telemetry (logs, metrics, traces), health endpoints, alerting and the operational documents an on-call developer needs. The goal is that a problem in production is detected by an alert rather than a user, and that the alert leads straight to a log query and a runbook.

Every service uses a shared ServiceDefaults project that configures OpenTelemetry, health endpoints, resilience and structured logging. What reaches GCP, and what is wired to alerts and probes, follows the rules below.

## Rules

### Telemetry

1. **Every host MUST call the shared ServiceDefaults that configure OpenTelemetry logs, metrics and traces** for ASP.NET Core, HttpClient, Npgsql, Wolverine, Marten (where used) and the .NET runtime.
2. **The OTLP exporter MUST be enabled when `OTEL_EXPORTER_OTLP_ENDPOINT` is set**, and Terraform MUST set `OTEL_SERVICE_NAME` and the resource attribute `deployment.environment.name` (through `OTEL_RESOURCE_ATTRIBUTES`) per service.
3. **Traces MUST be exported in deployed environments.** On Cloud Run with request-based billing (`cpu_idle = true`, serverless style) the batch exporter does not get CPU between requests, so spans are lost. A service in that style MUST use a solution that works without background CPU, for example a non-batching (simple) exporter for the span volume we have, or an OpenTelemetry Collector sidecar in the Cloud Run service. A service that does neither MUST record the exception in an ADR. Services with always-on CPU use the normal batch exporter.
4. **Noise MUST be filtered at the source with a trace-filtering processor** (a custom OpenTelemetry `BaseProcessor<Activity>` in ServiceDefaults, named `TraceFilteringProcessor` in the example below) that drops health checks, Wolverine/Marten polling and proxy housekeeping spans, without hiding real database spans inside requests.
5. **Traces MUST be correlated across BCC systems with the BCC propagator.** Where a session id is carried as baggage it MUST be the identity provider's session id (`sid`), never the application's cookie or BFF session key, and baggage MUST be stripped on calls to non-BCC hosts. This lets a trace survive the load balancer and be joined with sign-on and other services, without leaking an identifier to third parties.
6. **Sampling SHOULD fit the traffic.** Recording every trace is fine locally, in sandbox, and in production for a service with low traffic, where the span count and its cost stay small; check them after go-live. A service with high or bursty traffic SHOULD use a parent-based ratio sampler in production, so cost and volume stay predictable.

### Logging

7. **On Cloud Run, logs MUST be structured JSON for Cloud Logging, with trace correlation fields and Error Reporting tagging for exceptions**, enabled when `K_SERVICE` is set. The formatter MUST have tests, since a broken format silently disconnects logs from traces.
8. **Log messages SHOULD be source-generated `[LoggerMessage]` methods in one log class per area.** Fixed templates make log-based metrics and queries stable. Logging rules for personal data are in [18 Personal Data and GDPR](18-personal-data-and-gdpr.md).

### Health and resilience

9. **Every service MUST expose `/health` (all checks, including the database and other dependencies) and `/alive` (self only), with output caching and a request timeout.** Without the cache, a probe storm becomes a database load problem. `/health` with dependency details MUST NOT be reachable through the public gateway: either do not route it, or return status only on the public path (see [26 Gateway and Frontend Hosting](26-gateway-and-frontend-hosting.md)).
10. **Cloud Run probes MUST be wired: startup probe (TCP or `/health`) and liveness on `/alive`.** Liveness must never include the database; a Cloud SQL outage would otherwise restart every instance at once.
11. **Outgoing `HttpClient`s MUST use the standard resilience handler** (`AddStandardResilienceHandler`) from ServiceDefaults.

### Alerts and dashboards

12. **Dashboards and alert policies MUST be defined in Terraform, and every alert's documentation MUST link a runbook.** Alerts clicked in the console drift and get lost on re-creation.
13. **Alert recipients MUST be configured for every deployed environment before go-live.** An alert policy without a notification channel alerts nobody.
14. **Log-based metric filters MUST reference message templates defined as constants in code, and a test MUST check that each filter matches its template.** Rewording a log message otherwise turns its alert off without any error.
15. **Alerting and dashboards SHOULD be written so they can move between Cloud Monitoring and Grafana (Terraform, log-based metrics on stable templates).** The platform chosen for dashboards is recorded in an ADR, not in this standard.

### Operational documents

16. **Each service MUST have runbooks in `docs/runbooks/`** for at least: rollback, external dependency outage, and session or access revocation. Where they apply, also: peak load (for services with predictable peaks), dead letters (for services that use queues or subscriptions) and projection rebuild (event-sourced modules). Alerts link to these.
17. **Each service MUST have a go-live checklist** (`docs/internal/go-live.md` or `docs/runbooks/go-live.md`) that covers alert recipients, probes, secrets, data migration and rollback before the first production release.

## Examples

### ServiceDefaults with filtering and propagation (rules 1, 4, 5)

The ServiceDefaults project holds the OpenTelemetry setup (`Extensions.cs`) and the BCC propagator (`BccPropagator.cs`). The trace processor suppresses Wolverine housekeeping on its own and never its parent:

```csharp
internal sealed class TraceFilteringProcessor : BaseProcessor<Activity>
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public override void OnEnd(Activity activity)
    {
        // Wolverine's own internal bookkeeping commands ... are suppressed on their own - never their parent.
        if (IsWolverineInternalDbCommand(activity)) { Suppress(activity); return; }
        ...
```

Cover the processor with unit tests (`TraceFilteringProcessorTests`).

### Health endpoints with cache and timeout (rule 9)

```csharp
var healthChecks = app.MapGroup("");
healthChecks.CacheOutput("HealthChecks").WithRequestTimeout("HealthChecks");
healthChecks.MapHealthChecks("/health");
healthChecks.MapHealthChecks("/alive", new() { Predicate = static r => r.Tags.Contains("live") });
```

### Liveness without the database (rule 10)

```hcl
# Liveness must NOT include the DB check: a Cloud SQL outage would otherwise fail liveness
# on every instance simultaneously and trigger a restart storm. /alive is self-check only.
liveness_probe {
```

### Structured logging with tests (rule 7)

Put the Cloud Logging formatter in `GcpLoggingExtensions.cs` in ServiceDefaults, and test it (for example `GcpStructuredLogFormatterTests`) so that a format change that disconnects logs from traces fails the build.

### Log classes registered (rule 8)

```csharp
var registered = new ServiceCollection().AddMyAppLogs().Select(d => d.ServiceType).ToHashSet();
var logClasses = typeof(MyAppAssemblyMarker).Assembly.GetTypes()
    .Where(t => t.GetMethods(AllMembers).Any(m => m.GetCustomAttribute<LoggerMessageAttribute>() is not null));
logClasses.Where(t => !registered.Contains(t)).Select(t => t.FullName).Should().BeEmpty();
```

### Alerts as code with runbook links (rules 12, 13)

```hcl
resource "google_monitoring_alert_policy" "dead_letter_backlog" {
  display_name          = "myapp ${local.env}: dead-lettered Pub/Sub messages"
  notification_channels = local.alert_channels
  documentation {
    mime_type = "text/markdown"
    content   = "A message failed 5 deliveries to the processor and sits in myapp-dead-letter-inspect. See docs/runbooks/dead-letters.md."
  }
```

Recipients come from `var.alert_emails`, which is empty until configured. Set it for every deployed environment before go-live.

### Filter tests (rule 14)

A `MonitoringFilterTests` class ties each log-based metric filter to the template the code logs through. Rewording a message would turn its alert off without any error, so the test fails instead.

### Runbooks (rules 16, 17)

A `docs/runbooks/` folder, for a service that uses queues and has peak periods, with files such as `peak-load.md`, `rollback.md`, `dead-letters.md`, `external-api-outage.md`, `projection-rebuild.md`, `sessions-and-admin-rights.md` and `prod-setup.md`. The go-live checklist lives in `docs/internal/go-live.md`.

### Serverless trace constraint (rule 3)

A service on request-based billing states the constraint next to its telemetry setup: "Traces are best-effort under request-based billing: the batch exporter only runs while an instance has CPU. Alerting never depends on OTel, only on structured log lines." Such a service either exports with a simple exporter or a collector sidecar, or documents the exception with an ADR.

## Related

- [03 Infrastructure as Code](03-infrastructure-as-code.md) — Terraform for alerts and dashboards
- [04 GCP Runtime](04-gcp-runtime.md) — Cloud Run probes and billing mode
- [06 Deployment and Release](06-deployment-and-release.md) — rollback runbook and smoke test
- [13 Messaging and Integration](13-messaging-and-integration.md) — dead letters and polling
- [18 Personal Data and GDPR](18-personal-data-and-gdpr.md) — what not to log
- [23 Documentation and Decisions](23-documentation-and-decisions.md) — runbooks and ADRs
