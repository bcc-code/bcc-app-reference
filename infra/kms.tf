# Wraps the ASP.NET Core Data Protection keys (session ticket protection) that the app stores in the
# database. The app gets the key name from Gcp__DataProtectionKmsKeyName.
resource "google_kms_key_ring" "api" {
  depends_on = [google_project_service.services] # the API must be on before first use
  name       = "${local.app}-${local.env}"
  location   = local.region
}

resource "google_kms_crypto_key" "data_protection" {
  name            = "data-protection"
  key_ring        = google_kms_key_ring.api.id
  rotation_period = "7776000s" # 90 days
  lifecycle {
    # Key rings and keys cannot be deleted in GCP; destroying one in state would orphan it and lose the ability to
    # decrypt the stored Data Protection keys (every session would end).
    prevent_destroy = true
  }
}

# On this key only, not project-wide (standard 04.10).
resource "google_kms_crypto_key_iam_member" "api_data_protection" {
  crypto_key_id = google_kms_crypto_key.data_protection.id
  role          = "roles/cloudkms.cryptoKeyEncrypterDecrypter"
  member        = "serviceAccount:${google_service_account.api.email}"
}
