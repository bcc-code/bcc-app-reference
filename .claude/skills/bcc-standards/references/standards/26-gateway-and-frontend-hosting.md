# 26 Gateway and Frontend Hosting

How a browser request reaches our app: the shared BCC platform gateway terminates TLS and routes by hostname and path, content-hashed frontend bundles come from a private bucket behind Cloud CDN, and everything else (`index.html`, sign-in callbacks and `/api`) goes to the Cloud Run service. The gateway itself is owned by the platform team in `bcc-platform-gateway`; this standard covers the application side and what an app team needs to know about the gateway.

## Request path

```
browser ──HTTPS──► BCC platform gateway (global external ALB, one per tier)
                   TLS (cert per hostname, MODERN/TLS 1.2), HTTP→HTTPS redirect,
                   unknown host → gateway 404
                   │
                   ├─ /assets, /assets/*  ──► bb-<app>-<component>-<env>  (backend bucket, Cloud CDN on)
                   │                           └─ private GCS bucket, objects under assets/
                   │
                   └─ everything else     ──► bs-<app>-api-<env>          (backend service, serverless NEG)
                                               └─ Cloud Run: index.html (CSP nonce, login check,
                                                  bootstrap data), /signin-oidc, /api/*, /health
```

Locally the same split is made by the API itself: it serves `index.html` and proxies Vite through YARP on one HTTPS origin (`*.dev.localhost`), see [02 Local Development](02-local-development.md).

## How the gateway works (platform side)

- **Two gateways (tiers).** `sandbox` serves the `dev` and `sandbox` environments; `prod` serves `prod` and `staging`. A staging route lands on the production URL map and is reviewed as a production change. Each tier has a DNS anchor (`gateway.bcc.no`, `gateway.sandbox.bcc.no`); app hostnames are CNAMEs to it.
- **One file per app.** `routes/<app>.yaml` in `bcc-platform-gateway` declares environments (project ids), backends and hosts. DNS records and a managed certificate per hostname follow from the file; there is no certificate work for the app team.
- **Name contract.** The gateway references app-owned backends by constructed name only: `projects/<project>/global/backendServices/bs-<app>-<component>-<env>` or `backendBuckets/bb-...`. The route file name is `app`, the backend key is `component`, the environment key is `environment`. A mismatch is not caught until apply.
- **Backends live in the app project.** A serverless NEG must be in the same project as its Cloud Run service, and a backend bucket in the same project as its GCS bucket, so the app's Terraform creates them with `bcc-platform-gateway-tf-modules` (`gateway-backend`, `gateway-backend-bucket`).
- **Matching.** Longest-prefix, not ordered. `/api/*` does not match `/api`, so declare both. Unmatched hostnames get a monitorable gateway 404.
- **Plan-time checks.** Hostname collisions, unknown zones, undeclared backends, malformed paths, and a sandbox-tier environment pointing at a prod project all fail the gateway plan. There is no automatic plan on gateway PRs; the platform team runs it.
- **App-owned extras.** Cloud CDN, Cloud Armor (`security_policy`) and IAP attach to the backend in the app's project, not the gateway. CDN invalidation is issued on the gateway's URL map, which app teams cannot reach, so rely on cache headers.

## Rules

### Routing and backends

