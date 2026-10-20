# Per-PR preview environment (standard 07). Applied by .github/workflows/preview.yml into workspace pr<N>.
#
# This is a starting point and does not yet describe a complete preview. TODO for the project that adopts it:
#   - a database on the shared preview Cloud SQL instance (one database and user per PR, standard 07 rules 6 and 20);
#   - a Cloud Run job that runs the migrations, executed after every apply;
#   - the OIDC mock service that the API logs in through (var.oidc_mock_image);
#   - the Auth Proxy sidecar and the connection string, as in ../cloudrun_api.tf.
#   - messaging: the Pub/Sub topic, push subscription and durability pass job of ../pubsub.tf and ../scheduler.tf, with the
#     Messaging__* and Internal__* settings of ../cloudrun_api.tf. The preview has no database, so it has no outbox either.
# Every resource is named <app>-<thing>-pr<N> and labelled with the PR, so a stray resource can be traced to its PR.

locals {
  slug = "pr${var.pr_number}"
  labels = {
    app = "template"
    pr  = var.pr_number
  }
}

provider "google" {
  project = var.project_id
  region  = var.region
}

# Created once by ../iam.tf (preview_runtime). The preview workflow may act as this account and no other, so a preview cannot
# run as the sandbox API or the Terraform accounts.
data "google_service_account" "runtime" {
  account_id = "sa-template-preview-run"
}

resource "google_cloud_run_v2_service" "api" {
  name                = "template-api-${local.slug}"
  location            = var.region
  labels              = local.labels
  deletion_protection = false # throwaway: the whole workspace is destroyed with the PR

  # TODO: previews should not be publicly reachable (standard 07 rule 19). Put them behind IAP or Cloud Armor, or at least
  # require the app login on every route. INGRESS_TRAFFIC_ALL is the value that works without the gateway.
  ingress = "INGRESS_TRAFFIC_ALL"

  template {
    service_account = data.google_service_account.runtime.email
    labels          = local.labels
    scaling {
      min_instance_count = 0 # previews scale to zero (standard 07 rule 9)
      max_instance_count = 2
    }
    containers {
      name  = "app"
      image = var.api_image
      # preview.yml bakes the built frontend into wwwroot; see ../cloudrun_api.tf.
      env {
        name  = "Frontend__StaticPath"
        value = "/app/wwwroot"
      }
      ports {
        container_port = 8080
      }
      resources {
        cpu_idle          = true
        startup_cpu_boost = true
        limits            = { cpu = "1000m", memory = "512Mi" }
      }
      startup_probe {
        tcp_socket {
          port = 8080
        }
        timeout_seconds   = 240
        period_seconds    = 240
        failure_threshold = 1
      }
      liveness_probe {
        http_get {
          path = "/alive"
          port = 8080
        }
      }
    }
  }
}

# The preview has its own login (the OIDC mock) and holds generated test data only, so the service itself is open and the
# app enforces login. This is why previews must never receive production or personal data.
resource "google_cloud_run_v2_service_iam_member" "invoker" {
  name     = google_cloud_run_v2_service.api.name
  location = google_cloud_run_v2_service.api.location
  role     = "roles/run.invoker"
  member   = "allUsers"
}
