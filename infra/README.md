# Infrastructure

Terraform for the app in GCP, on the BCC landing zone, one file per concern (standard 03). One root module, applied once per
environment (`sandbox`, `prod`), each in its own GCP project with its own state. The landing zone provides the project, the CI
account `sa-gha-cicd`, the Terraform state and the shared Cloud SQL instance; this Terraform only reads them. See
[ADR 0024](../docs/adrs/0024-wolverine-serverless-with-outbox-and-pubsub-push.md) for the messaging design.

`template` in resource names is replaced by the app's slug when `scripts/init-app.ps1` initialises a copy (`app` in `main.tf`).

| File | Holds |
|---|---|
| `terraform.tf` | Azure backend (state), Terraform and provider versions |
| `variables.tf` | `props` and `deployment_props`, set by the platform workflow; the monitoring collector's endpoint and key, passed by `deploy.yml` and `infra.yml` |
| `main.tf` | locals from the props, the provider, enabled project APIs |
| `artifact_registry.tf` | `ar-<app>-<env>` with cleanup policies (90 days, keep the 20 newest) |
| `iam.tf` | runtime service account `sa-<app>-api-<env>` and who may act as it |
| `cloudrun_api.tf` | Cloud Run service `<app>-api-<env>`, with the Cloud SQL Auth Proxy sidecar; per-environment scaling, ingress and sign-in client |
| `kms.tf` | key ring `<app>-<env>` and the Data Protection key (90-day rotation, `prevent_destroy`), usable by the runtime account |
| `secrets.tf` | reads the OIDC client secret (created by hand, no value in state), writes the monitoring collector's auth header write-only (no value in state), and grants the runtime account access to each |
| `pubsub.tf` | messages topic, push subscription to `/internal/pubsub/...`, dead-letter topic and inspect subscription, push service account |
| `scheduler.tf` | scheduler service account and the durability pass job (every minute) |
| `monitoring.tf` | `alert_emails`, email channels, alert policies, the uptime check and the log-based metrics, each alert linking a runbook |
| `database.tf` | membership of the runtime account and `sa-gha-cicd` in the shared database's admin group |
| `bucket_assets.tf` | private bucket `<project_id>-assets` for hashed frontend bundles |
| `gateway_backend.tf` | backend service and backend bucket for the platform gateway |

## Preview roots

Two more roots serve the PR previews ([docs/pr-previews.md](../docs/pr-previews.md)). They are separate Terraform roots with
their own state, applied to the landing zone's `dev` environment, not to sandbox.

- `infra/preview-shared/`: what all previews use, applied once by hand through `infra.yml` (environment `dev`, component
  `main`). See its [README](preview-shared/README.md).
- `infra/preview/`: one workspace per PR (component `pr<N>`), applied and destroyed by `.github/workflows/preview.yml`. It
  holds the API and the OIDC mock as Cloud Run services (pinned to one instance), the runtime accounts (the API's is a member
  of the database group), the Pub/Sub topic, the durability-pass job and the per-PR secrets. The API gets its own Postgres
  schema, `Persistence__Schema=pr<N>`. The test data comes from the seeder, which runs from its image on the CI runner after
  the apply; Terraform does not create it.

The CI `infra` job runs `fmt`, `init -backend=false` and `validate` on all three roots.

## How it is applied

Only by CI, through the platform workflow `bcc-code/bcc-platform-deploy/.github/workflows/terraform.yml@main`. It is not pinned
to a commit SHA: the platform's Workload Identity Federation trusts the caller only when the called workflow runs from
`refs/heads/main`, so a SHA pin makes authentication fail. `.github/workflows/deploy.yml` applies sandbox before every rollout
(so Terraform and the deploy never race), and `.github/workflows/infra.yml` runs plan, apply or destroy by hand. Both use the
concurrency group `deploy-<env>`. State is in the platform's Azure storage account, selected by the platform workflow. The
variables `props` and `deployment_props` come from the GitHub environment variable `PROPS` and the workflow.
`otel_exporter_otlp_endpoint` and `otel_exporter_otlp_auth_key` come from the org-level `OTEL_EXPORTER_OTLP_ENDPOINT_DEV/_PROD`
and `OTEL_EXPORTER_OTLP_AUTH_KEY_DEV/_PROD`, which `deploy.yml` and `infra.yml` both pass. If an org value is missing, the
workflows pass it empty and no telemetry reaches the collector. `alert_emails` is a JSON list in the repository variable
`ALERT_EMAILS_SANDBOX` or `ALERT_EMAILS_PROD` (for example `["ops@example.org"]`), which the workflows pass when it is set; unset
means empty, and prod refuses an empty list.