1. **Every public GCP-hosted app MUST be served through the platform gateway, with its route in `routes/<app>.yaml` and its backends created by the `gateway-backend` and `gateway-backend-bucket` modules in the app's `infra/gateway_backend.tf`.** The gateway gives one place for TLS, certificates, DNS and HTTP→HTTPS, and lets the app lock down ingress.
2. **The host's `default_backend` MUST be the API (Cloud Run), and only `/assets` and `/assets/*` MUST route to the bucket.** `index.html` must come from the API so security headers, the per-request CSP nonce, the login check and bootstrap data apply ([16 HTTP Security Hardening](16-http-security-hardening.md), [21 Frontend](21-frontend.md)). This differs from the "common shape" in the gateway's own docs, which puts the bucket as default and the API under `/api/*`. Our apps deliberately do the opposite, and the gateway docs should mention this variant.
3. **Module inputs MUST be `app` = route file name, `component` = backend key, `environment = local.props.app_environment`, and `region` MUST be the Cloud Run service's own location.** A NEG in the wrong region resolves to nothing at request time instead of failing at apply.
4. **Only one module instance per project MAY hold the project-scoped gateway grant: set `grant_gateway_access = false` on every other instance.** Two instances managing the same binding remove it from each other on destroy.
5. **Apply the app's backend before the gateway route PR merges; remove the route before destroying or renaming a backend.** A URL map cannot reference a backend that does not exist. Renames go new-alongside-old, then switch route, verify, and delete old.
6. **The module `source` MUST be pinned to a full commit SHA (with a version comment where a tag exists); `?ref=main` is not allowed** ([17 Supply Chain and Security Scanning](17-supply-chain-and-security-scanning.md)).

### Ingress and trust

7. **Cloud Run ingress MUST be `INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER` once traffic flows through the gateway hostname.** Otherwise the `*.run.app` URL bypasses the gateway. Change ingress in a separate apply *after* the route is live and verified; doing both at once leaves no way in if the route is wrong. See [04 GCP Runtime](04-gcp-runtime.md).
8. **Forwarded headers MUST be enabled so the app sees the original scheme and host. Before the app uses the client IP, the configuration MUST match the header shape the external Application Load Balancer produces, and this MUST be tested.** Google documents `X-Forwarded-For` as `<supplied-value>,<client-ip>,<load-balancer-ip>`, so with `ForwardLimit = 1` the app sees the load balancer's address, not the client's, and per-IP rate limiting breaks. With ingress restricted to the load balancer, set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` and `ForwardLimit = 2`; otherwise use `ForwardLimit = 2` with the load balancer and Google front-end ranges as known networks, or a parser that takes the client entry. Invalid configuration fails startup. Cover it with a test that uses the real header shape, and verify once on a deployed app by logging `RemoteIpAddress`. See [16 HTTP Security Hardening](16-http-security-hardening.md).
9. **Hosts that must not be public (dev tools, internal admin hosts) SHOULD use IAP on the backend service or Cloud Armor, not only app login.** These attach in the app project. Previews are a documented exception: see [07 Preview Environments](07-preview-environments.md).

### Bucket and assets

10. **The assets bucket MUST be private: `uniform_bucket_level_access = true`, `public_access_prevention = "enforced"`, and `grant_object_access = true` on the bucket module** so the project's load balancer service agent reads it. That grant can fail on the first apply (the service agent is created with the project's first backend bucket); re-apply.
11. **CI MUST upload bundles additively before the rollout, with `public, max-age=31536000, immutable`, using `gcloud storage rsync` without delete (or `cp --no-clobber`).** Old bundles stay so a Cloud Run traffic rollback still finds them ([06 Deployment and Release](06-deployment-and-release.md)). The CI service account gets `roles/storage.objectUser` on the bucket, not `objectAdmin`. `objectUser` can still overwrite and delete objects, so the bucket SHOULD have object versioning or a lifecycle rule that removes bundles only after the rollback window.
12. **Bundles MUST sit under `assets/` in the bucket, at the same path the SPA requests, so no `path_prefix_rewrite` is needed.** Say so in a comment on the route in the gateway file.
13. **Cloud CDN MUST stay on for the backend bucket and off for the API backend (both are the module defaults).** Responses from the API set their own `Cache-Control` (`no-store` for `index.html` and `/api`), so CDN on the API backend is not needed.
14. **Bucket CORS MUST be configured only when another origin loads files from it (widgets), with `GET`/`HEAD` only.** Set none when nothing reads the bucket cross-origin; where a widget loader needs `*`, explain why in a comment.
15. **Each repo SHOULD state in the bucket Terraform which files come from the bucket and which from the API image.** For example, a comment listing `index.html` and the other files the API image serves.

### Health endpoints

16. **`/health` with dependency details MUST NOT be reachable through the public gateway.** Either do not route it (Cloud Run probes reach the instance directly), or expose a status-only answer on the public path. See [22 Observability and Operations](22-observability-and-operations.md).

## Examples

### Gateway route (rules 1, 2, 12)

In `bcc-platform-gateway`, `routes/myapp.yaml`:

```yaml
backends:
  api:
    type: cloud_run # bs-myapp-api-<env>, infra/gateway_backend.tf in the app repo
  ui:
    type: backend_bucket # bb-myapp-ui-<env>, infra/bucket_ui.tf in the app repo

