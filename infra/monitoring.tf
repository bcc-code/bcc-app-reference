# Alert policies as code; every one links a runbook in docs/runbooks (standard 22, rule 12). Recipients come from
# var.alert_emails: with an empty list the policies exist and notify nobody, which the precondition in cloudrun_api.tf
# refuses for prod.
#
# Metrics only, no log-based filters: no alert depends on the wording of a log message (standard 22 rule 14). Add log
# metrics together with a test that ties each filter to the message template it matches.

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
    content   = "More than 1 % of API requests answered 5xx over 5 minutes. Runbook: ${var.runbook_base_url}/api-5xx.md"
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
    content   = "p95 request latency of the API is above 1000 ms over 5 minutes. Runbook: ${var.runbook_base_url}/api-latency.md"
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
  count        = var.public_hostname == "" ? 0 : 1
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
      project_id = var.project_id
      host       = var.public_hostname
    }
  }
}

resource "google_monitoring_alert_policy" "health_check_failed" {
  count                 = var.public_hostname == "" ? 0 : 1
  display_name          = "${local.app} ${local.env}: /health check failing"
  combiner              = "OR"
  notification_channels = local.alert_channels
  documentation {
    mime_type = "text/markdown"
    content   = "The uptime check on https://${var.public_hostname}/health has failed from more than one location. Runbook: ${var.runbook_base_url}/health-check-failed.md"
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

resource "google_monitoring_alert_policy" "migration_failed" {
  display_name          = "${local.app} ${local.env}: migration job failed"
  combiner              = "OR"
  notification_channels = local.alert_channels
  documentation {
    mime_type = "text/markdown"
    content   = "An execution of ${local.migrate_job_name} failed. The rollout of the new revision did not happen. Runbook: ${var.runbook_base_url}/migration-failed.md"
  }
  conditions {
    display_name = "failed job execution"
    condition_threshold {
      filter          = "resource.type = \"cloud_run_job\" AND resource.labels.job_name = \"${local.migrate_job_name}\" AND metric.type = \"run.googleapis.com/job/completed_execution_count\" AND metric.labels.result = \"failed\""
      comparison      = "COMPARISON_GT"
      threshold_value = 0
      duration        = "0s"
      aggregations {
        alignment_period   = "300s"
        per_series_aligner = "ALIGN_SUM"
      }
    }
  }
}

resource "google_monitoring_alert_policy" "database_cpu" {
  display_name          = "${local.app} ${local.env}: database CPU above 80 %"
  combiner              = "OR"
  notification_channels = local.alert_channels
  documentation {
    mime_type = "text/markdown"
    content   = "Cloud SQL CPU utilisation has been above 80 % for 10 minutes. Runbook: ${var.runbook_base_url}/database-cpu.md"
  }
  conditions {
    display_name = "cloud sql cpu utilisation"
    condition_threshold {
      filter          = "resource.type = \"cloudsql_database\" AND resource.labels.database_id = \"${var.project_id}:${google_sql_database_instance.main.name}\" AND metric.type = \"cloudsql.googleapis.com/database/cpu/utilization\""
      comparison      = "COMPARISON_GT"
      threshold_value = 0.8
      duration        = "600s"
      aggregations {
        alignment_period   = "60s"
        per_series_aligner = "ALIGN_MEAN"
      }
    }
  }
}
