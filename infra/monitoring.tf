# Alert policies as code; every one links a runbook in docs/runbooks (standard 22, rule 12). Recipients come from
# var.alert_emails: with an empty list the policies exist and notify nobody, which the precondition in cloudrun_api.tf
# refuses for prod.
#
# Most alerts use platform metrics. The log-based metrics at the end of this file read the API's logs and depend on the
# wording of a log message, so standard 22 rule 14 applies: each filter must be tied to a message template defined as a
# constant in code, with a test that checks the match. Wolverine's own lines are in a library, so a rewording shows up
# only on a Wolverine upgrade: check it then.

variable "alert_emails" {
  type        = list(string)
  default     = []
  description = "Recipients of the alert emails. Empty until configured, so no one is notified; prod refuses an empty list (cloudrun_api.tf). deploy.yml and infra.yml pass it from the repository variable ALERT_EMAILS_SANDBOX or ALERT_EMAILS_PROD, a JSON list, when that variable is set."
}

locals {
  alert_channels = [for channel in google_monitoring_notification_channel.email : channel.id]

  # Alerts link to the runbooks of this repository on the default branch.
  runbook_base_url = "https://github.com/${local.props.repo_owner}/${local.props.repo_name}/blob/main/docs/runbooks"

  # Public host name served by the platform gateway, for the uptime check (the host of the gateway route file). Empty skips
  # the check, so a new app can apply before its route exists.
  public_hostname_by_environment = {
    sandbox = ""
    prod    = ""
  }
  public_hostname = local.public_hostname_by_environment[local.env]
}

resource "google_monitoring_notification_channel" "email" {
  for_each     = toset(var.alert_emails)
  depends_on   = [google_project_service.services]
  display_name = "${local.app} ${local.env}: ${each.value}"
  type         = "email"
  labels = {
    email_address = each.value
  }
}

resource "google_monitoring_alert_policy" "api_5xx" {
  display_name          = "${local.app} ${local.env}: api 5xx ratio above 1 %"
  combiner              = "OR"
  notification_channels = local.alert_channels
  documentation {
    mime_type = "text/markdown"
    content   = "More than 1 % of API requests answered 5xx over 5 minutes. Runbook: ${local.runbook_base_url}/api-5xx.md"
  }
  conditions {
    display_name = "5xx / all requests"
    condition_threshold {
      filter             = "resource.type = \"cloud_run_revision\" AND resource.labels.service_name = \"${local.api_service_name}\" AND metric.type = \"run.googleapis.com/request_count\" AND metric.labels.response_code_class = \"5xx\""
      denominator_filter = "resource.type = \"cloud_run_revision\" AND resource.labels.service_name = \"${local.api_service_name}\" AND metric.type = \"run.googleapis.com/request_count\""
      comparison         = "COMPARISON_GT"
      threshold_value    = 0.01
      duration           = "300s"
      aggregations {
        alignment_period     = "300s"
        per_series_aligner   = "ALIGN_SUM"
        cross_series_reducer = "REDUCE_SUM"
      }
      denominator_aggregations {
        alignment_period     = "300s"
        per_series_aligner   = "ALIGN_SUM"
        cross_series_reducer = "REDUCE_SUM"
      }
    }
  }
}

resource "google_monitoring_alert_policy" "api_latency" {
  display_name          = "${local.app} ${local.env}: api latency p95 above 1 s"
  combiner              = "OR"
  notification_channels = local.alert_channels
  documentation {
    mime_type = "text/markdown"
    content   = "p95 request latency of the API is above 1000 ms over 5 minutes. Runbook: ${local.runbook_base_url}/api-latency.md"
  }
  conditions {
    display_name = "request latency p95"
    condition_threshold {
      filter          = "resource.type = \"cloud_run_revision\" AND resource.labels.service_name = \"${local.api_service_name}\" AND metric.type = \"run.googleapis.com/request_latencies\""
      comparison      = "COMPARISON_GT"
      threshold_value = 1000 # milliseconds
      duration        = "300s"
      aggregations {
        alignment_period     = "300s"
        per_series_aligner   = "ALIGN_PERCENTILE_95"
        cross_series_reducer = "REDUCE_MAX"
      }
    }
  }
}

