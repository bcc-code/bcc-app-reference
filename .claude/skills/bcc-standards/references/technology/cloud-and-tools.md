# Cloud and Tools

Shared tooling used across teams and services: .NET Aspire, Terraform, Google Cloud Platform and Cloud Run, Pub/Sub with Cloud Events, Twilio and SendGrid, OpenTelemetry, Grafana and K6. This page also covers the environments we run and the rule to develop over HTTPS locally. The local AppHost setup (.NET Aspire) is described in [02 Local Development](../standards/02-local-development.md).

## .NET Aspire

.NET Aspire is an opinionated stack for building and running distributed .NET applications locally. It handles service orchestration, telemetry configuration and developer setup.

- Use Aspire for local development orchestration in distributed systems.
- Standardize telemetry (OpenTelemetry) and configuration across services through Aspire.

Example: a solution uses Aspire to start the API, database and message broker locally with shared telemetry. One `aspire run` starts everything.

Related: [02 Local Development](../standards/02-local-development.md) for the AppHost rules, [OpenTelemetry](#opentelemetry), [Grafana](#grafana).

## Terraform

Terraform is our Infrastructure as Code tool. All cloud infrastructure is defined in Terraform and applied through CI/CD, with no manual changes through the console.

- Version infrastructure alongside application code.
- Prefer reusable modules for common patterns (service + IAM + database = a module).
- Store state in a remote backend (GCS) with locking, not locally.
- Apply changes through CI/CD pipelines, not ad-hoc from local machines.

Example: a new service is provisioned using a Terraform module that creates the Cloud Run service, Cloud SQL database, IAM bindings and Pub/Sub topics in one apply.

Related: [03 Infrastructure as Code](../standards/03-infrastructure-as-code.md), [Google Cloud Platform](#google-cloud-platform-gcp), [Cloud Run](#cloud-run), [Environments](#environments).

## Google Cloud Platform (GCP)

GCP is our primary cloud provider. Default to GCP services for hosting and managed infrastructure.

- Use GCP-managed services (Cloud Run, Cloud SQL, Pub/Sub, etc.) instead of self-hosted equivalents.
- Do not introduce other cloud providers without a clear business requirement. Multi-cloud sounds good in theory but adds significant operational overhead.
- Standardize on GCP to share expertise and tooling across teams.

Related: [04 GCP Runtime](../standards/04-gcp-runtime.md), [Cloud Run](#cloud-run), [Terraform](#terraform).

## Cloud Run

Cloud Run is GCP's managed platform for running stateless containers. It scales up and down automatically based on traffic, including to zero.

- Use Cloud Run for all stateless services.
- Keep startup time short. Cold starts happen when scaling from zero, and slow startup means slow first requests.
- Services must be stateless. Do not store session data in memory or on the local filesystem.
- Use GKE instead if you need custom orchestration or truly stateful workloads.

Related: [04 GCP Runtime](../standards/04-gcp-runtime.md) for the Cloud Run configuration we require (ingress, sidecars, probes, images), [OpenTelemetry](#opentelemetry).

## Google Pub/Sub and Cloud Events

Google Pub/Sub is our message bus for async integration between services. Cloud Events is the standardized payload format we use for events.

- Use Pub/Sub for event-driven integration between services.
- Format event payloads using the Cloud Events spec for consistency and interoperability. The message type travels in the CloudEvents `type` or a Pub/Sub `message-type` attribute (see [13 Messaging and Integration](../standards/13-messaging-and-integration.md)).
- Version event schemas explicitly. Consumers depend on them and breaking changes must be coordinated.
- Use direct HTTP calls for synchronous workflows where a message bus adds unnecessary indirection.
- Do not use ad-hoc event formats per service. Standardize on one envelope.

Example: when an order is created, an `OrderCreated` Cloud Event is published to a Pub/Sub topic. Multiple services subscribe: one sends a confirmation email, another updates analytics.

Related: [13 Messaging and Integration](../standards/13-messaging-and-integration.md), [04 GCP Runtime](../standards/04-gcp-runtime.md) for push subscription and dead-letter rules, [Google Cloud Platform](#google-cloud-platform-gcp).

## Twilio and SendGrid

Twilio handles SMS and SendGrid handles email. Both are managed third-party APIs that handle delivery, monitoring and compliance so we do not have to build that infrastructure ourselves.

- If the platform's shared notification service covers the need, send through it; otherwise use SendGrid/Twilio directly.
- Use Twilio for SMS (OTPs, notifications).
- Use SendGrid for transactional and marketing email.
- Centralize templates and delivery settings. Do not scatter email/SMS logic across services.
- Store credentials in the secret store, not in environment files.

Example: a password reset sends via SendGrid using a managed template. OTP messages go via Twilio.

Related: [security principles](../principles/security.md) for secret management, [18 Personal Data and GDPR](../standards/18-personal-data-and-gdpr.md) for audit logging.

## OpenTelemetry

OpenTelemetry is the vendor-neutral standard for collecting and exporting logs, metrics and traces. Instrument all services consistently with it.

- Instrument every service with OpenTelemetry from the start. It is much harder to add retroactively.
- Export to Grafana or another monitoring backend.
- Use consistent resource attributes across services so you can correlate traces across service boundaries.
- Make sure every request carries a trace ID that propagates through the entire call chain.

Example: a user request triggers a trace that spans the BFF, backend API and database query. The full trace is visible in Grafana.

Related: [22 Observability and Operations](../standards/22-observability-and-operations.md), [Grafana](#grafana).

## Grafana

Grafana is the target platform for dashboards of system health and performance. Cloud Monitoring is accepted where the shared Grafana setup is not yet used; [22 Observability and Operations](../standards/22-observability-and-operations.md) has the current rule.

- Do not create fragmented dashboards in different tools for different services.
- Define dashboards and alert policies in Terraform, and link a runbook from every alert.
- Standardize dashboard templates across services so on-call engineers see a consistent view regardless of which service they are looking at.
- Set up alerts for SLO breaches: latency, error rate, availability.

Related: [22 Observability and Operations](../standards/22-observability-and-operations.md), [OpenTelemetry](#opentelemetry), [K6](#k6).

## K6

K6 is our load-testing tool. Write load tests as code, version them with the codebase, and run them before major releases and events.

- Keep K6 scripts in the repository alongside the code they test.
- Run load tests as part of the release process, especially before significant traffic changes.
- Version scripts and update them as APIs evolve.
- For legacy scenarios, JMeter is an alternative.

Example: a K6 script simulates 200 concurrent users hitting a checkout endpoint for 10 minutes. Results feed into Grafana dashboards.

Related: [19 Testing Strategy](../standards/19-testing-strategy.md), [16 HTTP Security Hardening](../standards/16-http-security-hardening.md) for rate limiting.

## HTTPS in Local Development

Always run local development over HTTPS. The closer local mirrors production, the fewer surprises ship.

- Serve the API and frontend over `https://` locally, using the ASP.NET Core development certificate (`dotnet dev-certs https --trust`).
- Do not disable certificate validation or add HTTP-only fallbacks to make things "just work". Fix the certificate instead.
- Keep the same cookie flags locally as in production (`Secure`, `SameSite`). These only behave correctly over HTTPS.

Why it matters: several behaviours only appear under HTTPS: `Secure` cookies, `SameSite=None`, secure-context browser APIs and mixed-content blocking. HSTS is not sent in local development, because it would pin HTTPS for the local domain for a year. Developing over plain HTTP hides these until staging or production, where they are more expensive to diagnose. Dev/prod parity keeps the feedback loop honest.

Related: [02 Local Development](../standards/02-local-development.md) (rules 14 and 15 set the `https://<app>.dev.localhost` address and the single origin), [14 Authentication and Sessions](../standards/14-authentication-and-sessions.md), [16 HTTP Security Hardening](../standards/16-http-security-hardening.md) for HSTS, [Environments](#environments).

## Environments

We run up to four environments, each with a clear purpose:

- **Dev**: shared experimental space. No other system should take a dependency on it. It takes a dependency on Sandbox.
- **Sandbox**: the main testing environment for all systems.
- **Staging** (where one exists): a mirror of production for final QA before releasing.
- **Prod**: production.

Guidance:

- Promote builds through the chain: Sandbox, Staging where it exists, Prod. Where there is no staging, sandbox runs the same image, migrations and Terraform as prod ([06 Deployment and Release](../standards/06-deployment-and-release.md)).
- Never create dependencies on Dev or Sandbox from stable environments.
- Short-lived preview environments are fine for feature validation, as long as they are cleaned up.
- Environment configuration should be managed in code (Terraform), not applied manually.

Related: [Terraform](#terraform), [Cloud Run](#cloud-run), [06 Deployment and Release](../standards/06-deployment-and-release.md), [07 Preview Environments](../standards/07-preview-environments.md).
