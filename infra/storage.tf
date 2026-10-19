# Content-hashed frontend bundles, served by the gateway under /assets. Only these come from the bucket. index.html and
# everything else come from the API image, so the SPA shell, the CSP nonce and the API are always deployed together
# (standard 26, rules 2 and 15).
resource "google_storage_bucket" "assets" {
  depends_on                  = [google_project_service.services]
  name                        = local.assets_bucket_name
  location                    = "EU"
  uniform_bucket_level_access = true
  public_access_prevention    = "enforced" # the gateway's service agent reads it; nobody else (standard 26 rule 10)

  # No CORS block: nothing loads these files from another origin (standard 26 rule 14).

  lifecycle {
    # Old bundles must stay for a Cloud Run traffic rollback (standard 04 rule 12), so the bucket is not disposable.
    prevent_destroy = true
  }
}
