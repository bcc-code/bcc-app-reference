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
- Not applied against a real GCP project. Roles and the monitoring filters are checked by `validate` for syntax only.
