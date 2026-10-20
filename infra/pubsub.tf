# Wolverine (DurabilityMode.ServerlessWithOutbox) stores outgoing integration messages in the Postgres outbox in the
# handler transaction and publishes them to one topic after the commit. The push subscription delivers each message back
# to the API's POST /internal/pubsub/messages, so handlers run inside a request (Cloud Run gives an instance CPU only
# while it serves one). A message that fails max_delivery_attempts pushes goes to the dead-letter topic.
# The durability pass that sends outbox rows whose inline send failed is in scheduler.tf.
locals {
  pubsub_service_agent = "serviceAccount:service-${data.google_project.main.number}@gcp-sa-pubsub.iam.gserviceaccount.com"

  # Audiences of the Google OIDC tokens that Pub/Sub push and Cloud Scheduler send to /internal/**. Fixed strings, set as
  # Cloud Run custom audiences, so the API's env vars do not depend on the service's own URL.
  internal_pubsub_audience    = "https://${local.api_service_name}/internal/pubsub"
  internal_scheduler_audience = "https://${local.api_service_name}/internal/scheduler"
}

# Identity the push subscription signs its OIDC tokens as. /internal/pubsub accepts only tokens from it.
resource "google_service_account" "pubsub_push" {
  account_id   = "sa-${local.app}-pubsub-push-${local.env}"
  display_name = "${local.app} Pub/Sub push to the API (${local.env})"
}

# Creating a push subscription with an OIDC token requires actAs on the token's service account.
resource "google_service_account_iam_member" "terraform_acts_as_pubsub_push" {
  service_account_id = google_service_account.pubsub_push.name
  role               = "roles/iam.serviceAccountUser"
  member             = local.terraform_sa_member
}

# The Pub/Sub service agent mints the OIDC tokens for the push service account.
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
  name  = "${local.app}-messages-push-${local.env}"
  topic = google_pubsub_topic.messages.id

  # Matches the Cloud Run request timeout (cloudrun_api.tf).
  ack_deadline_seconds = 600

  push_config {
    push_endpoint = "${google_cloud_run_v2_service.api.uri}/internal/pubsub/messages"

    oidc_token {
      service_account_email = google_service_account.pubsub_push.email
      audience              = local.internal_pubsub_audience
    }
  }

  retry_policy {
    minimum_backoff = "10s"
    maximum_backoff = "600s"
  }

  dead_letter_policy {
    dead_letter_topic     = google_pubsub_topic.dead_letter.id
    max_delivery_attempts = 5
  }

  expiration_policy {
    ttl = "" # never expires
  }

  depends_on = [
    google_service_account_iam_member.terraform_acts_as_pubsub_push,
    google_service_account_iam_member.pubsub_push_token_creator,
  ]
}

# Keeps dead-lettered messages for 7 days. Nothing consumes it: an operator pulls from it to look at a message, see
# docs/runbooks/pubsub-dead-letters.md.
resource "google_pubsub_subscription" "dead_letter_inspect" {
  name  = "${local.app}-dead-letter-inspect-${local.env}"
  topic = google_pubsub_topic.dead_letter.id

  message_retention_duration = "604800s"

  expiration_policy {
    ttl = "" # never expires
  }
}

# The API publishes to the messages topic only...
resource "google_pubsub_topic_iam_member" "api_publisher" {
  topic  = google_pubsub_topic.messages.id
  role   = "roles/pubsub.publisher"
  member = "serviceAccount:${google_service_account.api.email}"
}

# ...and reads it: Wolverine's resource check gets the topic at startup (it creates topics only with AutoProvision, which
# is off outside Development). Scoped to this topic.
resource "google_pubsub_topic_iam_member" "api_viewer" {
  topic  = google_pubsub_topic.messages.id
  role   = "roles/pubsub.viewer"
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

# The push request carries an identity token, so the push account may invoke this service and no other.
resource "google_cloud_run_v2_service_iam_member" "pubsub_push_invoker" {
  name     = google_cloud_run_v2_service.api.name
  location = google_cloud_run_v2_service.api.location
  role     = "roles/run.invoker"
  member   = "serviceAccount:${google_service_account.pubsub_push.email}"
}
