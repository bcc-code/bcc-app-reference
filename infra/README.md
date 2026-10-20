# Infrastructure

Terraform for the GCP resources of the app, one file per concern (standard 03). One root module, applied once per
environment (`sandbox`, `prod`), each in its own GCP project with its own state. Per-environment differences are in
`local.env_config` in `main.tf`; the `*.tfvars.example` files only hold project-specific inputs.

| File | Contents |
|---|---|
| `terraform.tf` | Provider pins, GCS backend (partial configuration) |
| `main.tf` | Per-environment settings, providers, enabled APIs |
| `cloudrun_api.tf` | API service, Auth Proxy sidecar, probes on `/alive` (liveness) and TCP (startup) |
| `cloudrun_migrate.tf` | Cloud Run job that applies the schema before a rollout |
| `database.tf` | Cloud SQL Postgres with IAM authentication, IAM database users |
| `iam.tf` | One service account per runtime and caller, GitHub Workload Identity Federation |
| `pubsub.tf` | Messages topic, push subscription, dead-letter topic and inspect subscription, push service account, topic IAM |
| `scheduler.tf` | Cloud Scheduler job for the durability pass, scheduler service account |
| `kms.tf` | Key for the Data Protection keys (90-day rotation, `prevent_destroy`) |
| `secrets.tf` | Secret Manager containers, no values |
| `storage.tf` | Private assets bucket for the content-hashed frontend bundles |
| `gateway_backend.tf` | API and bucket backends for the platform gateway (standard 26) |
| `artifact_registry.tf` | Image repository with cleanup policies (dry run first) |
| `monitoring.tf` | Alert policies and an uptime check, each linking a runbook |
| `preview/` | Starting point for per-PR previews (standard 07), own root module |

## Deviations from the BCC platform setup

Standard 03 describes applying through the shared `bcc-code/bcc-platform-deploy` workflows, with project data from
`props` and `deployment_props`, and a state backend owned by the platform. This template does not depend on that
platform, so it uses plain variables, a GCS backend and its own `infra.yml`. If your project is provisioned by the
landing zone:

1. Replace the variables `project_id`, `region`, `environment` with `local.props` as in vote-rewrite's `infra/main.tf`.
2. Replace `infra.yml` with a call to `bcc-code/bcc-platform-deploy/.github/workflows/terraform.yml`. That repository has
   no release tags (checked 2026-10-19), so pin a commit SHA, not `@main`.
3. Use the shared database from `props.shared_db` instead of `database.tf`, and the platform's `sa-gha-cicd` instead of the
   accounts in `iam.tf`.

## Bootstrapping a new environment

Do this once per environment, as a project admin. It is the only time Terraform runs outside CI (standard 03 rule 4).

1. Create the GCP project, a GCS bucket for the state (versioning on, access limited to infra admins), and enable the
   APIs `cloudresourcemanager.googleapis.com`, `serviceusage.googleapis.com`, `iam.googleapis.com`. The rest is enabled by
   `main.tf`.
2. Copy `sandbox.tfvars.example` or `prod.tfvars.example` to `<env>.tfvars` and fill in the placeholders. The file holds no
   secrets, but keep it out of the repository unless the team decides otherwise.
3. Apply locally with your own credentials:

   ```bash
   terraform -chdir=infra init -backend-config="bucket=<state bucket>" -backend-config="prefix=template/<env>"
   terraform -chdir=infra apply -var-file=<env>.tfvars
   ```

   The first apply of the gateway backend bucket module can fail on the grant for the load balancer's service agent. Apply
   again.
4. Create the GitHub environments `sandbox` and `prod` (Settings, Environments). On both: deployment branches limited to
   `main`. On `prod`: required reviewers. Set the variables from `terraform output`:

   | Variable | Source |
   |---|---|
   | `GCP_PROJECT_ID` | project id |
   | `GCP_REGION` | region |
   | `GCP_WORKLOAD_IDENTITY_PROVIDER` | output `workload_identity_provider` |
   | `GCP_DEPLOY_SERVICE_ACCOUNT` | output `deploy_service_account` |
   | `GCP_TERRAFORM_SERVICE_ACCOUNT` | output `terraform_service_account` |
   | `GCP_PLAN_SERVICE_ACCOUNT` | output `plan_service_account` |
   | `GCP_ARTIFACT_REPOSITORY` | output `artifact_registry_repository` |
   | `TF_STATE_BUCKET` | state bucket |
   | `TF_VARS` | the content of `<env>.tfvars`, as one multi-line variable |
   | `PUBLIC_BASE_URL` | `https://<public_hostname>` |

   The environment `preview` (sandbox only, no branch restriction, used by `preview.yml`) gets the sandbox values of the
   first six variables, with `GCP_PREVIEW_SERVICE_ACCOUNT` (output `preview_service_account`) instead of
   `GCP_DEPLOY_SERVICE_ACCOUNT`, plus `TF_STATE_BUCKET`. See [../docs/pr-previews.md](../docs/pr-previews.md).

   The plan job on pull requests has no environment. It reads the same variables from the `sandbox` environment through
   repository-level copies: add `GCP_WORKLOAD_IDENTITY_PROVIDER`, `GCP_PLAN_SERVICE_ACCOUNT`, `TF_STATE_BUCKET` and `TF_VARS`
   (the sandbox values) as repository variables as well.
