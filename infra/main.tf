locals {
  app = "template"
  env = var.environment

  # Everything that differs between environments is here, so a reviewer sees it in one place (standard 03, rule 2).
  # Billing model: both environments use request-based billing (cpu_idle = true). Background work does not run while an
  # instance is idle. If background work is added, change cpu_idle and the minimum instances together (standard 04, rule 19).
  env_config = {
    sandbox = {
      api_min_instances         = 0 # scale to zero
      api_max_instances         = 5
      api_cpu_idle              = true
      db_tier                   = "db-f1-micro"
      db_availability           = "ZONAL"
      db_point_in_time_recovery = false
      db_deletion_protection    = false # disposable: sandbox holds generated test data only
      service_deletion_protect  = false
    }
    prod = {
      api_min_instances         = 1 # one warm instance, so a quiet period does not end in a cold start
      api_max_instances         = 20
      api_cpu_idle              = true
      db_tier                   = "db-custom-1-3840"
      db_availability           = "REGIONAL"
      db_point_in_time_recovery = true
      db_deletion_protection    = true
      service_deletion_protect  = true
    }
  }
  cfg = local.env_config[local.env]

  api_service_name    = "${local.app}-api-${local.env}"
  migrate_job_name    = "${local.app}-migrate-${local.env}"
  image_placeholder   = "us-docker.pkg.dev/cloudrun/container/placeholder"
  sql_proxy_image     = "gcr.io/cloud-sql-connectors/cloud-sql-proxy:2.21.1"
  db_name             = local.app
  alert_channels      = [for c in google_monitoring_notification_channel.email : c.id]
  assets_bucket_name  = "${var.project_id}-assets" # bucket names are global; the project id is unique
  deploy_sa_member    = "serviceAccount:${google_service_account.deploy.email}"
  terraform_sa_member = "serviceAccount:${google_service_account.terraform.email}"
  plan_sa_member      = "serviceAccount:${google_service_account.plan.email}"
}

provider "google" {
  project               = var.project_id
  region                = var.region
  billing_project       = var.project_id
  user_project_override = true
}

provider "google-beta" {
  project               = var.project_id
  region                = var.region
  billing_project       = var.project_id
  user_project_override = true
}

data "google_project" "main" {}

resource "google_project_service" "services" {
  for_each = toset([
    "artifactregistry.googleapis.com",
    "cloudkms.googleapis.com",
    "cloudscheduler.googleapis.com",
    "cloudresourcemanager.googleapis.com",
    "iam.googleapis.com",
    "iamcredentials.googleapis.com",
    "monitoring.googleapis.com",
    "pubsub.googleapis.com",
    "run.googleapis.com",
    "secretmanager.googleapis.com",
    "sqladmin.googleapis.com",
    "storage.googleapis.com",
    "sts.googleapis.com",
  ])
  service            = each.value
  disable_on_destroy = false
}
