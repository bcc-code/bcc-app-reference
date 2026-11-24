locals {
  props      = merge(jsondecode(var.props), jsondecode(var.deployment_props))
  project_id = local.props.gcp.project_id
  region     = local.props.gcp.location

  # The platform workflow derives the workspace from the component name, so it is what keeps one preview's state and
  # resource names apart from another's. For PR 1234 it is "pr1234".
  slug = local.props.component_name

  # Same app name as infra/, so resource names follow one scheme (init-app.ps1 replaces it in both).
  app = "bcc-template"

  api_service      = "${local.app}-api-${local.slug}"
  oidcmock_service = "${local.app}-oidc-${local.slug}"

  # Cloud Run's stable URL form is known before the services exist, so the API and the mock can name each other
  # without a dependency cycle.
  api_url  = "https://${local.api_service}-${data.google_project.main.number}.${local.region}.run.app"
  oidc_url = "https://${local.oidcmock_service}-${data.google_project.main.number}.${local.region}.run.app"

  # Lets the platform find a stray resource and the PR it belongs to.
  labels = {
    template-preview = "true"
    template-pr      = var.pr_number
  }

  # Cloud SQL Auth Proxy sidecar, the same image as infra/cloudrun_api.tf (keep the two in step).
  sql_proxy_image = "gcr.io/cloud-sql-connectors/cloud-sql-proxy:2.26.2@sha256:e8c76356fb586300c63f0d29c8c75cfeeba33c29736f9d4afa2df2d78cd0fa19"
}

provider "google" {
  project               = local.project_id
  region                = local.region
  billing_project       = local.project_id
  user_project_override = true
}

data "google_project" "main" {}

# The shared part (infra/preview-shared) is looked up by name, not through its state, so a preview apply cannot lock or
# change that state.
data "google_kms_key_ring" "shared" {
  name     = "${local.app}-${local.props.app_environment}"
  location = local.region
}

data "google_kms_crypto_key" "data_protection" {
  name     = "data-protection"
  key_ring = data.google_kms_key_ring.shared.id
}

# Created by the landing zone; the same account that applies this root.
data "google_service_account" "cicd" {
  account_id = "sa-gha-cicd"
}
