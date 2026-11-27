locals {
  # Placeholder until the first deploy; deploy.yml sets the real image by digest.
  api_image_placeholder = "us-docker.pkg.dev/cloudrun/container/placeholder"

  # Billing model (standard 04.19): request-based. cpu_idle = true throttles CPU between requests, and sandbox scales to
  # zero, so a cold start follows the first request after idle (startup_cpu_boost shortens it). The every-minute
  # durability pass keeps an instance warm, which is not billed between requests; each pass costs only its own request
  # time. Prod keeps one instance so a quiet period does not end in a cold start. Instance-based billing (cpu_idle = false)
  # goes with in-process background work or minimum instances above zero.
  api_scaling_by_environment = {
    sandbox = { min = 0, max = 5 }
    prod    = { min = 1, max = 20 }
  }
  api_scaling = local.api_scaling_by_environment[local.env]

  # Only the gateway reaches the service; the run.app URL answers 404. The smoke test in deploy.yml goes through the gateway
  # (APP_BASE_URL), so the route has to exist before the first deploy (docs/runbooks/first-deploy.md). Prod must use the internal
  # load balancer (precondition below).
  api_ingress_by_environment = {
    sandbox = "INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER"
    prod    = "INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER"
  }
  api_ingress = local.api_ingress_by_environment[local.env]

  # Cloud SQL Auth Proxy sidecar, pinned by digest (standard 17.8). Version 2.26.2 is the newest release that was at
  # least 7 days old on 2026-10-26; the digest is the multi-arch index of gcr.io/cloud-sql-connectors/cloud-sql-proxy:2.26.2.
  # Dependabot does not read this file: refresh it by hand, and keep deploy.yml (the same proxy, as a binary with a
  # sha256) and preview/api.tf on the same version.
  sql_proxy_image = "gcr.io/cloud-sql-connectors/cloud-sql-proxy:2.26.2@sha256:e8c76356fb586300c63f0d29c8c75cfeeba33c29736f9d4afa2df2d78cd0fa19"

  # The sign-in providers per environment (ADR 0031): at least one must be on, and both may be. null turns a provider off: no
  # setting, no secret and no secret access for it.
  #
  # BCC Signon (Auth0). The client id (not a secret) is kept here, not passed in as a variable. The placeholder fails the apply
  # (precondition below) until the client exists and its id is entered (docs/runbooks/first-deploy.md). The client secret comes
  # from Secret Manager (<app>-oidc-client-secret).
  oidc_client_placeholder = "SET-OIDC-CLIENT-ID"
  oidc_by_environment = {
    sandbox = { authority = "https://login.sandbox.bcc.no", client_id = "SET-OIDC-CLIENT-ID" }
    prod    = { authority = "https://login.bcc.no", client_id = "SET-OIDC-CLIENT-ID" }
  }
  oidc = local.oidc_by_environment[local.env]

  # Microsoft Entra ID: the tenant and the client (application) id of the single-tenant app registration, neither a secret. Off
  # by default. The client secret comes from Secret Manager (<app>-entra-client-secret). Steps: docs/runbooks/first-deploy.md.
  #   sandbox = { tenant_id = "<directory (tenant) id>", client_id = "<application (client) id>" }
  entra_by_environment = {
    sandbox = null
    prod    = null
  }
  entra = local.entra_by_environment[local.env]

  signon_on = local.oidc != null
  entra_on  = local.entra != null

  # The first administrator (ADR 0031): Auth__BootstrapAdmins comes from the Secret Manager secret <app>-bootstrap-admins, because the
  # ids are those of real people and are not committed (standard 18.15). Off by default. Turn it on once the secret exists
  # (docs/runbooks/first-deploy.md); it may be turned off again after the first administrator has signed in.
  bootstrap_admins_secret_by_environment = {
    sandbox = false
    prod    = false
  }
  bootstrap_admins_secret_on = local.bootstrap_admins_secret_by_environment[local.env]

  # No password: the proxy authenticates the runtime account (IAM database user = its email without
  # ".gserviceaccount.com", standard 03.14). The pool is kept small: with a shared server, each instance holds at most 10
  # connections.
  api_env = merge(
    local.signon_on ? { Oidc__Authority = local.oidc.authority, Oidc__ClientId = local.oidc.client_id } : {},
    local.entra_on ? { Entra__TenantId = local.entra.tenant_id, Entra__ClientId = local.entra.client_id } : {},
    local.api_env_common,
  )

  api_env_common = {
    # deploy.yml bakes index.html into the API project's wwwroot, which the .NET SDK image puts under its working directory
    # /app. The host requires the path in Static mode (the default outside Development).
    Frontend__StaticPath = "/app/wwwroot"

    # Pub/Sub push and Cloud Scheduler call /internal/** with Google OIDC tokens; the app checks audience and email.
    # Internal__AllowAll is for local runs and is never set here.
    Messaging__UsePubSub              = "true"
    Messaging__ProjectId              = local.project_id
    Messaging__Topic                  = google_pubsub_topic.messages.name
    Internal__PubSubAudience          = local.internal_pubsub_audience
    Internal__PubSubServiceAccount    = google_service_account.pubsub_push.email
    Internal__SchedulerAudience       = local.internal_scheduler_audience
    Internal__SchedulerServiceAccount = google_service_account.scheduler.email

    # The app trusts X-Forwarded-For/-Proto from any sender, which is safe only when the load balancer is the only way in.
    # It makes Request.IsHttps follow X-Forwarded-Proto (UseHsts, the OIDC redirect URI).
    ASPNETCORE_FORWARDEDHEADERS_ENABLED = tostring(local.api_ingress == "INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER")

    Gcp__ProjectId                = local.project_id
    Gcp__DataProtectionKmsKeyName = google_kms_crypto_key.data_protection.id
    OTEL_SERVICE_NAME             = local.api_service_name
    ConnectionStrings__template   = "Host=127.0.0.1;Port=5432;Database=${local.props.shared_db.db_name};Username=${trimsuffix(google_service_account.api.email, ".gserviceaccount.com")};Maximum Pool Size=10"

    # Traces and metrics go to the BCC monitoring collector, over gRPC, the .NET default. The header with its key comes
    # from Secret Manager below. The collector routes by cloud.account.id: traces and logs land in this project as well
    # as the central one.
    OTEL_EXPORTER_OTLP_ENDPOINT = var.otel_exporter_otlp_endpoint
    OTEL_RESOURCE_ATTRIBUTES    = "cloud.account.id=${local.project_id},deployment.environment.name=${local.env}"
  }
}

