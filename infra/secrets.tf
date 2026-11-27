# The OIDC client secret of BCC Signon. The value is never in Terraform state or GitHub: a person creates the secret and
# its first version with gcloud before the first deploy (docs/runbooks/first-deploy.md):
#   printf '%s' "<value>" | gcloud secrets create bcc-template-oidc-client-secret --data-file=- --project <project>
# Terraform only reads the secret and grants access to it. The apply fails until the secret exists. Only where BCC Signon is on.
data "google_secret_manager_secret" "oidc_client_secret" {
  count      = local.signon_on ? 1 : 0
  depends_on = [google_project_service.services] # the API must be on before first use
  secret_id  = "${local.app}-oidc-client-secret"
}

# On this secret only, not project-wide (standard 04.10).
resource "google_secret_manager_secret_iam_member" "api_oidc_client_secret" {
  count     = local.signon_on ? 1 : 0
  secret_id = data.google_secret_manager_secret.oidc_client_secret[0].id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.api.email}"
}

# The client secret of the Entra app registration, created by hand the same way (ADR 0031):
#   printf '%s' "<value>" | gcloud secrets create bcc-template-entra-client-secret --data-file=- --project <project>
# Only where Entra is on.
data "google_secret_manager_secret" "entra_client_secret" {
  count      = local.entra_on ? 1 : 0
  depends_on = [google_project_service.services]
  secret_id  = "${local.app}-entra-client-secret"
}

resource "google_secret_manager_secret_iam_member" "api_entra_client_secret" {
  count     = local.entra_on ? 1 : 0
  secret_id = data.google_secret_manager_secret.entra_client_secret[0].id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.api.email}"
}

# The first administrators (ADR 0031): one string of <provider>:<id> entries, for example
# "entra:<object id>,bcc-signon:<personUid>". The ids belong to real people, so the value is not in the repository or in Terraform state.
# Created by hand like the client secrets; only where bootstrap_admins_secret_by_environment turns it on:
#   printf '%s' "entra:<object id>" | gcloud secrets create bcc-template-bootstrap-admins --data-file=- --project <project>
data "google_secret_manager_secret" "bootstrap_admins" {
  count      = local.bootstrap_admins_secret_on ? 1 : 0
  depends_on = [google_project_service.services]
  secret_id  = "${local.app}-bootstrap-admins"
}

resource "google_secret_manager_secret_iam_member" "api_bootstrap_admins" {
  count     = local.bootstrap_admins_secret_on ? 1 : 0
  secret_id = data.google_secret_manager_secret.bootstrap_admins[0].id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.api.email}"
}

# The BCC monitoring collector's auth key, as the header the OTLP exporter sends. Unlike the client secret above, nobody
# here can read the value to enter it by hand: it is an org-level GitHub secret that the platform team rotates. deploy.yml
# and infra.yml pass it in, and the version is written write-only, so the value is not in state either (standard 03.18).
resource "google_secret_manager_secret" "otel_exporter_otlp_headers" {
  depends_on = [google_project_service.services]
  secret_id  = "${local.app}-otel-exporter-otlp-headers"

  replication {
    auto {}
  }
}

resource "google_secret_manager_secret_version" "otel_exporter_otlp_headers" {
  secret         = google_secret_manager_secret.otel_exporter_otlp_headers.id
  secret_data_wo = "Authorization=AuthKey ${var.otel_exporter_otlp_auth_key}"
  # A write-only value is written again only when this number changes. It is taken from the key (28 bits of its SHA-256,
  # nothing to recover a key from), so a rotated key gets a new version on the next apply without anyone bumping it.
  secret_data_wo_version = parseint(substr(sha256(var.otel_exporter_otlp_auth_key), 0, 7), 16)

  # A new number replaces the version. Cloud Run reads the secret when an instance starts and does not start one it cannot
  # read, and the API scales to zero, so the new version must exist before the old one is destroyed. Running instances
  # keep the old key until they are replaced.
  lifecycle {
    create_before_destroy = true
  }
}

resource "google_secret_manager_secret_iam_member" "api_otel_exporter_otlp_headers" {
  secret_id = google_secret_manager_secret.otel_exporter_otlp_headers.id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.api.email}"
}
