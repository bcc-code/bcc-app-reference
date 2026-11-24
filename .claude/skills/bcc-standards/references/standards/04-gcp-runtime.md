# 04 GCP Runtime

This standard covers how our services run on Google Cloud: Cloud Run configuration, database connectivity, probes, container images, schema changes, secrets, key storage, static assets, messaging and logging.

These settings decide whether an outage in one dependency takes the whole service down, whether a rollback works, and whether the service is reachable only through the platform gateway. The Terraform conventions are in [03 Infrastructure as Code](03-infrastructure-as-code.md). Background on the platform is in [cloud and tools](../technology/cloud-and-tools.md#cloud-run).

## Rules

### Cloud Run service

1. **Services MUST be `google_cloud_run_v2_service` (and v2 jobs / worker pools).** The v2 resources expose sidecars, `depends_on` between containers and per-container probes, which the rules below rely on.
2. **Ingress MUST be `INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER`, so the service is reachable only through the BCC platform gateway.** With `INGRESS_TRAFFIC_ALL` plus an `allUsers` invoker, the `*.run.app` URL bypasses the gateway and everything it enforces. If `allUsers` remains as invoker because the gateway does not send an identity token, the comment MUST say that ingress is what restricts access. Preview services are a documented exception: they use the public `run.app` URL, are protected by the app's (mock) login and MUST NOT hold real data ([07 Preview Environments](07-preview-environments.md)).
3. **Non-production environments SHOULD scale to zero (`min_instance_count = 0`, `cpu_idle = true`); all services SHOULD set `startup_cpu_boost = true`.** Production min instances and CPU allocation follow the billing model (rule 19).

### Database connectivity

4. **When the service uses Cloud SQL, it MUST reach the database through a Cloud SQL Auth Proxy sidecar running with `--auto-iam-authn`, and the app container MUST `depends_on` the sidecar.** No passwords, and the app starts only when the proxy listens.
5. **When a sidecar is used, it MUST have its own TCP startup probe on 5432 with a short period (1 s).** Cloud Run waits for a dependency only through that dependency's startup probe; a 10 s period adds up to 10 s to every cold start.

### Probes

6. **The app container MUST have an explicit startup probe (TCP on the app port).** The block must be written out: `startup_probe` is Optional+Computed in the provider, so deleting it leaves whatever probe the live service has.
7. **The app container MUST have a liveness probe on `/alive`, and `/alive` MUST NOT check the database or other dependencies.** A dependency outage would otherwise fail liveness on every instance at once and cause a restart storm. Dependency checks belong on `/health`, used for monitoring and uptime checks; Cloud Run has no separate readiness probe, and the startup probe is rule 6 ([22 Observability and Operations](22-observability-and-operations.md)).

### Container image

8. **Images MUST be built with `dotnet publish /t:PublishContainer` on a chiseled base image (`ContainerFamily=<distro>-chiseled` or `<distro>-chiseled-extra` when ICU is needed: `noble-*` for .NET 10, `resolute-*` for .NET 11, whose images are built on Ubuntu 26.04 only); .NET services SHOULD NOT use a Dockerfile.** Chiseled images have no shell or package manager, which shrinks the attack surface, and the SDK build is reproducible without a Dockerfile to maintain. A Dockerfile is allowed with a documented reason, for example when the service needs OS packages that the chiseled images do not offer; base it on a pinned minimal image.

### Schema and migrations

9. **Schema changes and migrations MUST run as a Cloud Run job or a deploy step that completes before the new revision receives traffic, and MUST NOT run on app start in production.** Several instances starting at once race on the migration, a failed migration leaves instances crash-looping, and the app's runtime identity would need DDL rights. Run the step so that a failure fails the deploy (for example `gcloud run jobs execute --wait`). Setup variants are in the examples. Details in [12 Data Access and Migrations](12-data-access-and-migrations.md).

### Secrets and keys

10. **Secrets MUST be injected from Secret Manager with `value_source.secret_key_ref`, and each runtime service account MUST get `roles/secretmanager.secretAccessor` on each secret it reads, not project-wide.** Secrets then never appear in the Terraform config, the image or the revision's plain environment variables. See [security principles](../principles/security.md).
11. **ASP.NET Core Data Protection keys MUST be persisted in shared durable storage (Postgres or a GCS bucket) and encrypted with a Cloud KMS key, for example with Google's `Google.Cloud.AspNetCore.DataProtection.Kms` package (`ProtectKeysWithGoogleKms`); startup MUST fail outside Development/Test when the KMS key name is missing.** Keys shared across instances keep cookies and session tickets valid on every instance; KMS keeps a database dump or bucket copy from yielding the keys. The KMS key has 90-day rotation and `prevent_destroy`. Storing the keys unencrypted does not meet this rule. If an in-house `IXmlEncryptor` is used instead of the Google package, the code and docs MUST say it is in-house.

### Frontend assets

12. **Content-hashed frontend assets MUST be uploaded additively (`gcloud storage cp --no-clobber` or `rsync` without delete) with `Cache-Control: public, max-age=31536000, immutable`, before the new revision rolls out.** Old bundles stay, so a Cloud Run traffic rollback still finds the assets its `index.html` references.
13. **`index.html` MUST be served by the API with `no-store` (or `no-cache`), and any non-hashed file that is overwritten (a loader script) MUST have a short `max-age` and a documented restore path.** See [26 Gateway and Frontend Hosting](26-gateway-and-frontend-hosting.md).

### Messaging and background work

14. **Pub/Sub push subscriptions MUST send an OIDC token from a dedicated push service account, and each receiver MUST verify the token's audience (its own URL) and the calling service account (`email` claim).** See [13 Messaging and Integration](13-messaging-and-integration.md).
15. **Every push subscription MUST have a dead-letter topic (`max_delivery_attempts` between 5, the Pub/Sub minimum, and 100; choose it per subscription and document the value), a pull "inspect" subscription on that topic, and an ops UI or runbook to republish or discard dead letters.** Without the inspect subscription, dead letters are only a metric.
16. **Cloud Tasks MUST use deterministic task names derived from the business key, so a retried enqueue does not create a second task.** Hash the key (for example a hex SHA-256 prefix) rather than using sequential names, which reduce throughput. A task name cannot be reused for a period after the task has run or been deleted, so state how a legitimate repeat is keyed (include the action or a version in the hashed input).
17. **Background work runs either in a Cloud Run worker pool (always-on, instance billed) or as serverless push (Pub/Sub, Cloud Tasks, Cloud Scheduler calling HTTP endpoints, CPU only during requests).** This is an architecture choice; both are acceptable. What is not acceptable is relying on background threads in a request-billed service with `cpu_idle = true`, where CPU is throttled between requests.

### Logging

18. **When `K_SERVICE` is set, the app MUST log structured JSON with Google's console formatter, with the trace id qualified by project id, and an Error Reporting augmenter on error entries.** One JSON line per event keeps multi-line messages and stack traces together and links logs to traces. Local runs keep the readable formatter.

### Billing and cost

19. **Each environment's billing model (instance-based vs request-based, min instances, `cpu_idle`) MUST be documented with the reason, next to the Terraform that sets it or in an ADR.** Billing model and CPU allocation move together; changing one without the other either costs money or stalls background work.

## Examples

### Ingress, sidecar and its startup probe (rules 2, 4, 5)

In `infra/cloudrun_api.tf`:

```hcl
ingress = "INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER"
...
containers {
  name  = "cloud-sql-proxy"
  args  = ["--address=0.0.0.0", "--port=5432", "--auto-iam-authn", "--structured-logs", local.props.shared_db.connection_name]
  # The app depends on this container, and Cloud Run waits for a dependency only through its own startup probe.
  # Checked every second (not the default 10 s), so a missed first check does not delay the cold start.
  startup_probe {
    tcp_socket { port = 5432 }
    timeout_seconds   = 1
    period_seconds    = 1
    failure_threshold = 30
  }
}
```

### Liveness without the database (rules 6, 7)

```hcl
# Liveness must NOT include the DB check: a Cloud SQL outage would otherwise fail liveness
# on every instance simultaneously and trigger a restart storm. /alive is self-check only.
liveness_probe {
  failure_threshold     = 3
  initial_delay_seconds = 10
  period_seconds        = 60
  timeout_seconds       = 30
  http_get { path = "/alive" }
}
```

### Migrations as a job before rollout (rule 9)

A workflow job `schema` runs the EF migrations, then the Wolverine schema. Using `--wait` makes the job fail on a failed execution, so `deploy` never rolls a revision against a missing schema.

Two setup variants are in use:

- A database-manager Cloud Run job (for example `myapp-database-manager-<env>`) plus a Wolverine schema job, each run with `gcloud run jobs execute --wait`.
- The image's own `db-apply` command, run through the Auth Proxy in the deploy job.

### Data Protection with KMS, fail fast (rule 11)

```csharp
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("myapp")
    .PersistKeysToDbContext<DataProtectionKeysContext>(); // keys in Postgres; a GCS bucket works too

var kmsKeyName = builder.Configuration["Gcp:DataProtectionKmsKeyName"];
if (!string.IsNullOrWhiteSpace(kmsKeyName))
{
    // Google.Cloud.AspNetCore.DataProtection.Kms
    dataProtection.ProtectKeysWithGoogleKms(kmsKeyName);
}
else if (!builder.Environment.IsDevelopmentOrTest())
{
    throw new InvalidOperationException("Gcp:DataProtectionKmsKeyName must be set outside Development and Test.");
}
```

`PersistKeysToDbContext` comes from `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`. Check the method names against the package versions in use.

### Additive asset upload (rules 12, 13)

```yaml
- name: Upload hashed assets without deleting old ones, so rollback keeps working
  run: gcloud storage cp --recursive --no-clobber --cache-control="public, max-age=31536000, immutable" assets "gs://$PROJECT-assets/"
```

The API defines the cache headers it uses: `no-store, no-cache, must-revalidate, max-age=0` for the shell (`index.html`) and `public, max-age=31536000, immutable` for hashed files.

### Push subscription with OIDC and dead letter (rules 14, 15)

In `infra/pubsub.tf`:

```hcl
push_config {
  push_endpoint = "${google_cloud_run_v2_service.processor.uri}/internal/pubsub/${each.key}"
  oidc_token {
    service_account_email = google_service_account.pubsub_push.email
    audience              = local.pubsub_audience
  }
}
dead_letter_policy {
  dead_letter_topic     = google_pubsub_topic.dead_letter.id
  max_delivery_attempts = 5
}
```

A `google_pubsub_subscription.dead_letter_inspect` on the dead-letter topic is read by the API's ops page.

### Deterministic task names (rule 16)

Derive the task name from the business key when enqueuing: `TaskName.FromProjectLocationQueueTask(project, location, queue, task.Name)`, where `task.Name` is a hash of the key (for example the order id plus the action and a version, so a legitimate repeat gets a new name). A retried enqueue then hits the existing task instead of creating a second one.

### Worker pool variant (rule 17)

`infra/worker.tf` uses `google_cloud_run_v2_worker_pool`, always on, with CPU always allocated.

### Structured logging (rule 18)

Switch to `AddGoogleCloudConsole` only when `K_SERVICE` is set. Document why: the default formatter splits stack traces into separate INFO entries. Add an `ErrorReportingLogAugmenter` so error entries reach Error Reporting.

### Billing model (rule 19)

A comment above `api_min_instances` / `api_cpu_idle` in `infra/cloudrun_api.tf` states the model per environment: production instance-based, dev and sandbox request-based, previews scale to zero.

## Related
- [03 Infrastructure as Code](03-infrastructure-as-code.md) — Terraform conventions for these resources
- [06 Deployment and Release](06-deployment-and-release.md) — schema job and asset upload ordering
- [12 Data Access and Migrations](12-data-access-and-migrations.md) — migrations outside app start
- [13 Messaging and Integration](13-messaging-and-integration.md) — receiving push messages
- [22 Observability and Operations](22-observability-and-operations.md) — `/health`, logging and tracing
- [26 Gateway and Frontend Hosting](26-gateway-and-frontend-hosting.md) — ingress via the gateway and frontend serving
- [Security principles](../principles/security.md) — secrets and zero trust
- [Cloud and tools](../technology/cloud-and-tools.md) — Cloud Run, GCP, Pub/Sub and OpenTelemetry background
