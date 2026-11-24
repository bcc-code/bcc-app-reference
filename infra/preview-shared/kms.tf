# Wraps the ASP.NET Core Data Protection keys that every preview stores in its schema. The previews look the key up by name.
resource "google_kms_key_ring" "main" {
  depends_on = [google_project_service.services] # the API must be on before first use
  name       = "${local.app}-${local.env}"
  location   = local.region
}

# Standard 03.16: KMS keys are never destroyed by Terraform. GCP cannot delete a key ring or a key name anyway, so a destroy
# would only disable the versions and break every preview that still has keys wrapped by them.
resource "google_kms_crypto_key" "data_protection" {
  name            = "data-protection"
  key_ring        = google_kms_key_ring.main.id
  rotation_period = "7776000s" # 90 days

  lifecycle {
    prevent_destroy = true
  }
}
