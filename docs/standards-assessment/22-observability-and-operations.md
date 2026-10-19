---
standard: 22 Observability and Operations
assessed: 2026-10-19
commit: e4b2b86
---
# 22 Observability and Operations — assessment

ServiceDefaults, structured Cloud Logging with tests, health endpoints, probes, alert policies with runbooks and the go-live
checklist are in place. Traces are not exported from deployed environments at all, there is no BCC propagator or session
baggage, no span-filtering processor, no dashboard in Terraform, and two runbooks the standard requires are missing although the
template has the components they cover.

**Score: Weak** · ✅ 9 · ⚠️ 3 · ❌ 4 · N/A 1

Commands run: `dotnet test --project api/tests/BccCode.Template.Web.Tests` (78 passed, includes `GcpStructuredLoggingTests`),
`dotnet test --project api/tests/BccCode.Template.ArchitectureTests` (14 passed, includes `LoggingRulesTests`).

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | Every host calls ServiceDefaults with OTel for ASP.NET Core, HttpClient, Npgsql, Wolverine, Marten, runtime | MUST | ✅ | `api/ServiceDefaults/Extensions.cs:44-57`; called in `Api.Marten/Program.cs:14`, `Api.EfCore/Program.cs:11`, `OidcMock/Program.cs:7`. |
| 2 | OTLP exporter on when endpoint set; Terraform sets service name and environment | MUST | ✅ | `Extensions.cs:59-62`; `infra/cloudrun_api.tf:15-16` sets `OTEL_SERVICE_NAME` and `OTEL_RESOURCE_ATTRIBUTES=deployment.environment.name=…`. |
| 3 | Traces exported in deployed environments (non-batching exporter or collector under `cpu_idle`) | MUST | ❌ | `infra/cloudrun_api.tf:13-14` sets no `OTEL_EXPORTER_OTLP_ENDPOINT`, so no traces leave the service; `infra/main.tf:12,22` uses `cpu_idle = true` in both environments and there is no simple exporter or collector sidecar. `docs/runbooks/README.md:23-24` says traces are "best effort", which overstates it: they are not exported. |
| 4 | Noise filtered at source with a `TraceFilteringProcessor` | MUST | ⚠️ | Only an ASP.NET Core instrumentation filter drops `/health` and `/alive` (`Extensions.cs:52-53`). No processor; proxy housekeeping spans (YARP in Vite mode) and future Wolverine/Marten polling spans are not filtered. Nothing runs in the background today (`LayerRulesTests.Nothing_runs_in_the_background`), so polling noise does not exist yet. |
| 5 | BCC propagator and session id as baggage | MUST | ❌ | No propagator or baggage code in `api/` (grep for `Propagator`, `Baggage` finds nothing). |
| 6 | Sampling configured per environment | SHOULD | ❌ | No sampler configured in `Extensions.cs`; default always-on everywhere. |
| 7 | Structured JSON logs on Cloud Run with trace fields and Error Reporting; formatter tested | MUST | ✅ | `api/ServiceDefaults/GcpLoggingExtensions.cs:39-46` (on `K_SERVICE`), `:52-65` (trace project id, `ErrorReportingLogAugmenter`); tests in `api/tests/BccCode.Template.Web.Tests/GcpStructuredLoggingTests.cs` (7 tests, e.g. `An_entry_links_to_the_current_trace_when_the_project_id_is_known`). |
| 8 | `[LoggerMessage]` log classes per area; architecture test checks registration | SHOULD | ✅ | `Api.*/Infrastructure/SeedLog.cs`; `api/tests/BccCode.Template.ArchitectureTests/LoggingRulesTests.cs:19-35` (`Every_log_class_is_registered_by_AddTemplateLogs`); CA1848 at warning (`.editorconfig:32`). |
| 9 | `/health` (all checks) and `/alive` (self), output cache and timeout | MUST | ✅ | `Extensions.cs:23-26` (live tag, 10 s cache, 5 s timeout), `:69-78`. |
| 10 | Startup probe and liveness on `/alive` | MUST | ✅ | `infra/cloudrun_api.tf:70-77` (TCP startup), `:82-91` (liveness `/alive`). |
| 11 | Outgoing HttpClients use the standard resilience handler | MUST | ✅ | `Extensions.cs:28-32` (`ConfigureHttpClientDefaults` → `AddStandardResilienceHandler`). |
| 12 | Dashboards and alerts in Terraform; every alert links a runbook | MUST | ⚠️ | Five alert policies in `infra/monitoring.tf`, each with a runbook link (`:24`, `:54`, `:105`, `:130`, `:153`). No `google_monitoring_dashboard` resource. |
| 13 | Alert recipients configured for every deployed environment | MUST | ⚠️ | `infra/cloudrun_api.tf:119-122` refuses apply in prod without `alert_emails`; sandbox is allowed to have none and `infra/sandbox.tfvars.example:13` sets `alert_emails = []`. |
| 14 | Log-based metric filters reference code constants, with a test | MUST | N/A | No log-based metrics; `infra/monitoring.tf:5-6` states alerts are metrics-only. |
| 15 | Dashboards target Grafana; alerting kept portable | SHOULD | ✅ | Alerts are Terraform on Cloud Monitoring metrics, no log-text dependencies (`monitoring.tf:5-6`). |
| 16 | Runbooks: peak load, rollback, dead letters, dependency outage, projection rebuild, session revocation | MUST | ❌ | `docs/runbooks/` has rollback plus per-alert runbooks. `docs/runbooks/README.md:16-18` skips the rest as "no such component", but two do apply: the app depends on an external OIDC provider (dependency outage) and keeps server-side sessions with back-channel logout (ADR 0004), so session or access revocation is a real operation. Peak load is also not covered. Dead letters and projection rebuild are reasonably skipped (no messaging; inline projection only). |
| 17 | Go-live checklist | MUST | ✅ | `docs/runbooks/go-live.md` covers recipients, probes, secrets, data, rollback. |

