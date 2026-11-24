# First deploy of an environment

The one-time steps to get an application made from the template running in a new environment (`sandbox` first, then `prod`). After this, every green push to `main` that changes something deployable (not only docs or tests) deploys by itself ([ADR 0014](../adrs/0014-build-once-deploy-after-green-ci.md)). The design is in [ADR 0013](../adrs/0013-cloud-run-behind-the-platform-gateway.md). `<app>` is the application's slug (`template` until `scripts/init-app.ps1` has run).

## Variables and secrets

| Name | Kind and scope | Who sets it |
|---|---|---|
| `GCP_WIDPP_ID_DEV`, `GCP_WIDPP_ID_PROD` | Org variables: the workload identity providers | The platform team. Visible to repositories in the organization |
| `OTEL_EXPORTER_OTLP_ENDPOINT_DEV`, `OTEL_EXPORTER_OTLP_ENDPOINT_PROD` and the secrets `OTEL_EXPORTER_OTLP_AUTH_KEY_DEV`, `OTEL_EXPORTER_OTLP_AUTH_KEY_PROD` | Org variables and secrets: the BCC monitoring collector ([ADR 0026](../adrs/0026-telemetry-export-through-the-bcc-monitoring-collector.md)) | The platform team. If one is missing the workflows pass it empty and no telemetry arrives |
| `PROPS`, `GCP_PROJECT_ID`, `GCP_REGION` and the state-backend variables `AZURE_*` | Environment variables of `sandbox`, `prod` and `dev` | The landing zone, in step 2. Do not edit them |
| `APP_BASE_URL` | Environment variable of `sandbox` and `prod`: `https://<host>` of the gateway | By hand, in step 6, before the first deploy of the environment. Required for `sandbox` and `prod`: ingress is internal from the start, so the runner cannot reach the `run.app` URL, and the smoke test fails with an error when it is empty |
| `ALERT_EMAILS_SANDBOX`, `ALERT_EMAILS_PROD` | Repository variables: a JSON list such as `["ops@example.org"]` | By hand. `alert_emails` is empty when unset, and prod refuses an empty list |

No repository secret is needed.

## 1. GitHub environment

In the repository settings, create the environment `sandbox`. The landing zone writes its variables into it, and the deploy job runs in it. For `prod`, also add required reviewers (standard 06.4) and limit the deployment branches to `main`: the workload identity binding trusts the environment name. The environments `dev` (previews) and `preview` (deployment records) are described in [repository settings](../development/repository-settings.md).

## 2. Provision the project

Run **Provision Project** (`provision-project.yml`) with `environment: sandbox`, first with `action: plan`, then `apply`. It creates the GCP project for `<app>`, the Terraform state, `sa-gha-cicd` with its federation binding to this repository's `sandbox` environment, a database on the platform's shared Cloud SQL instance (`use-shared-db: true`, [ADR 0015](../adrs/0015-shared-platform-postgres-with-iam-auth.md)), and the environment variables listed above.

Check that `PROPS` now has `shared_db.db_name`, `shared_db.connection_name` and `shared_db.admin_group_id`. The schema step in `deploy.yml` hands the created objects to the database's owner role (`REASSIGN OWNED`, [ADR 0016](../adrs/0016-schema-applied-by-a-deploy-step.md)); the landing zone creates the database and its owner role both named `${project_name}-${env}` and grants the role to the access group.

The workflow has to be on `main` before it can be run. If the deploy workflow runs on `main` before the project exists, it fails at the Terraform step; re-run it after this step.

## 3. Set up sign-in

The app signs users in through BCC Signon (Auth0) with a confidential client ([ADR 0019](../adrs/0019-bff-sign-in-with-bcc-signon.md)). Do this before the first deploy; the apply fails on purpose while the client id or the secret is missing.

**Client.** In BCC Signon create a client for the environment:

| Setting | Sandbox value |
|---|---|
| Type | Regular web application, confidential (client secret) |
| Allowed callback URL | `https://<host>/signin-oidc` |
| Allowed logout URL | `https://<host>/` |
| Back-channel logout URL | `https://<host>/auth/backchannel-logout` |
| Scopes | `openid profile` (no `offline_access`) |
| Claims | `https://login.bcc.no/claims/personUid` must be in the ID token |

`<host>` is the gateway host name of the environment (for example `<app>.sandbox.bcc.no`). The authority is `https://login.sandbox.bcc.no` for sandbox and `https://login.bcc.no` for prod (already in `oidc_by_environment` in `infra/cloudrun_api.tf`). Ask the platform team whether the client supports PAR; the app uses it when it is available.

**Client id.** The client id is not a secret. Replace `SET-OIDC-CLIENT-ID` for the environment in `oidc_by_environment` in `infra/cloudrun_api.tf` and merge. Until the placeholder is replaced the apply stops with a precondition message.

**Client secret.** The value stays out of Terraform state and GitHub. Create the secret and its first version by hand, with a project role that may manage Secret Manager (the Secret Manager API is enabled by the first apply; if the command says it is not, enable it first with `gcloud services enable secretmanager.googleapis.com --project "$PROJECT"`). Take `PROJECT` from `GCP_PROJECT_ID` in the `sandbox` environment. `read -rs` keeps the value out of the shell history and the terminal:

```bash
PROJECT=<GCP_PROJECT_ID of the sandbox environment>
gcloud secrets create <app>-oidc-client-secret --project "$PROJECT" --replication-policy=automatic
read -rs -p "Client secret: " OIDC_CLIENT_SECRET; echo
printf '%s' "$OIDC_CLIENT_SECRET" | gcloud secrets versions add <app>-oidc-client-secret --project "$PROJECT" --data-file=-
unset OIDC_CLIENT_SECRET
```

