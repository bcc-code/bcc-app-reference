locals {
  # Mirrors the AppHost's settings for the OIDC mock (api/AppHost/Program.cs), with the preview's URLs. The API runs as
  # environment "preview", so it applies the production checks: every setting below is required.
  #
  # The preview's test data is not created here: the seeder (the image's own seed command) runs on the CI runner in
  # preview.yml after the apply, against this preview's schema, so Terraform needs nothing for it.
  api_env = {
    ASPNETCORE_ENVIRONMENT = "preview"
    Persistence__Schema    = local.slug

    # No password: the proxy authenticates the runtime account (IAM database user = its email without
    # ".gserviceaccount.com"). The pool is small because every preview shares the instance's pooler.
    ConnectionStrings__template = "Host=127.0.0.1;Port=5432;Database=${local.props.shared_db.db_name};Username=${trimsuffix(google_service_account.api.email, ".gserviceaccount.com")};Maximum Pool Size=3"

    Oidc__Authority = local.oidc_url
    Oidc__ClientId  = "template" # the OIDC mock's client id (api/OidcMock/appsettings.json)

    # deploy.yml and preview.yml bake index.html into the API project's wwwroot, under the image's working directory /app.
    Frontend__StaticPath = "/app/wwwroot"

    # Pub/Sub push and Cloud Scheduler call /internal/** with Google OIDC tokens; the app checks audience and email.
    Messaging__UsePubSub              = "true"
    Messaging__ProjectId              = local.project_id
    Messaging__Topic                  = google_pubsub_topic.messages.name
    Internal__PubSubAudience          = local.internal_pubsub_audience
    Internal__PubSubServiceAccount    = google_service_account.pubsub_push.email
    Internal__SchedulerAudience       = local.internal_scheduler_audience
    Internal__SchedulerServiceAccount = google_service_account.scheduler.email

    # Cloud Run terminates TLS; this makes Request.IsHttps follow X-Forwarded-Proto (UseHsts, the OIDC redirect URI).
    ASPNETCORE_FORWARDEDHEADERS_ENABLED = "true"

    Gcp__ProjectId                = local.project_id
    Gcp__DataProtectionKmsKeyName = data.google_kms_crypto_key.data_protection.id
    OTEL_SERVICE_NAME             = local.api_service
  }
}

resource "google_cloud_run_v2_service" "api" {
  depends_on = [
    google_project_iam_member.api_runtime,
    google_service_account_iam_member.cicd_acts_as,
    google_cloud_identity_group_membership.api,
    google_kms_crypto_key_iam_member.api_data_protection,
    google_pubsub_topic_iam_member.api_publisher,
    google_secret_manager_secret_iam_member.reader,
    google_secret_manager_secret_version.secret,
  ]

  name     = local.api_service
  location = local.region
  # The documented exception for previews: the run.app URL is the way in, and the app's own login guards it. A preview has
  # no gateway route and holds generated test data only, so unlike sandbox and prod it does not use the internal load
  # balancer.
  ingress             = "INGRESS_TRAFFIC_ALL"
  labels              = local.labels
  deletion_protection = false

  template {
    service_account = google_service_account.api.email
    labels          = local.labels
    # Equal to the push subscription's ack deadline (pubsub.tf).
    timeout = "${local.pubsub_ack_deadline_seconds}s"
    # Small, so one preview cannot take many pooled connections.
    max_instance_request_concurrency = 3

    scaling {
      min_instance_count = 0
      max_instance_count = 1
    }

    containers {
      name  = "app"
      image = var.api_image
      # Starts after the proxy accepts connections.
      depends_on = ["cloud-sql-proxy"]

      ports {
        container_port = 8080
      }

      resources {
        cpu_idle          = true
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
        name = "Oidc__ClientSecret"
        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.secret["oidc-client-secret"].secret_id
            version = "latest"
          }
        }
      }

      startup_probe {
        tcp_socket {
          port = 8080
        }
        timeout_seconds   = 1
        period_seconds    = 1
        failure_threshold = 30
      }

      # /alive checks the process only, so a dependency outage cannot restart the instance.
      liveness_probe {
        failure_threshold     = 3
        initial_delay_seconds = 10
        period_seconds        = 60
        timeout_seconds       = 30
        http_get {
          path = "/alive"
        }
      }
    }

    containers {
      name  = "cloud-sql-proxy"
      image = local.sql_proxy_image
      args  = ["--address=0.0.0.0", "--port=5432", "--auto-iam-authn", "--structured-logs", local.props.shared_db.connection_name]

      resources {
        limits = { cpu = "500m", memory = "256Mi" }
      }

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
}

# The browser reaches the service directly, and Pub/Sub push and Cloud Scheduler need no invoker role of their own:
# /internal/** validates their OIDC tokens in the app.
resource "google_cloud_run_v2_service_iam_member" "api_invoker" {
  name     = google_cloud_run_v2_service.api.name
  location = google_cloud_run_v2_service.api.location
  role     = "roles/run.invoker"
  member   = "allUsers"
}