hosts:
  - name: web
    hostnames:
      dev: [myapp.dev.bcc.no]
      sandbox: [myapp.sandbox.bcc.no]
    # The API serves the SPA shell (index.html); only the content-hashed bundles come from the
    # bucket, stored under assets/ at the same path the SPA requests, so no rewrite.
    default_backend: api
    routes:
      - paths: ["/assets", "/assets/*"]
        backend: ui
```

### App backends (rules 3, 4, 6, 10)

In the app's `infra/gateway_backend.tf` and `infra/bucket_ui.tf`:

```hcl
module "gateway_backend_api" {
  source = "git::https://github.com/bcc-code/bcc-platform-gateway-tf-modules//gateway-backend?ref=<sha>" # <sha>: full commit SHA, version in a comment
  app         = "myapp"
  component   = "api"
  environment = local.props.app_environment
  cloud_run_service = google_cloud_run_v2_service.main.name
  region            = google_cloud_run_v2_service.main.location
}

resource "google_storage_bucket" "ui" {
  ...
  uniform_bucket_level_access = true
  public_access_prevention    = "enforced"
}

module "gateway_backend_ui" {
  source = "git::https://github.com/bcc-code/bcc-platform-gateway-tf-modules//gateway-backend-bucket?ref=<sha>"
  ...
  grant_object_access  = true
  grant_gateway_access = false
}
```

Pin `ref` to a full commit SHA (rule 6). Put a header comment in `gateway_backend.tf` that explains the name contract and the apply order, and a comment in `bucket_ui.tf` explaining why `index.html` stays in the image ("so the SPA and the API it calls are always deployed together").

### Locked-down ingress (rule 7)

In `infra/cloudrun_api.tf`: `ingress = "INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER"`. An internal worker service that nothing outside GCP calls uses `INGRESS_TRAFFIC_INTERNAL_ONLY`.

### Asset upload (rule 11)

```yaml
# adds objects, and the previous release's bundles stay for a Cloud Run rollback.
- name: Upload frontend assets to the UI bucket
  run: |-
    gcloud storage rsync --recursive \
      --cache-control="public, max-age=31536000, immutable" \
      ${{ runner.temp }}/assets "${UI_BUCKET}/assets"
```

### Onboarding with the platform gateway (rules 1, 5)

The gateway's onboarding guide (published to the developer portal as "Shared Gateway") covers the steps, moving a live hostname (two applies, lower TTL first, keep the old origin as rollback), shared hostnames like `api.bcc.no`, and a symptom table (gateway 404 vs app 404, 502 from wrong ingress, certificate not yet `ACTIVE`).

## Related

- [02 Local Development](02-local-development.md) — one HTTPS origin locally
- [03 Infrastructure as Code](03-infrastructure-as-code.md) — Terraform conventions
- [04 GCP Runtime](04-gcp-runtime.md) — Cloud Run ingress and assets
- [06 Deployment and Release](06-deployment-and-release.md) — upload order and rollback
- [07 Preview Environments](07-preview-environments.md) — previews and the preview exception
- [16 HTTP Security Hardening](16-http-security-hardening.md) — CSP nonce and forwarded headers
- [17 Supply Chain and Security Scanning](17-supply-chain-and-security-scanning.md) — pinning modules
- [21 Frontend](21-frontend.md) — SPA shell served by the API
