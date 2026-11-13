# 0026. Telemetry export through the BCC monitoring collector, best effort under request-based billing

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADR 0033 (Proposed there). Records the exceptions standards 22.3 and 22.4 ask for.

## Context

ServiceDefaults traces and measures the API, but in GCP nothing sends the spans and metrics anywhere unless `OTEL_EXPORTER_OTLP_ENDPOINT` is set. The other BCC services send OTLP to the shared collector in bcc-monitoring. The endpoint and the key are org-level GitHub values (`OTEL_EXPORTER_OTLP_ENDPOINT_DEV/_PROD` and the secrets `OTEL_EXPORTER_OTLP_AUTH_KEY_DEV/_PROD`), which this repository can read. The collector routes by the resource attribute `cloud.account.id`: traces go to Cloud Trace in the service's own project and in the central monitoring project, logs to the service's own Cloud Logging, metrics to Managed Prometheus in the central project, where Grafana alerts on the HTTP span metrics.

The API runs with request-based billing and scales to zero ([0013](0013-cloud-run-behind-the-platform-gateway.md), [0024](0024-wolverine-serverless-with-outbox-and-pubsub-push.md)). The batch exporter sends from a background thread, which gets no CPU between requests. Standard 22.3 says such a service must use a simple exporter or a collector sidecar, or record the exception in an ADR.

## Decision

1. **Export traces and metrics to the BCC collector with the batch exporter, and treat traces as best effort.** Spans waiting in the batch go out the next time the instance has CPU, which is the next request; the durability pass every minute is one. The host flushes on shutdown. Metrics are cumulative, so a late export delays them and loses nothing. Each Cloud Run instance sets its own `service.instance.id`. No alert of ours depends on spans: the alerts in `infra/monitoring.tf` watch Cloud Monitoring metrics, the uptime check, a Pub/Sub subscription and a log-based metric.
2. **The collector's key goes from the org secret through Terraform to Secret Manager, write-only.** `deploy.yml` and `infra.yml` pass the endpoint and the key as Terraform variables for the environment. Terraform writes the header `Authorization=AuthKey <key>` with `secret_data_wo`, so the value is not in the state (standard 03.18), and takes the version number from a hash of the key, so a rotated key gets a new version on the next apply. If an org value is missing the workflows pass it empty: without an endpoint the app exports nothing. The OTLP header is a secret: it is not logged or copied (it is in the [data inventory](../data-protection/data-inventory.md)).
3. **On Cloud Run, logs stay on stdout as JSON only.** An OTLP copy would come back from the collector into our own Cloud Logging, so every line would be stored twice. Locally, logs go over OTLP to the Aspire dashboard.
4. **Every trace is recorded**, except the children of a dropped local span (`/health`, `/alive`). Google's front end marks most requests not sampled, and the default parent-based sampler would follow that and drop them. Traffic is low, so the span count stays small (22.6).
5. **Propagation follows standard 22.5.** The session id in baggage is the sign-in session's `sid`, set only for signed-in requests, and baggage goes to `*.bcc.no` only. A caller's baggage is never read or passed on.
6. **No trace-filtering processor** (22.4): health checks are dropped by the instrumentation filter and the sampler, and nothing polls.

## Consequences

| | |
|---|---|
| Positive | Traces of sandbox requests in Cloud Trace, in our project and in the central one, and span metrics in Grafana. No extra container and no change to the billing model. The collector key never enters state. |
| Negative | Spans can be lost when an instance stops without a flush, and are late when it idles. The central project has no copy of our error logs. The `sid` of signed-in users is in the traces of both projects. |
| Follow-up | Measure the loss in sandbox (spans against Cloud Run's request count over a day) before prod. Alerts and dashboards beyond the ones in `infra/monitoring.tf` (standard 22.12 to 22.15). |

## Alternatives considered

- **Simple (non-batching) exporter** — a gRPC call to the collector on the critical path of every request; the request span still ends after the response is sent, when the CPU is throttled.
- **Collector sidecar** — another container to pin and keep up to date, throttled with the rest of the instance.
- **Instance-based billing (`cpu_idle = false`)** — reverses the cost decision for telemetry alone.
- **Key created by hand in Secret Manager** — nobody on the team can read the org secret, and every rotation would need someone to copy it.
- **Logs over OTLP too** — every line stored twice in our project.
