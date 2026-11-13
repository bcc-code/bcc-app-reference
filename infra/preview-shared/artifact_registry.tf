resource "google_artifact_registry_repository" "main" {
  depends_on    = [google_project_service.services]
  repository_id = "ar-${local.app}-${local.env}"
  format        = "DOCKER"
  description   = "Container images for the PR previews"
  location      = local.region

  # Dry run until the policy has been checked against real tags (standards 03.19, 07.18).
  cleanup_policy_dry_run = true

  # Preview images are tagged pr<N>-<sha>.
  cleanup_policies {
    id     = "delete-pr-tags-older-than-14-days"
    action = "DELETE"
    condition {
      tag_state    = "TAGGED"
      tag_prefixes = ["pr"]
      older_than   = "1209600s"
    }
  }
}
