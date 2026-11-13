# Cloud Scheduler calls the durability pass (docs/adrs/0024-wolverine-serverless-with-outbox-and-pubsub-push.md): it sends outbox rows whose
# inline send failed, releases due scheduled messages and deletes expired envelopes. Every 30 minutes, not every minute, so an idle preview scales to zero;
# the cost is that scheduled retries and stale outbox rows wait up to 30 minutes.
resource "google_cloud_scheduler_job" "durability_pass" {
  name             = "${local.app}-durability-pass-${local.slug}"
  region           = local.region # Cloud Scheduler supports europe-west4
  schedule         = "*/30 * * * *"
  time_zone        = "Europe/Oslo"
  attempt_deadline = "60s"

  # The next run is the retry.
  retry_config {
    retry_count = 0
  }

  http_target {
    http_method = "POST"
    # maxDuration (seconds) ends a slow pass cleanly, well inside attempt_deadline.
    uri = "${local.api_url}/internal/durability/pass?maxDuration=30"

    oidc_token {
      service_account_email = google_service_account.scheduler.email
      audience              = local.internal_scheduler_audience
    }
  }

  depends_on = [
    google_service_account_iam_member.scheduler_token_creator,
    google_cloud_run_v2_service.api,
  ]
}
