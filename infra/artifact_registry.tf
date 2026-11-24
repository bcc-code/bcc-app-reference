resource "google_artifact_registry_repository" "main" {
  depends_on    = [google_project_service.services]
  repository_id = "ar-${local.app}-${local.env}"
  format        = "DOCKER"
  description   = "Container images for ${local.env}"
  location      = local.region

  # Before changing a rule, set dry run back to true and check what it would delete (standard 03.19).
  # A new app's first apply only reports what the policies would delete. Flip this to false in a follow-up change after reading
  # the report (docs/runbooks/first-deploy.md). An app whose policy has proven itself runs with false.
  cleanup_policy_dry_run = true

  # KEEP wins over DELETE: an image is deleted only when older than 90 days and not among the 20 newest, so the
  # serving image and recent rollback targets stay.
  cleanup_policies {
    id     = "delete-older-than-90-days"
    action = "DELETE"
    condition {
      tag_state  = "ANY"
      older_than = "7776000s"
    }
  }

  cleanup_policies {
    id     = "keep-20-most-recent"
    action = "KEEP"
    most_recent_versions {
      keep_count = 20
    }
  }
}