## Gaps to close

- **3**: Export traces from Cloud Run. Either set `OTEL_EXPORTER_OTLP_ENDPOINT` to an OpenTelemetry Collector sidecar in
  `infra/cloudrun_api.tf`, or configure a simple (non-batching) span processor with a Cloud Trace / OTLP exporter in
  `api/ServiceDefaults/Extensions.cs` when `K_SERVICE` is set. Correct `docs/runbooks/README.md:23-24` either way.
- **5**: Add the BCC propagator and put the session id in baggage in `api/ServiceDefaults/Extensions.cs` (and the session
  middleware in `api/BccCode.Template.Web/`).
- **16**: Add `docs/runbooks/oidc-provider-outage.md`, `docs/runbooks/session-revocation.md` (how to end one user's or all
  sessions in the server-side store), and `docs/runbooks/peak-load.md`; update the table and the "Not written" paragraph in
  `docs/runbooks/README.md`.
- **4**: Add a `TraceFilteringProcessor` in ServiceDefaults that drops health, YARP proxy and polling spans while keeping DB spans
  inside requests, with a unit test.
- **12**: Add a `google_monitoring_dashboard` for the API (requests, 5xx, latency, DB CPU) in `infra/monitoring.tf`.
- **13**: Require `alert_emails` for sandbox too, or document in an ADR why sandbox alerts go nowhere; extend the precondition
  in `infra/cloudrun_api.tf:119-122`.
- **6** (SHOULD): Configure a parent-based ratio sampler for prod via configuration in `Extensions.cs`.

## Not verifiable in the repo

- **13**: actual `alert_emails` values in the real `*.tfvars` and whether a test alert reaches them (go-live item).
- **12**: runbook links resolve only once `runbook_base_url` points at the real repository (`infra/*.tfvars.example:12`).
- **3**: whether spans reach Cloud Trace in a deployed environment; check Cloud Trace in the sandbox project after a request.