5. Add the OIDC client secret out of band (see `secrets.tf`).
6. Grant the database privileges below.
7. Register the gateway route (`routes/<app>.yaml` in the platform gateway repository) once the backends exist.
8. From now on CI applies changes: `infra.yml` on pushes to `main` for sandbox, and on manual dispatch for prod.

Prod pulls its image from the sandbox registry by digest. After both environments exist, put the prod deploy account in
`image_reader_members` of sandbox (`sandbox.tfvars.example`) and apply sandbox.

## Database privileges

An IAM database user can log in and nothing else. Before the first migration, an admin connects once (Auth Proxy, as a
Cloud SQL admin user) and runs the following, with the user names from `terraform output`:

```sql
-- migrate user may create objects; the API user may use them
GRANT ALL ON SCHEMA public TO "<migrate_iam_database_user>";
GRANT CONNECT ON DATABASE template TO "<api_iam_database_user>";
GRANT USAGE ON SCHEMA public TO "<api_iam_database_user>";
ALTER DEFAULT PRIVILEGES FOR ROLE "<migrate_iam_database_user>" IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO "<api_iam_database_user>";
ALTER DEFAULT PRIVILEGES FOR ROLE "<migrate_iam_database_user>" IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO "<api_iam_database_user>";
```

Not verified against a real instance. The Marten variant also uses its own schemas (`mt_*` tables in `public` by default);
check where it creates objects and extend the grants. vote-rewrite instead runs `REASSIGN OWNED` after the migration
(`deploy.yml`); that is the alternative if default privileges are not enough.

## State

The state holds no secret values: secrets are containers only (`secrets.tf`). It does contain resource attributes such as
service account emails and the database connection name, so limit the state bucket to infra admins, `sa-<app>-tf-<env>`
(read and write) and `sa-<app>-plan-<env>` (read only; plan runs with `-lock=false`).

## Serverless messaging (Pub/Sub and Cloud Scheduler)

The API runs Wolverine with `DurabilityMode.ServerlessWithOutbox`: a handler writes outgoing integration messages to the
Postgres outbox in its transaction and publishes them to Pub/Sub after the commit; Pub/Sub pushes each message back to the
API, so handlers run inside a request (Cloud Run only gives an instance CPU while it serves one). A Cloud Scheduler job
sends outbox rows whose inline send failed. Runbooks: [pubsub-dead-letters](../docs/runbooks/pubsub-dead-letters.md),
[durability-pass](../docs/runbooks/durability-pass.md).

| Resource | Name | Purpose |
|---|---|---|
| Topic | `template-messages-<env>` | The one topic the API publishes to |
| Push subscription | `template-messages-push-<env>` | `POST <api>/internal/pubsub/messages`, OIDC token, ack deadline 600 s, retry backoff 10 to 600 s, 5 delivery attempts, then dead letter |
| Topic | `template-dead-letter-<env>` | Messages that failed 5 deliveries |
| Pull subscription | `template-dead-letter-inspect-<env>` | Keeps them 7 days for inspection; nothing consumes it |
| Scheduler job | `template-durability-pass-<env>` | Every minute, `POST <api>/internal/durability/pass`, attempt deadline 60 s, no retry (the next minute is the retry), in the API's region |
| Service accounts | `sa-template-pubsub-push-<env>`, `sa-template-scheduler-<env>` | Identities of the push and the scheduler tokens |

Environment variables of the API (`local.api_env`, also on the migration job): `Messaging__PubsubProjectId` (project id),
`Messaging__Topic` (topic name), `Internal__PubSubAudience` and `Internal__SchedulerAudience` (fixed strings
`https://<service name>/internal/pubsub` and `.../internal/scheduler`, also set as the Cloud Run `custom_audiences`),
`Internal__PubSubServiceAccount` and `Internal__SchedulerServiceAccount` (the account emails). `Internal__AllowAll` is never
set. The Cloud Run request timeout is 600 s so a push can use its full ack deadline.

IAM grants, all on the resource named:

