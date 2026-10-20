# Cloud Scheduler calls the Wolverine durability pass every minute (POST /internal/durability/pass). The pass sends outbox
# rows whose inline send failed and deletes expired envelopes and dead letters. It runs as a request because Cloud Run
# gives an instance CPU only while it serves one. The endpoint answers 500 when the pass failed, which Cloud Scheduler
# logs as an error (alert in monitoring.tf).

# Identity the Cloud Scheduler job signs its OIDC token as. /internal/durability accepts only tokens from it.
resource "google_service_account" "scheduler" {
  account_id   = "sa-${local.app}-scheduler-${local.env}"
  display_name = "${local.app} Cloud Scheduler to the API (${local.env})"
}

# Creating a job with an OIDC token requires actAs on the token's service account.
resource "google_service_account_iam_member" "terraform_acts_as_scheduler" {
  service_account_id = google_service_account.scheduler.name
  role               = "roles/iam.serviceAccountUser"
  member             = local.terraform_sa_member
}

# The Cloud Scheduler service agent mints the OIDC tokens for the scheduler service account.
resource "google_service_account_iam_member" "scheduler_token_creator" {
  service_account_id = google_service_account.scheduler.name
  role               = "roles/iam.serviceAccountTokenCreator"
  member             = "serviceAccount:service-${data.google_project.main.number}@gcp-sa-cloudscheduler.iam.gserviceaccount.com"

  depends_on = [google_project_service.services]
}

resource "google_cloud_run_v2_service_iam_member" "scheduler_invoker" {
  name     = google_cloud_run_v2_service.api.name
  location = google_cloud_run_v2_service.api.location
  role     = "roles/run.invoker"
  member   = "serviceAccount:${google_service_account.scheduler.email}"
}

resource "google_cloud_scheduler_job" "durability_pass" {
  name             = "${local.app}-durability-pass-${local.env}"
  region           = var.region # the API's region
  schedule         = "* * * * *"
  time_zone        = "Etc/UTC"
  attempt_deadline = "60s"

  # The next minute is the retry.
  retry_config {
    retry_count = 0
  }

  http_target {
    http_method = "POST"
    # maxDuration (seconds) bounds the pass well inside attempt_deadline, so a slow pass ends cleanly instead of being cut off.
    uri = "${google_cloud_run_v2_service.api.uri}/internal/durability/pass?maxDuration=30"

    oidc_token {
      service_account_email = google_service_account.scheduler.email
      audience              = local.internal_scheduler_audience
    }
  }

  depends_on = [
    google_project_service.services,
    google_service_account_iam_member.terraform_acts_as_scheduler,
    google_service_account_iam_member.scheduler_token_creator,
  ]
}
