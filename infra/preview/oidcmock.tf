# The preview's identity provider (api/OidcMock). Its client's redirect URIs come from Client:Urls, so one instance
# serves one API URL. It has no database: clients and tokens live in memory, signed with ephemeral keys, so it is
# pinned to one instance.
resource "google_cloud_run_v2_service" "oidcmock" {
  depends_on = [
    google_project_iam_member.mock_runtime,
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
  }
}

# The browser and the API both call the discovery and token endpoints.
resource "google_cloud_run_v2_service_iam_member" "oidcmock_invoker" {
  name     = google_cloud_run_v2_service.oidcmock.name
  location = google_cloud_run_v2_service.oidcmock.location
  role     = "roles/run.invoker"
  member   = "allUsers"
}