Validate locally without credentials or state:

```bash
terraform fmt -check -recursive infra
terraform -chdir=infra init -backend=false
terraform -chdir=infra validate
```

`.github/workflows/ci.yml` runs the same commands. `init` downloads the gateway modules from a private GitHub repository, so it
needs a git credential that can read `bcc-code/bcc-platform-gateway-tf-modules`. Do not run `plan` or `apply` from a laptop.

`.terraform.lock.hcl` is committed. After a provider bump, regenerate it for the platforms in use (the platform runner is
`ubuntu-24.04-arm`):

```bash
terraform -chdir=infra providers lock -platform=linux_amd64 -platform=linux_arm64 -platform=windows_amd64
```

## Per-environment settings

What differs between `sandbox` and `prod` is in `locals` maps keyed by environment, so a reviewer sees it in one place:

- `api_scaling_by_environment`, `api_ingress_by_environment` and `oidc_by_environment` in `cloudrun_api.tf`;
- `public_hostname_by_environment` in `monitoring.tf` (the gateway host name, for the uptime check).

The OIDC client ids are placeholders (`SET-OIDC-CLIENT-ID`) and a precondition fails the apply until a real id is entered. Prod
must use `INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER` and at least one `alert_emails` entry, both enforced by preconditions.

## Not in this state

- The GCP project, `sa-gha-cicd`, its Workload Identity Federation binding, the Terraform state storage and the GitHub
  environment variables (`PROPS`, `GCP_PROJECT_ID`, `GCP_REGION`, `AZURE_*`). The landing zone creates them
  (`provision-project.yml`). `iam.tf` reads `sa-gha-cicd` with a data source.
- The gateway route `routes/<app>.yaml` and DNS. They live in `bcc-platform-gateway`.
- The OpenTelemetry collector and the central monitoring project. They live in `bcc-monitoring`; this state only enables Cloud
  Trace, where the collector writes our traces.
- The Cloud Run image. `deploy.yml` sets it by digest; Terraform ignores it after the first apply.
- The objects in the assets bucket. `deploy.yml` uploads them.
- The Cloud SQL instance, the database and its owner role. The landing zone creates them on the platform's shared instance
  (`use-shared-db: true`) and publishes `shared_db.db_name`, `shared_db.connection_name` and `shared_db.admin_group_id` in
  `PROPS`.
- The database schema. `deploy.yml` applies it with the image's schema command before the rollout.

The OIDC client secret `<app>-oidc-client-secret` is created by hand with its first version
([first-deploy runbook](../docs/runbooks/first-deploy.md)); Terraform only reads it, so no secret value passes through state. The
monitoring collector's key is the exception: nobody on the team can read the org secret, so Terraform writes
`<app>-otel-exporter-otlp-headers` itself, write-only, and a rotated key gets a new version on the next apply. The database
uses IAM authentication, so it has no password.

## Order for a new environment

1. Run `provision-project.yml` for the environment (once per environment). It must run with `use-shared-db: true` before the
   first apply: `cloudrun_api.tf` and `database.tf` read `shared_db` from `PROPS`.
2. Create the OIDC client secret and enter the client id in `oidc_by_environment`.
3. Apply this Terraform (`infra.yml`, or the first `deploy.yml` run). This creates the backends the gateway refers to. The first
   apply of `gateway_backend_assets` can fail on the object-access grant because the load balancer's service agent is created
   with the project's first backend bucket; apply again.
4. Merge the route PR in `bcc-platform-gateway`. The route can only reference backends that already exist. Then set
   `public_hostname_by_environment` and the GitHub environment variable `APP_BASE_URL` to the gateway URL so the smoke test
   goes through the gateway.

Before removing or renaming a backend, remove its route first.

## Serverless messaging

The API runs Wolverine with `DurabilityMode.ServerlessWithOutbox`: a handler writes outgoing integration messages to the
Postgres outbox in its transaction and publishes them to Pub/Sub after the commit; Pub/Sub pushes each message back to the
API, so handlers run inside a request (Cloud Run only gives an instance CPU while it serves one). A Cloud Scheduler job sends
outbox rows whose inline send failed. The app uses this only with `Messaging__UsePubSub=true`, which `cloudrun_api.tf` sets.
Runbooks: [pubsub-dead-letters](../docs/runbooks/pubsub-dead-letters.md), [durability-pass](../docs/runbooks/durability-pass.md).

