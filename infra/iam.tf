# One service account per runtime and per caller (standard 03, rule 10). Account ids are limited to 30 characters; with
# a long app slug shorten the "sa-<slug>-" prefix.

# --- Runtimes ---------------------------------------------------------------------------------------------------------

resource "google_service_account" "api" {
  account_id   = "sa-${local.app}-api-${local.env}"
  display_name = "${local.app} api (${local.env})"
}

resource "google_service_account" "migrate" {
  account_id   = "sa-${local.app}-migrate-${local.env}"
  display_name = "${local.app} database migration job (${local.env})"
}

locals {
  # Project-level roles that have no resource-level form. Secret and KMS access is bound per resource in secrets.tf and kms.tf.
  runtime_roles = {
    api = [
      "roles/cloudsql.client",       # connect through the Auth Proxy sidecar
      "roles/cloudsql.instanceUser", # log in as an IAM database user
      "roles/cloudtrace.agent",
      "roles/logging.logWriter",
      "roles/monitoring.metricWriter",
    ]
    migrate = [
      "roles/cloudsql.client",
      "roles/cloudsql.instanceUser",
      "roles/logging.logWriter",
    ]
  }
  runtime_service_accounts = {
    api     = google_service_account.api
    migrate = google_service_account.migrate
  }
}

resource "google_project_iam_member" "runtime" {
  for_each = merge([
    for sa, roles in local.runtime_roles : {
      for role in roles : "${sa}/${role}" => { email = local.runtime_service_accounts[sa].email, role = role }
    }
  ]...)
  project = var.project_id
  role    = each.value.role
  member  = "serviceAccount:${each.value.email}"
}

# --- GitHub Actions: Workload Identity Federation (no service account keys, standard 03 rule 9) ------------------------

resource "google_iam_workload_identity_pool" "github" {
  depends_on                = [google_project_service.services]
  workload_identity_pool_id = "github"
  display_name              = "GitHub Actions"
}

resource "google_iam_workload_identity_pool_provider" "github" {
  workload_identity_pool_id          = google_iam_workload_identity_pool.github.workload_identity_pool_id
  workload_identity_pool_provider_id = "github"
  display_name                       = "GitHub OIDC"
  attribute_mapping = {
    "google.subject"        = "assertion.sub"
    "attribute.repository"  = "assertion.repository"
    "attribute.environment" = "assertion.environment"
    "attribute.ref"         = "assertion.ref"
    "attribute.event_name"  = "assertion.event_name"
  }
  # Tokens from any other repository are rejected before a service account binding is evaluated.
  attribute_condition = "assertion.repository == \"${var.github_repository}\""
  oidc {
    issuer_uri = "https://token.actions.githubusercontent.com"
  }
}

locals {
  wif_pool = google_iam_workload_identity_pool.github.name
}

# Deploy: pushes the image, runs the migration job and rolls out a revision. Only jobs that run in the GitHub environment
# named like this environment can use it. Set "deployment branches: main" and required reviewers on that environment,
# otherwise a workflow file on any branch can claim the environment (docs/development.md, Repository settings).
resource "google_service_account" "deploy" {
  account_id   = "sa-${local.app}-deploy-${local.env}"
  display_name = "${local.app} deploy from GitHub Actions (${local.env})"
}

# Terraform apply. Broad by nature (it creates the resources above), so it is reachable only from the GitHub
# environment of this environment, which carries the approval rule.
resource "google_service_account" "terraform" {
  account_id   = "sa-${local.app}-tf-${local.env}"
  display_name = "${local.app} terraform apply from GitHub Actions (${local.env})"
}

# Terraform plan on pull requests: read-only. PR workflows have no GitHub environment, so this account is bound to the
# repository instead and must not be able to change anything.
resource "google_service_account" "plan" {
  account_id   = "sa-${local.app}-plan-${local.env}"
  display_name = "${local.app} terraform plan from GitHub Actions (${local.env})"
}

resource "google_service_account_iam_member" "deploy_wif" {
  service_account_id = google_service_account.deploy.name
  role               = "roles/iam.workloadIdentityUser"
  member             = "principalSet://iam.googleapis.com/${local.wif_pool}/attribute.environment/${local.env}"
}

resource "google_service_account_iam_member" "terraform_wif" {
  service_account_id = google_service_account.terraform.name
  role               = "roles/iam.workloadIdentityUser"
  member             = "principalSet://iam.googleapis.com/${local.wif_pool}/attribute.environment/${local.env}"
}

resource "google_service_account_iam_member" "plan_wif" {
  service_account_id = google_service_account.plan.name
  role               = "roles/iam.workloadIdentityUser"
  member             = "principalSet://iam.googleapis.com/${local.wif_pool}/attribute.repository/${var.github_repository}"
}

# Deploy account: act as the two runtime accounts only (never project-wide, standard 03 rule 13). Cloud Run checks
# iam.serviceAccounts.actAs when the deploy changes a revision or executes the job.
resource "google_service_account_iam_member" "deploy_acts_as_runtime" {
  for_each           = local.runtime_service_accounts
  service_account_id = each.value.name
  role               = "roles/iam.serviceAccountUser"
  member             = local.deploy_sa_member
}

resource "google_project_iam_member" "deploy_service_usage" {
  project = var.project_id
  role    = "roles/serviceusage.serviceUsageConsumer" # gcloud calls APIs with this project as the quota project
  member  = local.deploy_sa_member
}

resource "google_artifact_registry_repository_iam_member" "deploy_writer" {
  repository = google_artifact_registry_repository.main.name
  location   = google_artifact_registry_repository.main.location
  role       = "roles/artifactregistry.writer"
  member     = local.deploy_sa_member
}

