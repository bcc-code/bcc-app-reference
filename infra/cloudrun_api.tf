locals {
  # IAM database user: the service account email without ".gserviceaccount.com". The Auth Proxy sidecar listens on 5432.
  # Keep the names below in step with the configuration keys the API reads (OIDC:* in BccCode.Template.Web, the
  # connection name "template" in the API project's configuration).
  db_connection_string = "Host=127.0.0.1;Port=5432;Database=${local.db_name};Username=${google_sql_user.api.name};Maximum Pool Size=10"

  api_env = {
    Gcp__ProjectId                = var.project_id
    Gcp__DataProtectionKmsKeyName = google_kms_crypto_key.data_protection.id
    ConnectionStrings__template   = local.db_connection_string
    OIDC__Authority               = var.oidc_authority
    OIDC__ClientId                = var.oidc_client_id
    # deploy.yml bakes index.html into the API project's wwwroot, which the .NET SDK image puts under its working directory
    # /app. The host requires the path in Static mode (the default outside Development) and fails at startup without it;
    # the migration job runs the same host, so it needs it too.
    Frontend__StaticPath = "/app/wwwroot"
    # Wolverine runs ServerlessWithOutbox: the outbox publishes to this topic and /internal/** accepts only these callers
    # (pubsub.tf, scheduler.tf). Internal__AllowAll is for local runs and is never set here.
    Messaging__PubsubProjectId        = var.project_id
    Messaging__Topic                  = google_pubsub_topic.messages.name
    Internal__PubSubAudience          = local.internal_pubsub_audience
    Internal__PubSubServiceAccount    = google_service_account.pubsub_push.email
    Internal__SchedulerAudience       = local.internal_scheduler_audience
    Internal__SchedulerServiceAccount = google_service_account.scheduler.email
    # Required by standard 22 rule 2. No OTLP endpoint is set: under request-based billing spans are best effort and
    # alerting depends on metrics and logs only (docs/runbooks/README.md).
    OTEL_SERVICE_NAME        = "${local.app}-api"
    OTEL_RESOURCE_ATTRIBUTES = "deployment.environment.name=${local.env}"
  }
}

resource "google_cloud_run_v2_service" "api" {
  depends_on          = [google_project_service.services, google_secret_manager_secret_iam_member.api_oidc_client_secret]
  name                = local.api_service_name
  location            = var.region
  deletion_protection = local.cfg.service_deletion_protect

  # Reachable only through the platform gateway (standard 04 rule 2, standard 26 rule 7). Change this in a separate apply
  # after the gateway route is live and verified; with a wrong route and this value there is no way in.
  ingress = "INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER"

  # Lets Cloud Run accept the Pub/Sub push and Cloud Scheduler tokens, whose audience is not the service URL.
  custom_audiences = [local.internal_pubsub_audience, local.internal_scheduler_audience]

  template {
    service_account                  = google_service_account.api.email
    max_instance_request_concurrency = 80
    # Covers the longest push: the push subscription's ack deadline is 600 s (pubsub.tf).
    timeout = "600s"
    scaling {
      min_instance_count = local.cfg.api_min_instances
      max_instance_count = local.cfg.api_max_instances
    }

    containers {
      name       = "app"
      image      = local.image_placeholder
      depends_on = ["cloud-sql-proxy"]
      ports {
        container_port = 8080
      }
      resources {
        cpu_idle          = local.cfg.api_cpu_idle
        startup_cpu_boost = true
        limits            = { cpu = "1000m", memory = "512Mi" }
      }
      dynamic "env" {
        for_each = local.api_env
        content {
          name  = env.key
          value = env.value
        }
      }
      env {
        name = "OIDC__ClientSecret"
        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.oidc_client_secret.secret_id
            version = "latest"
          }
        }
      }

      # Written out because the provider keeps the live probe when the block is removed (standard 04 rule 6). TCP passes as
      # soon as the port accepts; /health checks the database and would delay a cold start.
      startup_probe {
        tcp_socket {
          port = 8080
        }
        timeout_seconds   = 240
        period_seconds    = 240
        failure_threshold = 1
      }

      # Liveness must NOT include the database: a Cloud SQL outage would otherwise fail liveness on every instance at once
      # and cause a restart storm. /alive is a self check only. /health (all checks) is used by the uptime check and the
      # smoke test (standard 04 rule 7, standard 22 rule 9).
      liveness_probe {
        http_get {
          path = "/alive"
          port = 8080
        }
        initial_delay_seconds = 10
        period_seconds        = 30
        timeout_seconds       = 5
        failure_threshold     = 3
      }
    }

    containers {
      name  = "cloud-sql-proxy"
      image = local.sql_proxy_image
      args  = ["--address=0.0.0.0", "--port=5432", "--auto-iam-authn", "--structured-logs", google_sql_database_instance.main.connection_name]
      resources {
        limits = { cpu = "500m", memory = "256Mi" }
      }
      # The app depends on this container, and Cloud Run waits for a dependency only through its own startup probe.
      # Checked every second (not the default 10 s), so a missed first check does not delay the cold start.
      startup_probe {
        tcp_socket {
          port = 5432
        }
        timeout_seconds   = 1
        period_seconds    = 1
        failure_threshold = 30
      }
    }
  }

  lifecycle {
    # The image is owned by the deploy workflow (.github/workflows/deploy.yml), which rolls out a digest. Terraform keeps
    # everything else, including the scaling bounds, so a console change to them shows up in the next plan.
    ignore_changes = [template[0].containers[0].image, client, client_version]

    precondition {
      condition     = var.environment != "prod" || length(var.alert_emails) > 0
      error_message = "alert_emails must be set for prod: an alert policy without a recipient alerts nobody (standard 22 rule 13)."
    }
  }
}

# allUsers is the invoker because the gateway does not send an identity token. Access is restricted by ingress, which
# accepts traffic from the internal load balancer only, not by this binding.
resource "google_cloud_run_v2_service_iam_member" "api_invoker" {
  name     = google_cloud_run_v2_service.api.name
  location = google_cloud_run_v2_service.api.location
  role     = "roles/run.invoker"
  member   = "allUsers"
}
