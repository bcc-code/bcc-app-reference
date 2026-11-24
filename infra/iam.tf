resource "google_service_account" "api" {
  account_id   = "sa-${local.app}-api-${local.env}"
  display_name = "${local.app}-api (${local.env})"
}

# Created by the landing zone and bound to this repository's GitHub environment through Workload Identity Federation.
data "google_service_account" "cicd" {
  account_id = "sa-gha-cicd"
}

# The runtime account writes logs. Traces and metrics go to the BCC monitoring collector, which writes them to Cloud Trace
# and Managed Prometheus with its own identity, so the account has no Cloud Trace or Monitoring role. Its access
# to the shared database comes from the access group (database.tf), and its access to the Data Protection key, the
# secrets and the messages topic is granted on those resources only (kms.tf, secrets.tf, pubsub.tf).
locals {
  api_runtime_roles = [
    "roles/logging.logWriter", # structured logs
  ]
}

resource "google_project_iam_member" "api_runtime" {
  for_each = toset(local.api_runtime_roles)
  project  = local.project_id
  role     = each.value
  member   = "serviceAccount:${google_service_account.api.email}"
}

# Deploying a revision (gcloud run services update, and Terraform's own updates) is checked as actAs on the runtime
# account. Granted on this account only, not project-wide (standard 03.13).
resource "google_service_account_iam_member" "cicd_acts_as_runtime" {
  service_account_id = google_service_account.api.name
  role               = "roles/iam.serviceAccountUser"
  member             = "serviceAccount:${data.google_service_account.cicd.email}"
}

# deploy.yml pushes images as sa-gha-cicd (project role artifactregistry.writer from the landing zone), so the
# repository needs no binding here.