# roles/run.developer on the two resources, not on the project: update the service image, update and execute the job.
resource "google_cloud_run_v2_service_iam_member" "deploy_api" {
  name     = google_cloud_run_v2_service.api.name
  location = google_cloud_run_v2_service.api.location
  role     = "roles/run.developer"
  member   = local.deploy_sa_member
}

resource "google_cloud_run_v2_job_iam_member" "deploy_migrate" {
  name     = google_cloud_run_v2_job.migrate.name
  location = google_cloud_run_v2_job.migrate.location
  role     = "roles/run.developer"
  member   = local.deploy_sa_member
}

# objectUser, not objectAdmin: the upload adds objects and never needs to change bucket settings (standard 26 rule 11).
resource "google_storage_bucket_iam_member" "deploy_assets" {
  bucket = google_storage_bucket.assets.name
  role   = "roles/storage.objectUser"
  member = local.deploy_sa_member
}

# Terraform apply account. Each role is the admin role of a service this configuration manages. It cannot be narrower
# without a custom role per resource type. Known risk: whoever can run a job in this environment can change anything
# below, so keep required reviewers on the GitHub environment.
resource "google_project_iam_member" "terraform" {
  for_each = toset([
    "roles/artifactregistry.admin",
    "roles/cloudkms.admin",
    "roles/cloudsql.admin",
    "roles/iam.serviceAccountAdmin",
    "roles/iam.workloadIdentityPoolAdmin",
    "roles/monitoring.admin",
    "roles/resourcemanager.projectIamAdmin", # project-level bindings in this file
    "roles/run.admin",
    "roles/secretmanager.admin",
    "roles/serviceusage.serviceUsageAdmin",
    "roles/storage.admin",
  ])
  project = var.project_id
  role    = each.value
  member  = local.terraform_sa_member
}

# Terraform needs actAs on every account it attaches to a Cloud Run resource.
resource "google_service_account_iam_member" "terraform_acts_as_runtime" {
  for_each           = local.runtime_service_accounts
  service_account_id = each.value.name
  role               = "roles/iam.serviceAccountUser"
  member             = local.terraform_sa_member
}

# State bucket: apply reads and writes, plan only reads. Plan runs with -lock=false because it must not write to the bucket.
resource "google_storage_bucket_iam_member" "state_terraform" {
  bucket = var.state_bucket
  role   = "roles/storage.objectUser"
  member = local.terraform_sa_member
}

resource "google_storage_bucket_iam_member" "state_plan" {
  bucket = var.state_bucket
  role   = "roles/storage.objectViewer"
  member = local.plan_sa_member
}

# Plan reads every resource this configuration manages. Viewer does not cover all of them (for example IAM policies on
# secrets); add the missing read permissions if a plan reports 403.
resource "google_project_iam_member" "plan_viewer" {
  project = var.project_id
  role    = "roles/viewer"
  member  = local.plan_sa_member
}

resource "google_project_iam_member" "plan_security_reviewer" {
  project = var.project_id
  role    = "roles/iam.securityReviewer"
  member  = local.plan_sa_member
}

# --- Preview environments (sandbox only, standard 07) ---------------------------------------------------------------------

# Previews run PR code, so this account is bound to its own GitHub environment "preview" (which has no branch restriction)
# and exists in sandbox only. KNOWN RISK: it can create service accounts and Cloud Run services in the sandbox project, and
# anyone who can open a PR in the repository can run a job as it. Sandbox holds generated test data only. Narrow it with IAM
# conditions when previews are adopted (standard 07 rule 21).
resource "google_service_account" "preview" {
  count        = local.env == "sandbox" ? 1 : 0
  account_id   = "sa-${local.app}-preview-${local.env}"
  display_name = "${local.app} PR previews from GitHub Actions (${local.env})"
}

resource "google_service_account_iam_member" "preview_wif" {
  count              = local.env == "sandbox" ? 1 : 0
  service_account_id = google_service_account.preview[0].name
  role               = "roles/iam.workloadIdentityUser"
  member             = "principalSet://iam.googleapis.com/${local.wif_pool}/attribute.environment/preview"
}

resource "google_project_iam_member" "preview" {
  for_each = local.env == "sandbox" ? toset([
    "roles/run.admin",               # create and delete the per-PR services and jobs
    "roles/iam.serviceAccountAdmin", # create and delete the per-PR runtime account
    "roles/serviceusage.serviceUsageConsumer",
  ]) : toset([])
  project = var.project_id
  role    = each.value
  member  = "serviceAccount:${google_service_account.preview[0].email}"
}

# Acting as the per-PR runtime accounts only: the condition limits the project-wide binding to accounts named sa-<app>-pr<N>
# (standard 03 rule 13: serviceAccountUser must not be granted project-wide without a limit).
resource "google_project_iam_member" "preview_acts_as_pr_accounts" {
  count   = local.env == "sandbox" ? 1 : 0
  project = var.project_id
  role    = "roles/iam.serviceAccountUser"
  member  = "serviceAccount:${google_service_account.preview[0].email}"
  condition {
    title      = "only-preview-runtime-accounts"
    expression = "resource.name.startsWith(\"projects/${var.project_id}/serviceAccounts/sa-${local.app}-pr\")"
  }
}

resource "google_artifact_registry_repository_iam_member" "preview_writer" {
  count      = local.env == "sandbox" ? 1 : 0
  repository = google_artifact_registry_repository.main.name
  location   = google_artifact_registry_repository.main.location
  role       = "roles/artifactregistry.writer"
  member     = "serviceAccount:${google_service_account.preview[0].email}"
}

resource "google_storage_bucket_iam_member" "state_preview" {
  count  = local.env == "sandbox" ? 1 : 0
  bucket = var.state_bucket
  role   = "roles/storage.objectUser"
  member = "serviceAccount:${google_service_account.preview[0].email}"
}
