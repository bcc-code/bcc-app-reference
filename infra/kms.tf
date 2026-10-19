# Encrypts the ASP.NET Core Data Protection keys that are stored in Postgres (standard 04, rule 11).
resource "google_kms_key_ring" "main" {
  depends_on = [google_project_service.services] # the API must be on before first use
  name       = "${local.app}-${local.env}"
  location   = var.region
}

resource "google_kms_crypto_key" "data_protection" {
  name            = "data-protection"
  key_ring        = google_kms_key_ring.main.id
  rotation_period = "7776000s" # 90 days
  lifecycle {
    # A destroyed key makes every stored Data Protection key, and so every session cookie, unreadable.
    prevent_destroy = true
  }
}

resource "google_kms_crypto_key_iam_member" "api_data_protection" {
  crypto_key_id = google_kms_crypto_key.data_protection.id
  role          = "roles/cloudkms.cryptoKeyEncrypterDecrypter"
  member        = "serviceAccount:${google_service_account.api.email}"
}
