# Internal messages (docs/adrs/0024-wolverine-serverless-with-outbox-and-pubsub-push.md), one topic per preview: Wolverine publishes to it
# and the push subscription delivers each message back to this preview's API.
locals {
  pubsub_service_agent = "serviceAccount:service-${data.google_project.main.number}@gcp-sa-pubsub.iam.gserviceaccount.com"

  # Audiences of the Google OIDC tokens that Pub/Sub push and Cloud Scheduler send to /internal/**. Fixed strings, not
  # the run.app URL, so the service's env vars do not depend on its own URL.
  internal_pubsub_audience    = "https://${local.api_service}/internal/pubsub"
  internal_scheduler_audience = "https://${local.api_service}/internal/scheduler"

  # Keep equal to the Cloud Run request timeout.
  pubsub_ack_deadline_seconds = 300

  messages_topic = "${local.app}-messages-${local.slug}"
  push_name      = "${local.app}-messages-push-${local.slug}"
}

resource "google_pubsub_topic" "messages" {
  name   = local.messages_topic
  labels = local.labels
}

resource "google_pubsub_topic" "dead_letter" {
  name   = "${local.app}-dead-letter-${local.slug}"
  labels = local.labels
}

resource "google_pubsub_subscription" "messages_push" {
  name                 = local.push_name
  topic                = google_pubsub_topic.messages.id
  ack_deadline_seconds = local.pubsub_ack_deadline_seconds
  labels               = local.labels

  push_config {
    push_endpoint = "${local.api_url}/internal/pubsub/${local.push_name}"

    oidc_token {
      service_account_email = google_service_account.pubsub_push.email
      audience              = local.internal_pubsub_audience
    }
  }

  retry_policy {
    minimum_backoff = "10s"
    maximum_backoff = "600s"
  }

  # Only a backstop: Wolverine dead-letters and acknowledges on delivery 5, well before delivery 10.
  dead_letter_policy {
    dead_letter_topic     = google_pubsub_topic.dead_letter.id
    max_delivery_attempts = 10
  }

  # The subscription is destroyed with the preview.
  expiration_policy {
    ttl = ""
  }

  depends_on = [
    google_service_account_iam_member.pubsub_push_token_creator,
    google_cloud_run_v2_service.api,
  ]
}

# Keeps dead-lettered messages for 7 days. Nothing consumes it; a person pulls from it to inspect.
resource "google_pubsub_subscription" "dead_letter_inspect" {
  name   = "${local.app}-dead-letter-inspect-${local.slug}"
  topic  = google_pubsub_topic.dead_letter.id
  labels = local.labels

  message_retention_duration = "604800s"

  expiration_policy {
    ttl = ""
  }
}

# The API publishes to this preview's messages topic only.
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
