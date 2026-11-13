# Internal messages (docs/adrs/0024-wolverine-serverless-with-outbox-and-pubsub-push.md): Wolverine publishes to one topic and the push
# subscription delivers each message back to the API, so handlers run inside a request (Cloud Run gives an instance CPU
# only while it serves one).
data "google_project" "main" {
  project_id = local.project_id
}

locals {
  pubsub_service_agent = "serviceAccount:service-${data.google_project.main.number}@gcp-sa-pubsub.iam.gserviceaccount.com"

  # Audiences of the Google OIDC tokens that Pub/Sub push and Cloud Scheduler send to /internal/**. Fixed strings, not the
  # run.app URL, so the service's env vars do not depend on its own URL.
  internal_pubsub_audience    = "https://${local.api_service_name}/internal/pubsub"
  internal_scheduler_audience = "https://${local.api_service_name}/internal/scheduler"

  # Pub/Sub waits this long for the push to be acknowledged; keep equal to the Cloud Run request timeout (300 s, the default).
  pubsub_ack_deadline_seconds = 300
}

# Identity the push subscription signs its OIDC token as. /internal/pubsub accepts only tokens from it.
resource "google_service_account" "pubsub_push" {
  account_id   = "sa-${local.app}-push-${local.env}"
  display_name = "${local.app} Pub/Sub push to the API (${local.env})"
}

# The Pub/Sub service agent mints the OIDC tokens for the push account.
resource "google_service_account_iam_member" "pubsub_push_token_creator" {
  service_account_id = google_service_account.pubsub_push.name
  role               = "roles/iam.serviceAccountTokenCreator"
  member             = local.pubsub_service_agent

  depends_on = [google_project_service.services]
}

resource "google_pubsub_topic" "messages" {
  name       = "${local.app}-messages-${local.env}"
  depends_on = [google_project_service.services]
}

resource "google_pubsub_topic" "dead_letter" {
  name       = "${local.app}-dead-letter-${local.env}"
  depends_on = [google_project_service.services]
}

resource "google_pubsub_subscription" "messages_push" {
  name                 = "${local.app}-messages-push-${local.env}"
  topic                = google_pubsub_topic.messages.id
  ack_deadline_seconds = local.pubsub_ack_deadline_seconds

  push_config {
    push_endpoint = "${google_cloud_run_v2_service.api.uri}/internal/pubsub/${local.app}-messages-push-${local.env}"

    oidc_token {
      service_account_email = google_service_account.pubsub_push.email
      audience              = local.internal_pubsub_audience
    }
  }

  retry_policy {
    minimum_backoff = "10s"
    maximum_backoff = "600s"
  }

  # Only a backstop. A failed handler answers 500 and Pub/Sub redelivers; Wolverine dead-letters and acknowledges on delivery 5,
  # well before delivery 10 here, so a message reaches this topic only when the request itself keeps failing (crash, timeout).
  dead_letter_policy {
    dead_letter_topic     = google_pubsub_topic.dead_letter.id
    max_delivery_attempts = 10
  }

  expiration_policy {
    ttl = "" # never expires
  }

  depends_on = [google_service_account_iam_member.pubsub_push_token_creator]
}

# Keeps dead-lettered messages for 7 days (standard 04.15). Nothing consumes it; a person pulls from it to inspect.
resource "google_pubsub_subscription" "dead_letter_inspect" {
  name  = "${local.app}-dead-letter-inspect-${local.env}"
  topic = google_pubsub_topic.dead_letter.id

  message_retention_duration = "604800s"

  expiration_policy {
    ttl = "" # never expires
  }
}

# The API publishes to the messages topic only, not project-wide.
resource "google_pubsub_topic_iam_member" "api_publisher" {
  topic  = google_pubsub_topic.messages.id
  role   = "roles/pubsub.publisher"
  member = "serviceAccount:${google_service_account.api.email}"
}

# The Pub/Sub service agent forwards undeliverable messages to the dead-letter topic...
resource "google_pubsub_topic_iam_member" "dead_letter_publisher" {
  topic  = google_pubsub_topic.dead_letter.id
  role   = "roles/pubsub.publisher"
  member = local.pubsub_service_agent
}

# ...and acknowledges them on the source subscription.
resource "google_pubsub_subscription_iam_member" "dead_letter_subscriber" {
  subscription = google_pubsub_subscription.messages_push.id
  role         = "roles/pubsub.subscriber"
  member       = local.pubsub_service_agent
}

# No run.invoker for the push account: the service already allows allUsers (cloudrun_api.tf), ingress limits who can call
# it, and the app validates the OIDC token and the caller's email itself.