resource "google_cloud_run_v2_service" "api" {
  depends_on = [
    google_project_service.services,
    google_project_iam_member.api_runtime,
    google_secret_manager_secret_iam_member.api_oidc_client_secret,
    google_secret_manager_secret_iam_member.api_entra_client_secret,
    google_secret_manager_secret_iam_member.api_bootstrap_admins,
    google_secret_manager_secret_iam_member.api_otel_exporter_otlp_headers,
    google_secret_manager_secret_version.otel_exporter_otlp_headers,
    google_pubsub_topic_iam_member.api_publisher,
    google_kms_crypto_key_iam_member.api_data_protection,
    google_cloud_identity_group_membership.shared_db_access,
  ]

  name     = local.api_service_name
  location = local.region
  ingress  = local.api_ingress
  # The service holds no state; a redeploy from this code rebuilds it (standard 03.16).
  deletion_protection = false

  template {
    service_account = google_service_account.api.email
    # Explicit so it stays equal to the push subscription's ack deadline (pubsub.tf).
    timeout = "${local.pubsub_ack_deadline_seconds}s"

    scaling {
      min_instance_count = local.api_scaling.min
      max_instance_count = local.api_scaling.max
    }

    containers {
      name  = "app"
      image = local.api_image_placeholder
      # Starts after the proxy accepts connections (standard 04.4). The app must stay the first container: deploy.yml
      # updates `--container app`, and ignore_changes below addresses it as containers[0].
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

      # Read at instance start from the latest version; rotating a secret needs a new revision. Only for a provider that is on.
      dynamic "env" {
        for_each = local.signon_on ? [1] : []
        content {
          name = "Oidc__ClientSecret"
          value_source {
            secret_key_ref {
              secret  = data.google_secret_manager_secret.oidc_client_secret[0].secret_id
              version = "latest"
            }
          }
        }
      }

      dynamic "env" {
        for_each = local.entra_on ? [1] : []
        content {
          name = "Entra__ClientSecret"
          value_source {
            secret_key_ref {
              secret  = data.google_secret_manager_secret.entra_client_secret[0].secret_id
              version = "latest"
            }
          }
        }
      }

      dynamic "env" {
        for_each = local.bootstrap_admins_secret_on ? [1] : []
        content {
          name = "Auth__BootstrapAdmins"
          value_source {
            secret_key_ref {
              secret  = data.google_secret_manager_secret.bootstrap_admins[0].secret_id
              version = "latest"
            }
          }
        }
      }

      env {
        name = "OTEL_EXPORTER_OTLP_HEADERS"
        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.otel_exporter_otlp_headers.secret_id
            version = "latest"
          }
        }
      }

      # Written out because the provider keeps the live probe when the block is removed (standard 04.6). Passes as
      # soon as the port accepts connections.
      startup_probe {
        tcp_socket {
          port = 8080
        }
        timeout_seconds   = 1
        period_seconds    = 1
        failure_threshold = 30
      }

      # /alive checks the process only, no dependencies, so a dependency outage cannot restart every instance
      # (standard 04.7). Dependency checks belong on /health.
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

    # Cloud Run gives every container its own limits, so the proxy's 500m/256Mi come on top of the app's 1000m/512Mi
    # for the instance.
    containers {
      name  = "cloud-sql-proxy"
      image = local.sql_proxy_image
      args  = ["--address=0.0.0.0", "--port=5432", "--auto-iam-authn", "--structured-logs", local.props.shared_db.connection_name]

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
    # Owners of the ignored fields: deploy.yml sets the image; gcloud sets client, client_version and labels.
    # Scaling is not ignored: Terraform owns it (api_scaling_by_environment).
    ignore_changes = [template[0].containers[0].image, client, client_version, labels]

    precondition {
      condition     = local.signon_on || local.entra_on
      error_message = "Turn on at least one sign-in provider for ${local.env}: oidc_by_environment (BCC Signon) or entra_by_environment (Entra ID) in infra/cloudrun_api.tf."
    }

    precondition {
      condition     = local.signon_on ? local.oidc.client_id != local.oidc_client_placeholder : true
      error_message = "Set the BCC Signon client id for ${local.env} in oidc_by_environment (infra/cloudrun_api.tf); see docs/runbooks/first-deploy.md."
    }

    precondition {
      condition     = local.env != "prod" || local.api_ingress == "INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER"
      error_message = "Prod must use INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER so the run.app URL does not bypass the gateway (standards 03.15, 04.2, 26.7)."
    }

    precondition {
      condition     = local.env != "prod" || length(var.alert_emails) > 0
      error_message = "alert_emails must be set for prod: an alert policy without a recipient alerts nobody (standard 22.13)."
    }
  }
}

# The gateway sends no identity token, so the service allows unauthenticated invocations and ingress limits who can
# reach it. Pub/Sub push and Cloud Scheduler need no invoker role of their own: /internal/** validates their OIDC
# tokens in the app.
resource "google_cloud_run_v2_service_iam_member" "api_invoker" {
  name     = google_cloud_run_v2_service.api.name
  location = google_cloud_run_v2_service.api.location
  role     = "roles/run.invoker"
  member   = "allUsers"
}
