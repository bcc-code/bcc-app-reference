# The preview's identity provider (api/OidcMock). Its client's redirect URIs come from Client:Urls, so one instance
# serves one API URL. Clients and authorization codes live in memory, so it is pinned to one instance. Its signing and
# encryption keys are in the preview's own schema (OidcMock__Schema) of the shared database, reached through the Auth Proxy
# like the API does, so a cold start after scale to zero keeps the JWKS the API has cached.
resource "google_cloud_run_v2_service" "oidcmock" {
  depends_on = [
    google_project_iam_member.mock_runtime,
    google_cloud_identity_group_membership.mock,
    google_service_account_iam_member.cicd_acts_as,
  ]

  name                = local.oidcmock_service
  location            = local.region
  ingress             = "INGRESS_TRAFFIC_ALL"
  labels              = local.labels
  deletion_protection = false

  template {
    service_account = google_service_account.mock.email
    labels          = local.labels

    scaling {
      min_instance_count = 0
      max_instance_count = 1
    }

    containers {
      name  = "oidc"
      image = var.oidcmock_image
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

      env {
        name  = "Client__Urls__0"
        value = local.api_url
      }

      # No password: the proxy authenticates the runtime account (IAM database user = its email without
      # ".gserviceaccount.com").
      env {
        name  = "ConnectionStrings__template"
        value = "Host=127.0.0.1;Port=5432;Database=${local.props.shared_db.db_name};Username=${trimsuffix(google_service_account.mock.email, ".gserviceaccount.com")};Maximum Pool Size=2"
      }

      env {
        name  = "OidcMock__Schema"
        value = local.slug
      }

      # Without forwarded headers OpenIddict sees http behind Cloud Run's TLS termination and rejects every request.
      env {
        name  = "ASPNETCORE_FORWARDEDHEADERS_ENABLED"
        value = "true"
      }

      startup_probe {
        tcp_socket {
          port = 8080
        }
        timeout_seconds   = 1
        period_seconds    = 1
        failure_threshold = 30
      }

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

# The browser and the API both call the discovery and token endpoints.
resource "google_cloud_run_v2_service_iam_member" "oidcmock_invoker" {
  name     = google_cloud_run_v2_service.oidcmock.name
  location = google_cloud_run_v2_service.oidcmock.location
  role     = "roles/run.invoker"
  member   = "allUsers"
}