| Resource | Name | Purpose |
|---|---|---|
| Topic | `<app>-messages-<env>` | The one topic the API publishes to |
| Push subscription | `<app>-messages-push-<env>` | `POST <api>/internal/pubsub/<subscription>`, OIDC token, ack deadline 300 s (equal to the Cloud Run request timeout), retry backoff 10 to 600 s, 10 delivery attempts, then dead letter |
| Topic | `<app>-dead-letter-<env>` | Messages that failed 10 deliveries |
| Pull subscription | `<app>-dead-letter-inspect-<env>` | Keeps them 7 days for inspection; nothing consumes it |
| Scheduler job | `<app>-durability-pass-<env>` | Every minute (time zone Europe/Oslo), `POST <api>/internal/durability/pass`, attempt deadline 60 s, no retry (the next minute is the retry) |
| Service accounts | `sa-<app>-push-<env>`, `sa-<app>-sched-<env>` | Identities of the push and the scheduler tokens |

The delivery limit of 10 is a backstop: a failed handler answers 500 and Pub/Sub redelivers; Wolverine dead-letters and
acknowledges on delivery 5, so a message reaches the Pub/Sub dead-letter topic only when the request itself keeps failing
(crash, timeout).

Environment variables of the API (`local.api_env`): `Messaging__UsePubSub`, `Messaging__ProjectId` (project id),
`Messaging__Topic` (topic name), `Internal__PubSubAudience` and `Internal__SchedulerAudience` (fixed strings
`https://<service name>/internal/pubsub` and `.../internal/scheduler`), `Internal__PubSubServiceAccount` and
`Internal__SchedulerServiceAccount` (the account emails). `Internal__AllowAll` is never set.

IAM grants, all on the resource named:

| Grant | On | Why |
|---|---|---|
| `roles/pubsub.publisher` to the API account | messages topic | Publish the outbox |
| `roles/iam.serviceAccountTokenCreator` to the Pub/Sub service agent, and to the Cloud Scheduler service agent | push account, scheduler account respectively | The agents mint the OIDC tokens |
| `roles/pubsub.publisher` to the Pub/Sub service agent | dead-letter topic | Forward undeliverable messages |
| `roles/pubsub.subscriber` to the Pub/Sub service agent | push subscription | Acknowledge a message it dead-lettered |

The service allows `allUsers` as invoker because the gateway sends no identity token; ingress limits who can reach it, and
`/internal/**` validates the token's audience and account in the app. Pub/Sub push and Cloud Scheduler therefore need no
invoker role of their own. Do not route `/internal/**` through the platform gateway.

Alerts (`monitoring.tf`, all with a runbook link): 5xx ratio, p95 latency, the `/health` uptime check, messages in the
dead-letter inspect subscription, oldest unacked message on the push subscription above 15 minutes, Wolverine's "was moved to
the error queue" log line, durability pass failures and Cloud Scheduler job failures. The log-based metrics depend on log
wording (standard 22 rule 14): they match Wolverine's own message templates (listed in `monitoring.tf`), so check them when
Wolverine is upgraded.

Notes:

- The API database connection goes through the Cloud SQL Auth Proxy straight to Cloud SQL, with no managed connection
  pooling. The durability pass takes a session-level advisory lock, which is a problem behind a pooler in transaction mode.
  If managed pooling is turned on for the shared instance, give the message store a direct connection first.
- Push subscriptions are created with the service URL, so the first apply creates the Cloud Run service with the placeholder
  image. Nothing publishes before the first deploy, so no push fails in the meantime.

Manual checks after the first apply and deploy (sandbox):

1. `gcloud scheduler jobs run <app>-durability-pass-sandbox --location <region>`, then read the API log: the pass answers 200,
   and the Cloud Scheduler log shows no error.
2. Trigger an action that publishes a message and check that it arrives: `gcloud pubsub subscriptions describe
   <app>-messages-push-sandbox` shows the push endpoint, and the API log shows the handler running.
3. Call `/internal/durability/pass` and `/internal/pubsub/<subscription>` without a token, and with a token of the wrong
   audience or account: both must answer 401 or 403.

## Guard rails

- Prod requires `alert_emails`, the internal load balancer as ingress and a real OIDC client id (preconditions in
  `cloudrun_api.tf`).
- The KMS key has `prevent_destroy`, and the assets bucket refuses `force_destroy`. To retire an environment, remove them in a
  separate reviewed commit.
- `ignore_changes` is used for the container image, `client`, `client_version` and labels. The deploy workflow owns the image.
- Cloud Run ingress is the internal load balancer from the first apply, so `APP_BASE_URL` (the gateway URL) must be set before the
  first deploy: its smoke test fails with an error otherwise.

## Open items

- Dashboards: only alert policies exist. Add a dashboard when the Grafana decision (standard 22 rule 15) is made.
- Not applied against a real GCP project. Roles and the monitoring filters are checked by `validate` for syntax only.
