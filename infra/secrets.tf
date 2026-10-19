# Terraform creates the container and the accessor binding only. The value is added out of band so it never enters
# the Terraform state (standard 03, rule 18):
#   printf '%s' "<value>" | gcloud secrets versions add template-oidc-client-secret --data-file=- --project <project>
resource "google_secret_manager_secret" "oidc_client_secret" {
  depends_on = [google_project_service.services] # the API must be on before first use
  secret_id  = "${local.app}-oidc-client-secret"
  replication {
    auto {}
  }
}

resource "google_secret_manager_secret_iam_member" "api_oidc_client_secret" {
  secret_id = google_secret_manager_secret.oidc_client_secret.id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.api.email}"
}

# The schema step runs the API's own host, which reads the same client secret.
resource "google_secret_manager_secret_iam_member" "migrate_oidc_client_secret" {
  secret_id = google_secret_manager_secret.oidc_client_secret.id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.migrate.email}"
}
