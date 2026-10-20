---
standard: 04 GCP Runtime
assessed: 2026-10-19
commit: e4b2b86
updated: 2026-10-20 (ServerlessWithOutbox, ADR 0007)
---
# 04 GCP Runtime — assessment

The Cloud Run definition follows the standard: v2 resources, internal-load-balancer ingress, Auth Proxy sidecar with its own
fast probe, explicit startup and `/alive` liveness probes, a migration job, per-secret access, KMS-encrypted Data Protection
keys, additive asset upload and structured GCP logging. The API service and the migration job now get
`Frontend__StaticPath`, which the API requires at startup in `Static` mode; whether the API starts in GCP is still unverified
because nothing has been deployed. Background work is serverless push (ADR 0007): a Pub/Sub push subscription with its own
service account, OIDC audience, dead-letter topic and inspect subscription, and a Cloud Scheduler job for the durability
pass, all as HTTP requests. One defect remains: the image is not built on a chiseled base although the deploy workflow says
it is.

**Score: Adequate** · ✅ 17 · ⚠️ 0 · ❌ 1 · N/A 1

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | Cloud Run v2 resources | MUST | ✅ | `google_cloud_run_v2_service` (`infra/cloudrun_api.tf:24`), `google_cloud_run_v2_job` (`infra/cloudrun_migrate.tf:7`), preview too (`infra/preview/main.tf:29`). |
| 2 | Ingress internal LB only; allUsers comment says ingress restricts | MUST | ✅ | `INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER` (`cloudrun_api.tf:40`); `allUsers` invoker with the required comment (`:142-149`). The Pub/Sub push and scheduler accounts also get `roles/run.invoker` on the service (`pubsub.tf`, `scheduler.tf`); `infra/README.md` states that these are the intended callers and that the app's token check, not IAM, restricts them. |
| 3 | Non-prod scales to zero; startup CPU boost | SHOULD | ✅ | Sandbox `api_min_instances = 0`, `api_cpu_idle = true` (`main.tf:10-12`); `startup_cpu_boost = true` (`cloudrun_api.tf:64`). |
| 4 | Auth Proxy sidecar `--auto-iam-authn`; app `depends_on` it | MUST | ✅ | `cloudrun_api.tf:58,110-113`; same in the job (`cloudrun_migrate.tf:22,48-51`). |
| 5 | Sidecar TCP startup probe on 5432, period 1 s | MUST | ✅ | `cloudrun_api.tf:119-126`, `cloudrun_migrate.tf:55-62`. |
| 6 | Explicit TCP startup probe on app port | MUST | ✅ | `cloudrun_api.tf:84-93` with the reason it is written out. |
| 7 | Liveness on `/alive`; `/alive` checks no dependencies | MUST | ✅ | `cloudrun_api.tf:95-107`; `/alive` runs only checks tagged `live` (`api/ServiceDefaults/Extensions.cs:23,76`). Tests `SecurityTests.The_database_check_is_in_health_and_not_in_alive` (both variants). |
| 8 | `PublishContainer` on a chiseled base image; no Dockerfile | MUST | ❌ | `deploy.yml:135-139` uses `/t:PublishContainer` and there is no Dockerfile, but no project or props file sets `ContainerFamily` or `ContainerBaseImage` (`grep` over `*.csproj`, `*.props`), so the SDK default (non-chiseled) `aspnet` image is used. The step name "chiseled base image is set in the project file" (`deploy.yml:129`) is false. |
| 9 | Migrations as job/deploy step before traffic, never on app start | MUST | ✅ | `cloudrun_migrate.tf` job; `deploy.yml:184-189,257-262` updates and executes it with `--wait` before the service update. Startup does not migrate (standard 12 assessment). |
| 10 | Secrets via `secret_key_ref`; accessor per secret | MUST | ✅ | `cloudrun_api.tf:74-82`, `cloudrun_migrate.tf:37-45`; bindings per secret (`secrets.tf:12-23`). |
| 11 | Data Protection keys in Postgres, KMS-encrypted; fail without key outside Dev/Test | MUST | ✅ | `api/BccCode.Template.Web/DataProtection/DataProtectionConfiguration.cs:23-37` (key store from the variant, `KmsXmlEncryptor`, throws outside Development/Test); stores in `Infrastructure/MartenDataProtectionKeys.cs`, `Infrastructure/EfDataProtectionKeys.cs`; KMS key 90-day rotation and `prevent_destroy` (`kms.tf:8-16`). No test of the startup failure. |
| 12 | Hashed assets uploaded additively, immutable, before rollout | MUST | ✅ | `gcloud storage cp --recursive --no-clobber --cache-control="public, max-age=31536000, immutable"` before migrate and rollout (`deploy.yml:180-181,255-256`). |
| 13 | `index.html` served by API with `no-store` | MUST | ✅ | `IndexHtmlMiddleware.cs:34` sets `no-store`; `deploy.yml:114-116` bakes `index.html` into the API project's `wwwroot`, and the comment there points at the setting. `appsettings.json` sets `Frontend:Mode` `Static`, and `FrontendConfiguration.cs:27` requires `Frontend:StaticPath`; `infra/cloudrun_api.tf:13-16` now sets `Frontend__StaticPath = "/app/wwwroot"` in `local.api_env`, which the migration job also uses (`cloudrun_migrate.tf:31` merges `local.api_env`). The preview container sets the same value (`infra/preview/main.tf:51-55`). The direct `/index.html` request no longer bypasses the middleware (standard 16 rule 8). Not deployed; see "Not verifiable". |
| 14 | Pub/Sub push with OIDC, per-caller audience | MUST | ✅ | `google_pubsub_subscription.messages_push` sends an OIDC token as the dedicated `sa-<app>-pubsub-push-<env>` account with audience `https://<service>/internal/pubsub`; Cloud Scheduler uses its own account and audience (`infra/pubsub.tf`, `infra/scheduler.tf`). Both audiences are Cloud Run `custom_audiences` (`cloudrun_api.tf:43`). The receiver requires audience, service-account email and `email_verified` per caller (`api/BccCode.Template.Web/Internal/InternalAuthConfiguration.cs:84-98`); tests `InternalAuthTests` (wrong key, wrong audience, wrong account, unverified email, the other caller's audience). |
| 15 | Dead-letter topic, inspect subscription, runbook | MUST | ✅ | `dead_letter_policy` with `max_delivery_attempts = 5`, dead-letter topic and `dead_letter_inspect` pull subscription with 7-day retention, plus the service agent's publisher and subscriber grants (`infra/pubsub.tf`). Runbook `docs/runbooks/pubsub-dead-letters.md` describes how to inspect and republish. |
| 16 | Deterministic Cloud Tasks names | MUST | N/A | No Cloud Tasks. |
| 17 | No background threads under request billing | MUST | ✅ | `cpu_idle = true` in both environments with the consequence stated (`main.tf:6-7`). Messages are delivered by serverless push: Wolverine `ServerlessWithOutbox` (no agents, polling or local queues, `Messaging/TemplateMessaging.cs:28-38`), Pub/Sub push to `/internal/pubsub/{subscription}`, and Cloud Scheduler calling `/internal/durability/pass` every minute. The Cloud Run request timeout is 600 s to match the push ack deadline (`cloudrun_api.tf:48-49`). No hosted services (`LayerRulesTests.Nothing_runs_in_the_background`), and the Marten async daemon is off (`TemplateStore.cs:19-20`). |
| 18 | JSON logs with GCP formatter on `K_SERVICE`; trace id; Error Reporting | MUST | ✅ | `api/ServiceDefaults/GcpLoggingExtensions.cs:36-45` (only when `K_SERVICE` set), project-qualified trace (`:62`), `ErrorReportingLogAugmenter` (`:64,74-88`); `Gcp__ProjectId` set in `cloudrun_api.tf:8`. Tests in `api/tests/BccCode.Template.Web.Tests/GcpStructuredLoggingTests.cs`. |
| 19 | Billing model per env documented | MUST | ✅ | `infra/main.tf:6-7` (request-based in both, what to change together), prod min instance reason at `:20`. |

## Gaps to close

- **8**: Add `<ContainerFamily>noble-chiseled</ContainerFamily>` (or `noble-chiseled-extra` if ICU is needed) to the two API
  projects or to `Directory.Build.props` for deployed projects, and verify the image base in the build log. Correct the step
  name at `deploy.yml:129` at the same time.
- **11**: Add a test that `AddTemplateDataProtection` throws in `Production` without `Gcp:DataProtectionKmsKeyName`.
- **13** (optional): A test that starts the host with production-like settings (no `Frontend:StaticPath` override from the
  test fixture) would catch a missing setting before a deploy does.

## Not verifiable in the repo

- Rules 2, 4–7: that the live services match Terraform (run `terraform plan` against sandbox and prod, or
  `gcloud run services describe template-api-<env>`).
- Rule 13: that the API and the migration job start in Cloud Run with `Frontend__StaticPath = "/app/wwwroot"`, that is, that
  the SDK container puts the published `wwwroot` under `/app`. Confirm with one deploy to sandbox, or by running the published
  image locally with `ASPNETCORE_ENVIRONMENT=Production` and the Cloud Run env vars.
- Rule 18: that log entries in Cloud Logging carry `logging.googleapis.com/trace` and show up in Error Reporting.
- Rules 2, 14, 17: that Pub/Sub push and Cloud Scheduler reach the `run.app` URL under `INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER`
  (documented by Google as allowed, not tried here), that Cloud Run accepts their tokens through `custom_audiences`, and that
  Cloud Scheduler is offered in the chosen region. `infra/README.md` lists manual checks for the first sandbox deploy.

Note: the 600 s request timeout applies to every route, not only `/internal/pubsub/**`, so a slow user request can now hold
an instance slot for up to 10 minutes instead of 30 s. Cloud Run has one timeout per service; a separate service for the push
receiver would be the way to keep a short timeout for users.
