locals {
  migrate_connection_string = "Host=127.0.0.1;Port=5432;Database=${local.db_name};Username=${google_sql_user.migrate.name}"
}

# Applies the database schema before a new revision gets traffic. Never run at API startup (standard 04 rule 9, standard 12).
# deploy.yml updates this job to the new image and executes it with --wait, so a failed migration stops the rollout.
resource "google_cloud_run_v2_job" "migrate" {
  depends_on          = [google_project_service.services]
  name                = local.migrate_job_name
  location            = var.region
  deletion_protection = local.cfg.service_deletion_protect

  template {
    template {
      service_account = google_service_account.migrate.email
      max_retries     = 0 # a half-applied migration must be looked at, not repeated blindly
      timeout         = "600s"

      containers {
        name       = "app"
        image      = local.image_placeholder
        depends_on = ["cloud-sql-proxy"]
        # The image runs the schema step when started with these arguments (var.migrate_args).
        args = var.migrate_args
        resources {
          limits = { cpu = "1000m", memory = "512Mi" }
        }
        # The schema step runs the API's own host, so it needs the same settings as the API (it validates the OIDC settings
        # outside Development), with the connection string of the migration user.
        dynamic "env" {
          for_each = merge(local.api_env, { ConnectionStrings__template = local.migrate_connection_string })
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
      }

      containers {
        name  = "cloud-sql-proxy"
        image = local.sql_proxy_image
        args  = ["--address=0.0.0.0", "--port=5432", "--auto-iam-authn", "--structured-logs", google_sql_database_instance.main.connection_name]
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

  lifecycle {
    # The image is owned by the deploy workflow, which sets it to the digest being deployed before each execution.
    ignore_changes = [template[0].template[0].containers[0].image, client, client_version]
  }
}
