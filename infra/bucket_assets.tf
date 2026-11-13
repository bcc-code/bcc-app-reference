# Content-hashed frontend bundles, served through the gateway at /assets/* (backend bucket in gateway_backend.tf).
#
# From the bucket: assets/* (the Vite output: hashed JS, CSS, fonts and images), uploaded by deploy.yml before each rollout.
# From the API image: index.html and any other file in the dist root, so the shell and the API it calls are always
# deployed together (standard 26.15).
resource "google_storage_bucket" "assets" {
  depends_on                  = [google_project_service.services]
  name                        = "${local.project_id}-assets" # bucket names are global; the project id is unique
  location                    = local.region
  uniform_bucket_level_access = true
  public_access_prevention    = "enforced"

  # Only rebuildable bundles live here, but a Cloud Run rollback needs the old ones, so the bucket is not treated as
  # disposable: destroy fails while it holds objects and someone has to decide (standard 03.16).
  force_destroy = false

  # No CORS: nothing loads these files from another origin (standard 26.14).

  # Protects against an overwrite or delete by the CI account, which has objectUser. There is no age rule on live
  # objects: an unchanged bundle is not uploaded again, so it would age out while a revision still references it.
  versioning {
    enabled = true
  }

  lifecycle_rule {
    action {
      type = "Delete"
    }
    condition {
      days_since_noncurrent_time = 30
      with_state                 = "ARCHIVED"
    }
  }
}

# deploy.yml uploads with gcloud storage rsync (no delete). objectUser can create and overwrite objects but not
# change bucket IAM (standard 26.11).
resource "google_storage_bucket_iam_member" "cicd_assets" {
  bucket = google_storage_bucket.assets.name
  role   = "roles/storage.objectUser"
  member = "serviceAccount:${data.google_service_account.cicd.email}"
}
