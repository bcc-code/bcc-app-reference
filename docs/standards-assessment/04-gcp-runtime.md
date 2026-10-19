---
standard: 04 GCP Runtime
assessed: 2026-10-19
commit: e4b2b86
updated: 2026-10-19 (gaps 1–4 of the index fixed)
---
# 04 GCP Runtime — assessment

The Cloud Run definition follows the standard: v2 resources, internal-load-balancer ingress, Auth Proxy sidecar with its own
fast probe, explicit startup and `/alive` liveness probes, a migration job, per-secret access, KMS-encrypted Data Protection
keys, additive asset upload and structured GCP logging. The API service and the migration job now get
`Frontend__StaticPath`, which the API requires at startup in `Static` mode; whether the API starts in GCP is still unverified
because nothing has been deployed. One defect remains: the image is not built on a chiseled base although the deploy
workflow says it is.

**Score: Adequate** · ✅ 15 · ⚠️ 0 · ❌ 1 · N/A 3

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | Cloud Run v2 resources | MUST | ✅ | `google_cloud_run_v2_service` (`infra/cloudrun_api.tf:24`), `google_cloud_run_v2_job` (`infra/cloudrun_migrate.tf:7`), preview too (`infra/preview/main.tf:29`). |
| 2 | Ingress internal LB only; allUsers comment says ingress restricts | MUST | ✅ | `INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER` (`cloudrun_api.tf:32`); `allUsers` invoker with the required comment (`:130-137`). |
| 3 | Non-prod scales to zero; startup CPU boost | SHOULD | ✅ | Sandbox `api_min_instances = 0`, `api_cpu_idle = true` (`main.tf:10-12`); `startup_cpu_boost = true` (`cloudrun_api.tf:52`). |
| 4 | Auth Proxy sidecar `--auto-iam-authn`; app `depends_on` it | MUST | ✅ | `cloudrun_api.tf:46,98-101`; same in the job (`cloudrun_migrate.tf:22,48-51`). |
| 5 | Sidecar TCP startup probe on 5432, period 1 s | MUST | ✅ | `cloudrun_api.tf:107-114`, `cloudrun_migrate.tf:55-62`. |
| 6 | Explicit TCP startup probe on app port | MUST | ✅ | `cloudrun_api.tf:72-81` with the reason it is written out. |
| 7 | Liveness on `/alive`; `/alive` checks no dependencies | MUST | ✅ | `cloudrun_api.tf:83-95`; `/alive` runs only checks tagged `live` (`api/ServiceDefaults/Extensions.cs:23,76`). Tests `SecurityTests.The_database_check_is_in_health_and_not_in_alive` (both variants). |
| 8 | `PublishContainer` on a chiseled base image; no Dockerfile | MUST | ❌ | `deploy.yml:135-139` uses `/t:PublishContainer` and there is no Dockerfile, but no project or props file sets `ContainerFamily` or `ContainerBaseImage` (`grep` over `*.csproj`, `*.props`), so the SDK default (non-chiseled) `aspnet` image is used. The step name "chiseled base image is set in the project file" (`deploy.yml:129`) is false. |
| 9 | Migrations as job/deploy step before traffic, never on app start | MUST | ✅ | `cloudrun_migrate.tf` job; `deploy.yml:184-189,257-262` updates and executes it with `--wait` before the service update. Startup does not migrate (standard 12 assessment). |
| 10 | Secrets via `secret_key_ref`; accessor per secret | MUST | ✅ | `cloudrun_api.tf:62-70`, `cloudrun_migrate.tf:37-45`; bindings per secret (`secrets.tf:12-23`). |
| 11 | Data Protection keys in Postgres, KMS-encrypted; fail without key outside Dev/Test | MUST | ✅ | `api/BccCode.Template.Web/DataProtection/DataProtectionConfiguration.cs:23-37` (key store from the variant, `KmsXmlEncryptor`, throws outside Development/Test); stores in `Infrastructure/MartenDataProtectionKeys.cs`, `Infrastructure/EfDataProtectionKeys.cs`; KMS key 90-day rotation and `prevent_destroy` (`kms.tf:8-16`). No test of the startup failure. |
| 12 | Hashed assets uploaded additively, immutable, before rollout | MUST | ✅ | `gcloud storage cp --recursive --no-clobber --cache-control="public, max-age=31536000, immutable"` before migrate and rollout (`deploy.yml:180-181,255-256`). |
| 13 | `index.html` served by API with `no-store` | MUST | ✅ | `IndexHtmlMiddleware.cs:34` sets `no-store`; `deploy.yml:114-116` bakes `index.html` into the API project's `wwwroot`, and the comment there points at the setting. `appsettings.json` sets `Frontend:Mode` `Static`, and `FrontendConfiguration.cs:27` requires `Frontend:StaticPath`; `infra/cloudrun_api.tf:13-16` now sets `Frontend__StaticPath = "/app/wwwroot"` in `local.api_env`, which the migration job also uses (`cloudrun_migrate.tf:31` merges `local.api_env`). The preview container sets the same value (`infra/preview/main.tf:49-53`). The direct `/index.html` request no longer bypasses the middleware (standard 16 rule 8). Not deployed; see "Not verifiable". |
| 14 | Pub/Sub push with OIDC, per-caller audience | MUST | N/A | No Pub/Sub in the sample. |
| 15 | Dead-letter topic, inspect subscription, runbook | MUST | N/A | No push subscriptions. |
| 16 | Deterministic Cloud Tasks names | MUST | N/A | No Cloud Tasks. |
| 17 | No background threads under request billing | MUST | ✅ | `cpu_idle = true` in both environments with the consequence stated (`main.tf:6-7`); no hosted background services in the sample, and the Marten async daemon is deliberately off (`TemplateStore.cs:19-20`). |
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
