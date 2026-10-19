resource "google_sql_database_instance" "main" {
  depends_on          = [google_project_service.services]
  name                = "${local.app}-${local.env}"
  region              = var.region
  database_version    = "POSTGRES_17"
  deletion_protection = local.cfg.db_deletion_protection # provider-side guard; sandbox is disposable, prod is not

  settings {
    edition           = "ENTERPRISE" # the shared-core tier of sandbox requires it
    tier              = local.cfg.db_tier
    availability_type = local.cfg.db_availability

    # Runtimes log in as IAM database users, so no database password exists for the services (standard 03, rule 14).
    database_flags {
      name  = "cloudsql.iam_authentication"
      value = "on"
    }

    backup_configuration {
      enabled                        = true
      point_in_time_recovery_enabled = local.cfg.db_point_in_time_recovery
    }

    # Public IP, but no authorized networks: the only way in is the Cloud SQL Auth Proxy with an IAM identity.
    # TODO: if the landing zone provides a private network, switch to private IP and remove the public address.
    ip_configuration {
      ipv4_enabled = true
      ssl_mode     = "ENCRYPTED_ONLY"
    }

    insights_config {
      query_insights_enabled = true
    }

    deletion_protection_enabled = local.cfg.db_deletion_protection # API-side guard
  }

  lifecycle {
    # Stateful resource (standard 03, rule 16). To retire an environment, remove this line in a separate reviewed commit.
    prevent_destroy = true
  }
}

resource "google_sql_database" "app" {
  name     = local.db_name
  instance = google_sql_database_instance.main.name
}

# IAM database users are the service account email without ".gserviceaccount.com".
resource "google_sql_user" "api" {
  instance = google_sql_database_instance.main.name
  name     = trimsuffix(google_service_account.api.email, ".gserviceaccount.com")
  type     = "CLOUD_IAM_SERVICE_ACCOUNT"
}

resource "google_sql_user" "migrate" {
  instance = google_sql_database_instance.main.name
  name     = trimsuffix(google_service_account.migrate.email, ".gserviceaccount.com")
  type     = "CLOUD_IAM_SERVICE_ACCOUNT"
}

# NOT DONE HERE: privileges inside the database. An IAM user starts with login only. Before the first migration an admin
# must let the migration user create objects in the schema and let the API user use them. The SQL is in infra/README.md
# ("Database privileges"). Terraform has no resource for it without a PostgreSQL provider and network access to the instance.
