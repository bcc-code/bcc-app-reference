locals {
  props      = merge(jsondecode(var.props), jsondecode(var.deployment_props))
  env        = local.props.app_environment
  project_id = local.props.gcp.project_id
  region     = local.props.gcp.location

  # The app name in resource names and in the gateway route file (routes/<app>.yaml in bcc-platform-gateway). init-app.ps1
  # replaces "template" with the app's slug (for example vote), so the app is bcc-vote and the service bcc-vote-api-<env>, as bcc-ateam.
  app = "bcc-template"

  api_service_name = "${local.app}-api-${local.env}"
}

provider "google" {
  project               = local.project_id
  region                = local.region
  billing_project       = local.project_id
  user_project_override = true
}

resource "google_project_service" "services" {
  for_each = toset([
    "artifactregistry.googleapis.com",
    "cloudkms.googleapis.com",
    "cloudscheduler.googleapis.com",
    "cloudtrace.googleapis.com", # the BCC monitoring collector writes our traces here
    "iamcredentials.googleapis.com",
    "logging.googleapis.com",
    "monitoring.googleapis.com",
    "pubsub.googleapis.com",
    "run.googleapis.com",
    "secretmanager.googleapis.com",
    "sqladmin.googleapis.com",
    "storage.googleapis.com",
  ])
  service = each.value
  # Other resources in the project may depend on the API; do not switch it off with this state.
  disable_on_destroy = false
}
