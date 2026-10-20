---
standard: 22 Observability and Operations
assessed: 2026-10-19
commit: e4b2b86
updated: 2026-10-20 (ServerlessWithOutbox, ADR 0007)
---
# 22 Observability and Operations — assessment

ServiceDefaults, structured Cloud Logging with tests, health endpoints, probes, alert policies with runbooks and the go-live
checklist are in place. Traces are not exported from deployed environments at all, there is no BCC propagator or session
baggage, no span-filtering processor, no dashboard in Terraform, and two runbooks the standard requires are missing although the
template has the components they cover. The messaging change adds five alerts with runbooks (`pubsub-dead-letters.md`,
`durability-pass.md`), so the dead-letter runbook now exists. Three of the new alerts are log-based metrics, two of which match
Wolverine's own log text rather than a constant in the app, with no test tying the filter to the text.

**Score: Weak** · ✅ 8 · ⚠️ 4 · ❌ 5 · N/A 0

Commands run: `dotnet test --project api/tests/BccCode.Template.Web.Tests` (78 passed, includes `GcpStructuredLoggingTests`),
`dotnet test --project api/tests/BccCode.Template.ArchitectureTests` (14 passed, includes `LoggingRulesTests`). For the
messaging update (2026-10-20) the Wolverine 6.51.0 assemblies in the local NuGet cache were searched for the text the log
filters match: "was moved to the error queue", "Durability pass step", "Unable to take the durability pass lock" and
"because another pass holds its lock" are all present in `Wolverine.dll`.

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | Every host calls ServiceDefaults with OTel for ASP.NET Core, HttpClient, Npgsql, Wolverine, Marten, runtime | MUST | ✅ | `api/ServiceDefaults/Extensions.cs:44-57`; called in `Api.Marten/Program.cs:14`, `Api.EfCore/Program.cs:11`, `OidcMock/Program.cs:7`. |
| 2 | OTLP exporter on when endpoint set; Terraform sets service name and environment | MUST | ✅ | `Extensions.cs:59-62`; `infra/cloudrun_api.tf:27-28` sets `OTEL_SERVICE_NAME` and `OTEL_RESOURCE_ATTRIBUTES=deployment.environment.name=…`. |
| 3 | Traces exported in deployed environments (non-batching exporter or collector under `cpu_idle`) | MUST | ❌ | `infra/cloudrun_api.tf:25-28` sets no `OTEL_EXPORTER_OTLP_ENDPOINT`, so no traces leave the service; `infra/main.tf:12,22` uses `cpu_idle = true` in both environments and there is no simple exporter or collector sidecar. `docs/runbooks/README.md:26-27` says traces are "best effort", which overstates it: they are not exported. |
| 4 | Noise filtered at source with a `TraceFilteringProcessor` | MUST | ⚠️ | Only an ASP.NET Core instrumentation filter drops `/health` and `/alive` (`Extensions.cs:52-53`). No processor; proxy housekeeping spans (YARP in Vite mode) are not filtered. `ServerlessWithOutbox` has no Wolverine polling, but the durability pass is a request every minute from Cloud Scheduler (`/internal/durability/pass`), about 1,440 request traces a day per environment, and it is not filtered. |
| 5 | BCC propagator and session id as baggage | MUST | ❌ | No propagator or baggage code in `api/` (grep for `Propagator`, `Baggage` finds nothing). |
| 6 | Sampling configured per environment | SHOULD | ❌ | No sampler configured in `Extensions.cs`; default always-on everywhere. |
| 7 | Structured JSON logs on Cloud Run with trace fields and Error Reporting; formatter tested | MUST | ✅ | `api/ServiceDefaults/GcpLoggingExtensions.cs:39-46` (on `K_SERVICE`), `:52-65` (trace project id, `ErrorReportingLogAugmenter`); tests in `api/tests/BccCode.Template.Web.Tests/GcpStructuredLoggingTests.cs` (7 tests, e.g. `An_entry_links_to_the_current_trace_when_the_project_id_is_known`). |
| 8 | `[LoggerMessage]` log classes per area; architecture test checks registration | SHOULD | ✅ | `Api.*/Infrastructure/SeedLog.cs`; `api/tests/BccCode.Template.ArchitectureTests/LoggingRulesTests.cs:19-35` (`Every_log_class_is_registered_by_AddTemplateLogs`); CA1848 at warning (`.editorconfig:32`). |
| 9 | `/health` (all checks) and `/alive` (self), output cache and timeout | MUST | ✅ | `Extensions.cs:23-26` (live tag, 10 s cache, 5 s timeout), `:69-78`. |
| 10 | Startup probe and liveness on `/alive` | MUST | ✅ | `infra/cloudrun_api.tf:84-93` (TCP startup), `:95-107` (liveness `/alive`). |
| 11 | Outgoing HttpClients use the standard resilience handler | MUST | ✅ | `Extensions.cs:28-32` (`ConfigureHttpClientDefaults` → `AddStandardResilienceHandler`). |
| 12 | Dashboards and alerts in Terraform; every alert links a runbook | MUST | ⚠️ | Ten alert policies in `infra/monitoring.tf`, each with a runbook link: the five earlier ones (`:26`, `:56`, `:107`, `:132`, `:155`), `pubsub_dead_letter` (`:180`), `pubsub_oldest_unacked` (`:203`) and three log-metric alerts (`:232`, `:240`, `:248`). No `google_monitoring_dashboard` resource. |
| 13 | Alert recipients configured for every deployed environment | MUST | ⚠️ | `infra/cloudrun_api.tf:135-138` refuses apply in prod without `alert_emails`; sandbox is allowed to have none and `infra/sandbox.tfvars.example:13` sets `alert_emails = []`. |
| 14 | Log-based metric filters reference code constants, with a test | MUST | ❌ | Three log-based metrics (`infra/monitoring.tf`, `google_logging_metric`). `poison_messages` matches "was moved to the error queue" and `durability_pass_failed` matches three Wolverine 6.51.0 templates; both are library text, not constants in the app, and no test checks that a filter matches. `scheduler_failures` filters on Cloud Scheduler's `severity>=ERROR`, which does not depend on wording. The comment at `monitoring.tf:5-8` states the rule and says the library text must be checked on a Wolverine upgrade. No test shows that a push dead-lettered by the push receiver logs "was moved to the error queue" at all: `MessagingTests.An_unreadable_payload_is_dead_lettered_and_acknowledged` checks the table, not the log. |
| 15 | Dashboards target Grafana; alerting kept portable | SHOULD | ⚠️ | Alerts are Terraform on Cloud Monitoring metrics, including Pub/Sub subscription metrics. Two log-based metrics depend on library log text that can change on a Wolverine upgrade, which is not the "stable templates" the rule asks for. |
| 16 | Runbooks: peak load, rollback, dead letters, dependency outage, projection rebuild, session revocation | MUST | ❌ | `docs/runbooks/` has rollback, dead letters (`pubsub-dead-letters.md`, inspect and republish with `gcloud`), the durability pass and per-alert runbooks. `docs/runbooks/README.md:18-20` skips the rest as "no such component", but two do apply: the app depends on an external OIDC provider (dependency outage) and keeps server-side sessions with back-channel logout (ADR 0004), so session or access revocation is a real operation. Peak load is also not covered. Projection rebuild is skipped because the projection is inline (see standards 10 and 12). |
| 17 | Go-live checklist | MUST | ✅ | `docs/runbooks/go-live.md` covers recipients, probes, secrets, data, rollback. |