# Calls /health through the public host name, so it also covers the gateway, TLS and the database check. With scale to
# zero (sandbox) the first check after idle can be slow and may alert once; the check timeout allows for a cold start.
resource "google_monitoring_uptime_check_config" "health" {
  count        = local.public_hostname == "" ? 0 : 1
  depends_on   = [google_project_service.services]
  display_name = "${local.app} ${local.env}: /health"
  timeout      = "30s"
  period       = "300s"

  http_check {
    path           = "/health"
    port           = 443
    use_ssl        = true
    validate_ssl   = true
    request_method = "GET"
  }

  monitored_resource {
    type = "uptime_url"
    labels = {
      project_id = local.project_id
      host       = local.public_hostname
    }
  }
}

resource "google_monitoring_alert_policy" "health_check_failed" {
  count                 = local.public_hostname == "" ? 0 : 1
  display_name          = "${local.app} ${local.env}: /health check failing"
  combiner              = "OR"
  notification_channels = local.alert_channels
  documentation {
    mime_type = "text/markdown"
    content   = "The uptime check on https://${local.public_hostname}/health has failed from more than one location. Runbook: ${local.runbook_base_url}/health-check-failed.md"
  }
  conditions {
    display_name = "uptime check failing"
    condition_threshold {
      filter          = "metric.type = \"monitoring.googleapis.com/uptime_check/check_passed\" AND metric.labels.check_id = \"${google_monitoring_uptime_check_config.health[0].uptime_check_id}\" AND resource.type = \"uptime_url\""
      comparison      = "COMPARISON_GT"
      threshold_value = 1
      duration        = "300s"
      aggregations {
        alignment_period     = "300s"
        per_series_aligner   = "ALIGN_NEXT_OLDER"
        cross_series_reducer = "REDUCE_COUNT_FALSE"
        group_by_fields      = ["resource.label.host"]
      }
    }
  }
}

# --- Serverless messaging (pubsub.tf, scheduler.tf) -------------------------------------------------------------------

resource "google_monitoring_alert_policy" "pubsub_dead_letter" {
  display_name          = "${local.app} ${local.env}: Pub/Sub dead letters"
  combiner              = "OR"
  notification_channels = local.alert_channels
  documentation {
    mime_type = "text/markdown"
    content   = "A message on `${google_pubsub_topic.messages.name}` failed ${google_pubsub_subscription.messages_push.dead_letter_policy[0].max_delivery_attempts} push deliveries to the API and waits in `${google_pubsub_subscription.dead_letter_inspect.name}` for 7 days. Runbook: ${local.runbook_base_url}/pubsub-dead-letters.md"
  }
  conditions {
    display_name = "messages in the dead-letter inspect subscription"
    condition_threshold {
      filter          = "resource.type = \"pubsub_subscription\" AND resource.labels.subscription_id = \"${google_pubsub_subscription.dead_letter_inspect.name}\" AND metric.type = \"pubsub.googleapis.com/subscription/num_undelivered_messages\""
      comparison      = "COMPARISON_GT"
      threshold_value = 0
      duration        = "300s"
      aggregations {
        alignment_period   = "60s"
        per_series_aligner = "ALIGN_MAX"
      }
    }
  }
}

resource "google_monitoring_alert_policy" "pubsub_oldest_unacked" {
  display_name          = "${local.app} ${local.env}: Pub/Sub push backlog"
  combiner              = "OR"
  notification_channels = local.alert_channels
  documentation {
    mime_type = "text/markdown"
    content   = "The oldest message on `${google_pubsub_subscription.messages_push.name}` has been unacknowledged for more than 15 minutes: pushes to the API are failing or the API does not keep up. Runbook: ${local.runbook_base_url}/pubsub-dead-letters.md"
  }
  conditions {
    display_name = "oldest unacked message age"
    condition_threshold {
      filter          = "resource.type = \"pubsub_subscription\" AND resource.labels.subscription_id = \"${google_pubsub_subscription.messages_push.name}\" AND metric.type = \"pubsub.googleapis.com/subscription/oldest_unacked_message_age\""
      comparison      = "COMPARISON_GT"
      threshold_value = 900 # seconds
      duration        = "300s"
      aggregations {
        alignment_period   = "60s"
        per_series_aligner = "ALIGN_MAX"
      }
    }
  }
}

