# Wraps the ASP.NET Core Data Protection keys that every preview stores in its schema. The previews look the key up by name.
resource "google_kms_key_ring" "main" {
  depends_on = [google_project_service.services] # the API must be on before first use
  name       = "${local.app}-${local.env}"
  location   = local.region
}

# No prevent_destroy: a preview's keys protect nothing durable.
resource "google_kms_crypto_key" "data_protection" {
  name            = "data-protection"
  key_ring        = google_kms_key_ring.main.id
  rotation_period = "7776000s" # 90 days
}