The secret id must stay `<app>-oidc-client-secret` (`infra/secrets.tf`). Terraform only reads it and grants the runtime account access to this one secret. To rotate: add a new version the same way, then roll out a new revision (the service reads `latest` at instance start).

**The collector's key** needs no step: Terraform writes it from the org secret, write-only.

**Data Protection key.** The apply creates the key ring `<app>-<env>` and the key `data-protection` (90-day rotation) in Cloud KMS ([ADR 0021](../adrs/0021-data-protection-keys-in-postgres-with-kms.md)). The key has `prevent_destroy`: Terraform refuses to destroy it, because GCP never fully deletes key rings and keys and the stored Data Protection keys (and so every session) would be lost. A `destroy` of the environment fails on it until it is removed from state on purpose.

## 4. Apply the infrastructure

The gateway route (step 5) needs the backends, and the smoke test of **Deploy** needs the route and `APP_BASE_URL` (step 6). So the order for a new environment is: apply the infrastructure here, then the route, then `APP_BASE_URL`, then the first deploy. Run **Infrastructure** (`infra.yml`, environment `sandbox`, `apply`). A push to `main` before step 6 would fail at the smoke test, after the rollout.

The apply creates Artifact Registry, the runtime service account, shared database access, the Data Protection key, the Pub/Sub topic and subscriptions, the Cloud Scheduler job, the Cloud Run service with the Cloud SQL Auth Proxy sidecar, the assets bucket and the gateway backends. The Artifact Registry cleanup policies start in dry run (`cleanup_policy_dry_run = true` in `infra/artifact_registry.tf`, standard 03.19): the first apply only reports what they would delete. After reading that report in the Cloud Logging audit log of the repository, set it to `false` in a follow-up change.

The first **Deploy** (step 6) then pushes the image, applies the database schema (`db-apply`), uploads the assets and rolls out. Its smoke test expects an anonymous `GET /` to answer 302 to the identity provider and an anonymous `GET /api/me` to answer 401.

Membership in the database admin group can take a few minutes to reach Cloud SQL. If the schema step fails to log in on the first run, re-run the workflow.

The database starts empty: the seeder only runs locally, in E2E and in previews, so the notes page shows its empty state in sandbox.

The bucket backend grants the load balancer's service agent read access (`grant_object_access`). On a project's first backend bucket that service agent may not exist yet and the apply fails; run **Infrastructure** (`infra.yml`, `apply`) again.

## 5. Gateway route

Only after step 4 has created both backends (standard 26.5): open a PR in `bcc-code/bcc-platform-gateway` that adds `routes/<app>.yaml`. Take the project id from `GCP_PROJECT_ID` in the `sandbox` environment.

```yaml
# routes/<app>.yaml
#
# <App> (bcc-code/<repo>). The filename is the `app` input of the gateway-backend modules in that repo's
# infra/gateway_backend.tf.

environments:
  sandbox:
    project_id: <GCP_PROJECT_ID of the sandbox environment>

backends:
  api:
    type: cloud_run # bs-<app>-api-<env>, infra/gateway_backend.tf
  assets:
    type: backend_bucket # bb-<app>-assets-<env>, same file

hosts:
  - name: web
    hostnames:
      sandbox: [<app>.sandbox.bcc.no]
    # The API serves the SPA shell (index.html), /api and /health. Only the content-hashed bundles come from the bucket,
    # stored under assets/ at the path the SPA requests, so no rewrite.
    default_backend: api
    routes:
      - paths: ["/assets", "/assets/*"]
        backend: assets
```

`sandbox.bcc.no` is a zone the sandbox gateway tier manages, so the DNS record and the certificate follow from the file; there is no manual DNS step. The platform team runs the gateway plan and apply. The certificate can take a while to become `ACTIVE`. Do not route `/internal/**` through the gateway.

## 6. Point the smoke test at the gateway and deploy

Set `public_hostname_by_environment` in `infra/monitoring.tf` (so the `/health` uptime check exists) and the environment variable `APP_BASE_URL` = `https://<host>` in the `sandbox` environment, then run **Deploy** (`deploy.yml`) manually for `sandbox`, or push to `main`. The smoke test goes through the gateway. Check in the browser that the page loads and its `/assets/*` requests are answered by the bucket (response header `x-goog-*`, or `Age` from the CDN on a repeat).

## 7. Check that ingress is locked to the gateway

Ingress is `INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER` from the first apply (`api_ingress_by_environment` in `infra/cloudrun_api.tf`, standard 26.7). Check that the `run.app` URL answers 404 from a browser while the gateway hostname works. Pub/Sub push and Cloud Scheduler reach the `run.app` URL from inside the project and keep working ([ADR 0023](../adrs/0023-pubsub-for-internal-messages-and-bcc-pubsub-for-events.md)).

## 8. Check messaging

After the first successful deploy, run the manual checks in the messaging section of [infra/README.md](../../infra/README.md): one durability pass by hand, one message end to end, and a call to `/internal/*` without a token that must answer 401 or 403.

## Later

- For prod: steps 1 to 8 with `prod`. Prod infrastructure is not applied by `deploy.yml`: run **Infrastructure** (`infra.yml`, environment `prod`, `apply`) first, and again whenever a release changes `infra/`. The precondition in `infra/cloudrun_api.tf` refuses public ingress there, so prod starts locked to the gateway and its route must exist before its smoke test can pass (set `APP_BASE_URL` first). Prod also needs `ALERT_EMAILS_PROD` and the checklist in [go-live.md](go-live.md). After the first `prod` approval, `deploy-prod` pushes the tarball that sandbox ran to the prod registry, stops if the digest differs, and tags a release.
- For previews: the landing zone's `dev` environment and `infra/preview-shared/`, see [pr-previews](../pr-previews.md).