## Gaps to close

- **3**: Export traces from Cloud Run. Either set `OTEL_EXPORTER_OTLP_ENDPOINT` to an OpenTelemetry Collector sidecar in
  `infra/cloudrun_api.tf`, or configure a simple (non-batching) span processor with a Cloud Trace / OTLP exporter in
  `api/ServiceDefaults/Extensions.cs` when `K_SERVICE` is set. Correct `docs/runbooks/README.md:26-27` either way.
- **5**: Add the BCC propagator and put the session id in baggage in `api/ServiceDefaults/Extensions.cs` (and the session
  middleware in `api/BccCode.Template.Web/`).
- **14**: Tie each log filter to a template: either log the app's own line (a `[LoggerMessage]` constant) when the push receiver
  dead-letters or the pass fails, and filter on that, or add a test that reads the message templates from the Wolverine
  assembly (or captures the log of `An_unreadable_payload_is_dead_lettered_and_acknowledged`) and asserts each filter string
  is in them. `infra/README.md` already asks for "a test on the .NET side".
- **4**: Drop `/internal/durability/pass` request spans (or sample them) in the same processor, unless the pass did work.
- **16**: Add `docs/runbooks/oidc-provider-outage.md`, `docs/runbooks/session-revocation.md` (how to end one user's or all
  sessions in the server-side store), and `docs/runbooks/peak-load.md`; update the table and the "Not written" paragraph in
  `docs/runbooks/README.md`.
- **4**: Add a `TraceFilteringProcessor` in ServiceDefaults that drops health, YARP proxy and polling spans while keeping DB spans
  inside requests, with a unit test.
- **12**: Add a `google_monitoring_dashboard` for the API (requests, 5xx, latency, DB CPU) in `infra/monitoring.tf`.
- **13**: Require `alert_emails` for sandbox too, or document in an ADR why sandbox alerts go nowhere; extend the precondition
  in `infra/cloudrun_api.tf:135-138`.
- **6** (SHOULD): Configure a parent-based ratio sampler for prod via configuration in `Extensions.cs`.

## Not verifiable in the repo

- **13**: actual `alert_emails` values in the real `*.tfvars` and whether a test alert reaches them (go-live item).
- **12**: runbook links resolve only once `runbook_base_url` points at the real repository (`infra/*.tfvars.example:12`).
- **14**: that the log-based metrics count anything in Cloud Logging: the API's JSON log puts the text in
  `jsonPayload.message`, and whether Wolverine's lines arrive with the template text intact is not checked. Trigger one dead
  letter and one failed pass in sandbox and watch the metrics.
- **3**: whether spans reach Cloud Trace in a deployed environment; check Cloud Trace in the sandbox project after a request.