# Log-based metrics. On Cloud Run the API logs JSON, so the message is in jsonPayload.message.
locals {
  api_log_filter = "resource.type=\"cloud_run_revision\" AND resource.labels.service_name=\"${local.api_service_name}\""

  # Alert when more than `above` matching lines fall in `window`.
  log_metric_alerts = {
    poison_messages = {
      name     = "pushed messages moved to Wolverine dead letters"
      metric   = google_logging_metric.poison_messages.name
      resource = "cloud_run_revision"
      above    = 0
      window   = "300s"
      doc      = "A message could not be handled and Wolverine moved it to its own dead letter table and acknowledged it, so it never reaches the Pub/Sub dead-letter topic. Find `was moved to the error queue` in the API log. Runbook: ${local.runbook_base_url}/pubsub-dead-letters.md"
    }
    durability_pass_failed = {
      name     = "durability pass failed"
      metric   = google_logging_metric.durability_pass_failed.name
      resource = "cloud_run_revision"
      above    = 4 # 5 or more of the passes that run every minute
      window   = "600s"
      doc      = "The durability pass could not run at least 5 times in 10 minutes (lock not taken, or the pass failed). Outbox rows whose inline send failed are then not sent. Runbook: ${local.runbook_base_url}/durability-pass.md"
    }
    scheduler_failures = {
      name     = "Cloud Scheduler job failed"
      metric   = google_logging_metric.scheduler_failures.name
      resource = "cloud_scheduler_job"
      above    = 2 # 3 or more failed attempts
      window   = "1800s"
      doc      = "The Cloud Scheduler job `${google_cloud_scheduler_job.durability_pass.name}` failed at least 3 times in 30 minutes. Runbook: ${local.runbook_base_url}/durability-pass.md"
    }
  }
}

# Wolverine logs "Envelope {envelope} was moved to the error queue" when it dead-letters a message in its own table.
resource "google_logging_metric" "poison_messages" {
  name   = "${local.app}/poison_messages_${local.env}"
  filter = "${local.api_log_filter} AND jsonPayload.message:\"was moved to the error queue\""
  metric_descriptor {
    metric_kind = "DELTA"
    value_type  = "INT64"
  }
}

# Wolverine 6.51.0 message templates (Wolverine.dll): "Durability pass step {Step} failed against message store {Store}",
# "Unable to take the durability pass lock for message store {Store}" and "Skipping message store {Store} in this durability
# pass because another pass holds its lock". Library text like the poison line above: check it on a Wolverine upgrade.
resource "google_logging_metric" "durability_pass_failed" {
  name   = "${local.app}/durability_pass_failed_${local.env}"
  filter = "${local.api_log_filter} AND (jsonPayload.message:\"Durability pass step\" OR jsonPayload.message:\"Unable to take the durability pass lock\" OR jsonPayload.message:\"because another pass holds its lock\")"
  metric_descriptor {
    metric_kind = "DELTA"
    value_type  = "INT64"
  }
}

# Cloud Scheduler logs an attempt that ended in a non-2xx answer or a deadline at severity ERROR.
resource "google_logging_metric" "scheduler_failures" {
  name   = "${local.app}/scheduler_failures_${local.env}"
  filter = "resource.type=\"cloud_scheduler_job\" AND severity>=ERROR AND resource.labels.job_id=\"${google_cloud_scheduler_job.durability_pass.name}\""
  metric_descriptor {
    metric_kind = "DELTA"
    value_type  = "INT64"
  }
}

resource "google_monitoring_alert_policy" "log_metric" {
  for_each              = local.log_metric_alerts
  display_name          = "${local.app} ${local.env}: ${each.value.name}"
  combiner              = "OR"
  notification_channels = local.alert_channels
  documentation {
    mime_type = "text/markdown"
    content   = each.value.doc
  }
  conditions {
    display_name = each.value.name
    condition_threshold {
      filter          = "resource.type = \"${each.value.resource}\" AND metric.type = \"logging.googleapis.com/user/${each.value.metric}\""
      comparison      = "COMPARISON_GT"
      threshold_value = each.value.above
      duration        = "0s"
      aggregations {
        alignment_period     = each.value.window
        per_series_aligner   = "ALIGN_SUM"
        cross_series_reducer = "REDUCE_SUM"
      }
    }
  }
}
