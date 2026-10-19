---
standard: 26 Gateway and Frontend Hosting
assessed: 2026-10-19
commit: e4b2b86
---
# 26 Gateway and Frontend Hosting — assessment

The app side of the gateway setup follows the standard: both backends come from the platform modules pinned to a SHA, the API
is the default backend, ingress is limited to the load balancer, the assets bucket is private, and CI uploads hashed bundles
additively before the rollout. The forwarded-headers limit is configured but not tested, and preview environments are public.

**Score: Strong** · ✅ 13 · ⚠️ 1 · ❌ 1 · N/A 0

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | Served through the platform gateway; backends via `gateway-backend`/`gateway-backend-bucket` in `infra/gateway_backend.tf` | MUST | ✅ | `infra/gateway_backend.tf:19-43`. The route file in `bcc-platform-gateway` is outside this repo. |
| 2 | Host `default_backend` is the API; only `/assets`, `/assets/*` go to the bucket | MUST | ✅ | Route shape documented in `infra/gateway_backend.tf:1-12`; the API serves `index.html` (`api/BccCode.Template.Web/Frontend/IndexHtmlMiddleware.cs`). |
| 3 | Module inputs `app`, `component`, `environment`; `region` = service location | MUST | ✅ | `gateway_backend.tf:22-27`, `:33-35`; `region = google_cloud_run_v2_service.api.location`. Environment is `local.env` (this repo's equivalent of `local.props.app_environment`). |
| 4 | Only one module instance holds the project-scoped gateway grant | MUST | ✅ | `gateway_backend.tf:38-39` sets `grant_gateway_access = false` on the bucket module. |
| 5 | Apply backend before route; remove route before destroying a backend | MUST | ✅ | Documented at `gateway_backend.tf:14`. Process; see below. |
| 6 | Module `source` pinned to a tag or SHA | MUST | ✅ | `gateway_backend.tf:20`, `:31` pinned to commit `69e6a49…`; `:16-18` explains why not a tag. |
| 7 | Cloud Run ingress internal load balancer once routed | MUST | ✅ | `infra/cloudrun_api.tf:28`; `:26-27` warns to change it in a separate apply after the route is live. |
| 8 | Trust exactly one proxy hop (`ForwardLimit = 1`), tested | MUST | ⚠️ | `api/BccCode.Template.Web/Http/ForwardedHeaders.cs:13-18` sets `ForwardLimit = 1`, used in `TemplateWeb.cs:26`, `:36`. No test sends a multi-hop `X-Forwarded-For` and checks the resolved client address; tests only set `X-Forwarded-Proto` (`Api.*.Tests/Infrastructure/AppFixture.cs`). |
| 9 | Non-public hosts (previews) behind IAP or Cloud Armor | SHOULD | ❌ | `infra/preview/main.tf:34-36` uses `INGRESS_TRAFFIC_ALL` with a TODO; `:76-81` grants `allUsers` invoker. |
| 10 | Private assets bucket: uniform access, public access prevention, `grant_object_access` | MUST | ✅ | `infra/storage.tf:8-9`; `gateway_backend.tf:42`. |
| 11 | CI uploads additively before rollout with immutable cache headers; CI SA has `objectUser` | MUST | ✅ | `.github/workflows/deploy.yml:181-182` (`gcloud storage cp --recursive --no-clobber --cache-control="public, max-age=31536000, immutable"`) runs before migrate and rollout; same for prod at `:256-257`. `infra/iam.tf:157-161` grants `roles/storage.objectUser`. |
| 12 | Bundles under `assets/` in the bucket, same path as requested | MUST | ✅ | Artifact is `frontend/dist/assets` (`deploy.yml:148-149`), downloaded to `assets/` and copied recursively into `gs://$PROJECT-assets/`, giving `assets/<file>`. |
| 13 | CDN on the bucket backend, off for the API; API sets `no-store` | MUST | ✅ | Module defaults (no CDN override in `gateway_backend.tf`); `IndexHtmlMiddleware.cs:34` and `Http/SecurityHeaders.cs:27-28` set `no-store`; tested in `Api.*.Tests/SecurityTests.cs:81`, `:105`. |
| 14 | Bucket CORS only when another origin reads it | MUST | ✅ | `infra/storage.tf:11` — no CORS block, with the reason. |
| 15 | Bucket Terraform states which files come from the bucket and which from the API | SHOULD | ✅ | `infra/storage.tf:1-3`. |

## Gaps to close

- **8**: Add a test in `api/tests/BccCode.Template.Web.Tests/` that sends `X-Forwarded-For: <spoofed>, <client>` through the
  configured middleware and asserts `RemoteIpAddress` is the last (gateway-appended) address, and that a single header value is
  taken as is. orginn's `ForwardedHeadersExtensionsTests` is the reference.
- **9** (SHOULD): Put previews behind IAP or Cloud Armor in `infra/preview/main.tf` (and close the TODO), or route them through
  the gateway with IAP on the backend service.

## Not verifiable in the repo

- **1, 2, 5**: the `routes/<app>.yaml` entry in `bcc-platform-gateway` (default backend `api`, `/assets` and `/assets/*` to
  `assets`, both paths declared) and the order in which it was applied.
- **7**: that the route was live before the ingress change was applied in each environment.
- **10**: whether the first apply's `grant_object_access` failed and was re-applied; check that the load balancer service agent
  has `roles/storage.objectViewer` on the bucket.
- **13**: CDN state on the deployed backend services (`gcloud compute backend-services describe bs-<app>-api-<env>`).