| Grant | On | Why |
|---|---|---|
| `roles/pubsub.publisher`, `roles/pubsub.viewer` to the API account | messages topic | Publish the outbox; Wolverine's resource check reads the topic at startup |
| `roles/run.invoker` to the push account, and to the scheduler account | the API service | Both send an identity token. The service also has `allUsers` as invoker (the gateway sends no token), so these two grants are the intended callers rather than a restriction; the restriction is the app's check of the token's audience and account |
| `roles/iam.serviceAccountTokenCreator` to the Pub/Sub service agent, and to the Cloud Scheduler service agent | push account, scheduler account respectively | The agents mint the OIDC tokens |
| `roles/pubsub.publisher` to the Pub/Sub service agent | dead-letter topic | Forward undeliverable messages |
| `roles/pubsub.subscriber` to the Pub/Sub service agent | push subscription | Acknowledge a message it dead-lettered |
| `roles/iam.serviceAccountUser` to the Terraform account | push account, scheduler account | actAs, needed to create an OIDC subscription or job |
| `roles/pubsub.admin`, `roles/cloudscheduler.admin`, `roles/logging.configWriter` to the Terraform account | project | Manage these services and the log-based metrics (see `iam.tf`) |

Alerts (`monitoring.tf`, all with a runbook link): messages in the dead-letter inspect subscription, oldest unacked message
on the push subscription above 15 minutes, Wolverine's "was moved to the error queue" log line, durability pass failures
and Cloud Scheduler job failures. The log-based metrics depend on log wording (standard 22 rule 14): they match Wolverine 6.51.0's own
message templates (listed in `monitoring.tf`), so check them when Wolverine is upgraded.

Notes:

- The API database connection goes through the Cloud SQL Auth Proxy straight to Cloud SQL, with no managed connection
  pooling. The durability pass takes a session-level advisory lock, which is a problem behind a pooler in transaction mode
  (the lock and the unlock can reach different server sessions). If managed pooling is turned on for this instance, give the
  message store a direct connection first.
- `ingress` is `INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER`. Pub/Sub push and Cloud Scheduler calls to the `run.app` URL are
  documented as allowed under this setting, but this has not been tried here: check it with the manual checks below. Do not
  route `/internal/**` through the platform gateway.
- Push subscriptions are created with the service URL, so the first apply creates the Cloud Run service with the placeholder
  image. Nothing publishes before the first deploy, so no push fails in the meantime.

Manual checks after the first apply and deploy (sandbox):

1. `gcloud scheduler jobs run template-durability-pass-sandbox --location <region>`, then read the API log: the pass
   answers 200, and the Cloud Scheduler log shows no error.
2. Trigger an action that publishes a message and check that it arrives: `gcloud pubsub subscriptions describe
   template-messages-push-sandbox` shows the push endpoint, and the API log shows the handler running.
3. Call `/internal/durability/pass` and `/internal/pubsub/messages` without a token, and with a token of the wrong audience
   or account: both must answer 401 or 403.
4. Pause the API's push (for example by returning 500 from a test handler in sandbox) and check that the message reaches
   `template-dead-letter-inspect-sandbox` after 5 attempts and that the alert fires.

## Guard rails

- Prod requires `alert_emails` (precondition in `cloudrun_api.tf`).
- The KMS key, the Cloud SQL instance and the assets bucket have `prevent_destroy`. To retire an environment, remove it in a
  separate reviewed commit.
- `ignore_changes` is used for the container image only. The deploy workflow owns it.
- The Artifact Registry cleanup policies run in dry-run mode. Read the report and compare digests with what live revisions
  use before setting `cleanup_policy_dry_run = false`.
- Cloud Run ingress is the internal load balancer. Change it only after the gateway route is live.

## Validate locally

```bash
terraform fmt -check -recursive infra
terraform -chdir=infra init -backend=false && terraform -chdir=infra validate
terraform -chdir=infra/preview init -backend=false && terraform -chdir=infra/preview validate
```

`init` downloads the gateway modules from a private GitHub repository, so it needs a git credential that can read
`bcc-code/bcc-platform-gateway-tf-modules`.

## Open items

- Environment variable names (`OIDC__*`, `Gcp__DataProtectionKmsKeyName`, `ConnectionStrings__template`) were compared with
  the Marten API project and its AppHost on 2026-10-19. `migrate_args` defaults to the Marten schema command
  (`resources setup`); set it for the EF Core variant.
- Dashboards: only alert policies exist. Add a dashboard when the Grafana decision (standard 22 rule 15) is made.
- Cloud Scheduler is offered in the API's region (checked for europe-west4 by the Privacy app with `gcloud scheduler locations list`); check the region you use.
- Not applied against a real GCP project. Roles and the monitoring filters are checked by `validate` for syntax only.
