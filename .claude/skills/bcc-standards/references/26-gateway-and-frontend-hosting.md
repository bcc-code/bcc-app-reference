# 26 Gateway and Frontend Hosting

How a browser request reaches our app: the shared BCC platform gateway terminates TLS and routes by hostname and path, content-hashed frontend bundles come from a private bucket behind Cloud CDN, and everything else — `index.html`, sign-in callbacks and `/api` — goes to the Cloud Run service. The gateway itself is owned by the platform team in `bcc-platform-gateway`; this note covers the application side and what an app team needs to know about the gateway. See Frontend Hosting Strategy and Backend-for-Frontend (BFF).

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
- **Name contract.** The gateway references app-owned backends by constructed name only — `projects/<project>/global/backendServices/bs-<app>-<component>-<env>` or `backendBuckets/bb-…`. The route file name is `app`, the backend key is `component`, the environment key is `environment`. A mismatch is not caught until apply.
- **Backends live in the app project.** A serverless NEG must be in the same project as its Cloud Run service, and a backend bucket in the same project as its GCS bucket, so the app's Terraform creates them with `bcc-platform-gateway-tf-modules` (`gateway-backend`, `gateway-backend-bucket`).
- **Matching.** Longest-prefix, not ordered. `/api/*` does not match `/api`, so declare both. Unmatched hostnames get a monitorable gateway 404.
- **Plan-time checks.** Hostname collisions, unknown zones, undeclared backends, malformed paths, and a sandbox-tier environment pointing at a prod project all fail the gateway plan. There is no automatic plan on gateway PRs; the platform team runs it.
- **App-owned extras.** Cloud CDN, Cloud Armor (`security_policy`) and IAP attach to the backend in the app's project, not the gateway. CDN invalidation is issued on the gateway's URL map, which app teams cannot reach, so rely on cache headers.

## Rules

### Routing and backends

1. **Every public GCP-hosted app MUST be served through the platform gateway, with its route in `routes/<app>.yaml` and its backends created by the `gateway-backend` and `gateway-backend-bucket` modules in the app's `infra/gateway_backend.tf`.** The gateway gives one place for TLS, certificates, DNS and HTTP→HTTPS, and lets the app lock down ingress.
2. **The host's `default_backend` MUST be the API (Cloud Run), and only `/assets` and `/assets/*` MUST route to the bucket.** `index.html` must come from the API so security headers, the per-request CSP nonce, the login check and bootstrap data apply ([16 HTTP Security Hardening](16-http-security-hardening.md), [21 Frontend](21-frontend.md)). This differs from the "common shape" in the gateway's own docs (`docs/index.md`), which puts the bucket as default and the API under `/api/*`; all three of our repos deliberately do the opposite, and the gateway docs should mention our variant.
3. **Module inputs MUST be `app` = route file name, `component` = backend key, `environment = local.props.app_environment`, and `region` MUST be the Cloud Run service's own location.** A NEG in the wrong region resolves to nothing at request time instead of failing at apply.
4. **Only one module instance per project MAY hold the project-scoped gateway grant: set `grant_gateway_access = false` on every other instance.** Two instances managing the same binding remove it from each other on destroy.
5. **Apply the app's backend before the gateway route PR merges; remove the route before destroying or renaming a backend.** A URL map cannot reference a backend that does not exist. Renames go new-alongside-old → switch route → verify → delete old.
6. **The module `source` MUST be pinned to a tag or SHA** once the platform team publishes them ([17 Supply Chain and Security Scanning](17-supply-chain-and-security-scanning.md)).

### Ingress and trust

7. **Cloud Run ingress MUST be `INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER` once traffic flows through the gateway hostname.** Otherwise the `*.run.app` URL bypasses the gateway. Change ingress in a separate apply *after* the route is live and verified; doing both at once leaves no way in if the route is wrong (orginn issue #949). See [04 GCP Runtime](04-gcp-runtime.md).
8. **The app MUST trust exactly one proxy hop for `X-Forwarded-For`/`X-Forwarded-Proto` (`ForwardLimit = 1`), and this MUST be tested.** The gateway has no fixed address list to put in `KnownProxies`; with ingress restricted to the load balancer, one hop is the gateway. orginn tests this in `ForwardedHeadersExtensionsTests`. See [16 HTTP Security Hardening](16-http-security-hardening.md).
9. **Hosts that must not be public (previews, dev tools) SHOULD use IAP on the backend service or Cloud Armor, not only app login.** These attach in the app project. See [07 Preview Environments](07-preview-environments.md).

### Bucket and assets

10. **The assets bucket MUST be private: `uniform_bucket_level_access = true`, `public_access_prevention = "enforced"`, and `grant_object_access = true` on the bucket module** so the project's load balancer service agent reads it. That grant can fail on the first apply (the service agent is created with the project's first backend bucket); re-apply.
11. **CI MUST upload bundles additively before the rollout, with `public, max-age=31536000, immutable`, using `gcloud storage rsync` without delete (or `cp --no-clobber`).** Old bundles stay so a Cloud Run traffic rollback still finds them ([06 Deployment and Release](06-deployment-and-release.md)). The CI service account gets `roles/storage.objectUser` on the bucket, not `objectAdmin`.
12. **Bundles MUST sit under `assets/` in the bucket, at the same path the SPA requests, so no `path_prefix_rewrite` is needed.** The route comment in the gateway file says so (orginn, privacy and vote all do this).
13. **Cloud CDN stays on for the backend bucket (module default) and off for the API backend (module default).** Responses from the API set their own `Cache-Control` (`no-store` for `index.html` and `/api`); do not enable CDN on the API backend.
14. **Bucket CORS MUST be configured only when another origin loads files from it (widgets), with `GET`/`HEAD` only.** bcc-privacy sets none because nothing reads it cross-origin; vote-rewrite allows `*` for its widget loader and explains why.
15. **Each repo SHOULD state in the bucket Terraform which files come from the bucket and which from the API image.** bcc-privacy's comment lists `index.html`, `widgets.js` and `ce/*` as API-served; orginn and vote state the same split.

## Example

`infra/gateway_backend.tf` (gateway modules), `infra/storage.tf` (private assets bucket), `infra/cloudrun_api.tf` (ingress), the asset upload in `.github/workflows/deploy.yml`, and the route still to be added in `routes/<app>.yaml` in `bcc-platform-gateway`.
