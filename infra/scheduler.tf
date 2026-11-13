# Cloud Scheduler calls the internal endpoints (docs/adrs/0024-wolverine-serverless-with-outbox-and-pubsub-push.md). They are scheduled requests because Cloud Run gives an instance
# CPU only while it serves one, so the app runs no timers of its own.

# Identity the jobs sign their OIDC token as. /internal/scheduler routes accept only tokens from it.
resource "google_service_account" "scheduler" {
  account_id   = "sa-${local.app}-sched-${local.env}"
  display_name = "${local.app} Cloud Scheduler to the API (${local.env})"
}

# The Cloud Scheduler service agent mints the OIDC tokens for the scheduler account.
resource "google_service_account_iam_member" "scheduler_token_creator" {
  service_account_id = google_service_account.scheduler.name
  role               = "roles/iam.serviceAccountTokenCreator"
  member             = "serviceAccount:service-${data.google_project.main.number}@gcp-sa-cloudscheduler.iam.gserviceaccount.com"

  depends_on = [google_project_service.services]
}

# Sends outbox rows whose inline send failed, releases due scheduled messages and deletes expired envelopes.
resource "google_cloud_scheduler_job" "durability_pass" {
  name             = "${local.app}-durability-pass-${local.env}"
  region           = local.region # Cloud Scheduler supports europe-west4
  schedule         = "* * * * *"
  time_zone        = "Europe/Oslo"
  attempt_deadline = "60s"

  # The next minute is the retry.
  retry_config {
    retry_count = 0
  }

  http_target {
    http_method = "POST"
    # maxDuration (seconds) ends a slow pass cleanly, well inside attempt_deadline.
    uri = "${google_cloud_run_v2_service.api.uri}/internal/durability/pass?maxDuration=30"

    oidc_token {
      service_account_email = google_service_account.scheduler.email
      audience              = local.internal_scheduler_audience
    }
  }

  depends_on = [
    google_project_service.services,
    google_service_account_iam_member.scheduler_token_creator,
  ]
}
