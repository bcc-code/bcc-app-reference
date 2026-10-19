# 04 GCP Runtime

How our services run on Google Cloud: Cloud Run configuration, database connectivity, probes, container images, schema changes, secrets, key storage, static assets, messaging and logging. These settings decide whether an outage in one dependency takes the whole service down, whether a rollback works, and whether the service is reachable only through the platform gateway. The Terraform conventions are in [03 Infrastructure as Code](03-infrastructure-as-code.md). See Cloud Run and Google Cloud Platform (GCP).

## Rules

### Cloud Run service

1. **Services MUST be `google_cloud_run_v2_service` (and v2 jobs / worker pools).** The v2 resources expose sidecars, `depends_on` between containers and per-container probes, which the rules below rely on.
2. **Ingress MUST be `INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER`, so the service is reachable only through the BCC platform gateway.** With `INGRESS_TRAFFIC_ALL` plus an `allUsers` invoker, the `*.run.app` URL bypasses the gateway and everything it enforces. If `allUsers` remains as invoker because the gateway does not send an identity token, the comment MUST say that ingress is what restricts access.
3. **Non-production environments SHOULD scale to zero (`min_instance_count = 0`, `cpu_idle = true`); all services SHOULD set `startup_cpu_boost = true`.** Production min instances and CPU allocation follow the billing model (rule 19).

### Database connectivity

4. **The app MUST reach Cloud SQL through a Cloud SQL Auth Proxy sidecar running with `--auto-iam-authn`, and the app container MUST `depends_on` the sidecar.** No passwords, and the app starts only when the proxy listens.
5. **The sidecar MUST have its own TCP startup probe on 5432 with a short period (1 s).** Cloud Run waits for a dependency only through that dependency's startup probe; a 10 s period adds up to 10 s to every cold start.

### Probes

6. **The app container MUST have an explicit startup probe (TCP on the app port).** The block must be written out: `startup_probe` is Optional+Computed in the provider, so deleting it leaves whatever probe the live service has.
7. **The app container MUST have a liveness probe on `/alive`, and `/alive` MUST NOT check the database or other dependencies.** A dependency outage would otherwise fail liveness on every instance at once and cause a restart storm. Dependency checks belong on `/health`, used for readiness and monitoring ([22 Observability and Operations](22-observability-and-operations.md)).

### Container image

8. **Images MUST be built with `dotnet publish /t:PublishContainer` on a chiseled base image (`ContainerFamily=noble-chiseled` or `noble-chiseled-extra` when ICU is needed); no Dockerfile for .NET services.** Chiseled images have no shell or package manager, which shrinks the attack surface, and the SDK build is reproducible without a Dockerfile to maintain.

### Schema and migrations

9. **Schema changes and migrations MUST run as a Cloud Run job or a deploy step that completes before the new revision receives traffic, and MUST NOT run on app start in production.** Several instances starting at once race on the migration, a failed migration leaves instances crash-looping, and the app's runtime identity would need DDL rights. Variants: orginn runs `orginn-database-manager-<env>` and a Wolverine schema job with `gcloud run jobs execute --wait`; vote-rewrite runs the image's `db-apply` command through the Auth Proxy in the deploy job. Details in [12 Data Access and Migrations](12-data-access-and-migrations.md).

### Secrets and keys

10. **Secrets MUST be injected from Secret Manager with `value_source.secret_key_ref`, and each runtime service account MUST get `roles/secretmanager.secretAccessor` on each secret it reads, not project-wide.** See Secret Management.
11. **ASP.NET Core Data Protection keys MUST be persisted in Postgres and encrypted with a Cloud KMS key (`KmsXmlEncryptor`); startup MUST fail outside Development/Test when the KMS key name is missing.** Keys shared across instances keep cookies and antiforgery tokens valid on every instance; KMS keeps a database dump from yielding the keys. The KMS key has 90-day rotation and `prevent_destroy`. See ASP.NET Core Data Protection.

### Frontend assets

12. **Content-hashed frontend assets MUST be uploaded additively (`gcloud storage cp --no-clobber` or `rsync` without delete) with `Cache-Control: public, max-age=31536000, immutable`, before the new revision rolls out.** Old bundles stay, so a Cloud Run traffic rollback still finds the assets its `index.html` references.
13. **`index.html` MUST be served by the API with `no-store` (or `no-cache`), and any non-hashed file that is overwritten (a widget loader) MUST have a short `max-age` and a documented restore path.** See Frontend Hosting Strategy.

### Messaging and background work

14. **Pub/Sub push subscriptions MUST send an OIDC token from a dedicated push service account with a per-caller audience, and the receiver MUST validate audience and service account email.** See Zero Trust and [13 Messaging and Integration](13-messaging-and-integration.md).
15. **Every push subscription MUST have a dead-letter topic (`max_delivery_attempts` around 5), a pull "inspect" subscription on that topic, and an ops UI or runbook to republish or discard dead letters.** Without the inspect subscription, dead letters are only a metric.
16. **Cloud Tasks MUST use deterministic task names derived from the business key, so a retried enqueue does not create a second task.**
17. **Background work runs either in a Cloud Run worker pool (always-on, instance billed) or as serverless push (Pub/Sub, Cloud Tasks, Cloud Scheduler calling HTTP endpoints, CPU only during requests).** This is an architecture choice; both are acceptable. What is not acceptable is relying on background threads in a request-billed service with `cpu_idle = true`, where CPU is throttled between requests.

### Logging

18. **When `K_SERVICE` is set, the app MUST log structured JSON with Google's console formatter, with the trace id qualified by project id, and an Error Reporting augmenter on error entries.** One JSON line per event keeps multi-line messages and stack traces together and links logs to traces. Local runs keep the readable formatter.

### Billing and cost

19. **Each environment's billing model (instance-based vs request-based, min instances, `cpu_idle`) MUST be documented with the reason, next to the Terraform that sets it or in an ADR.** Billing model and CPU allocation move together; changing one without the other either costs money or stalls background work.

## Example

`infra/` (Cloud Run, probes, proxy sidecar) and `api/ServiceDefaults/` (health endpoints, structured logging). Source repos: orginn `infra/cloudrun_api.tf` for probes, vote-rewrite `infra/cloudrun_api.tf` for ingress.
