resource "google_artifact_registry_repository" "main" {
  depends_on    = [google_project_service.services] # the API must be on before first use
  repository_id = "ar-${local.app}-${local.env}"
  format        = "DOCKER"
  description   = "Container images for ${local.env}"
  location      = var.region

  # FIRST APPLY ONLY. With true the policies below are evaluated and reported but delete nothing. Read the dry-run
  # report, compare the digests it lists with what live Cloud Run revisions and open previews reference, then set this
  # to false in a follow-up commit (standard 03, rule 19). A wrong policy deletes the image a rollback needs.
  cleanup_policy_dry_run = true

  # A KEEP policy always wins over a DELETE policy, so the 20 newest versions survive regardless of age. That keeps a
  # rarely redeployed prod image from ageing out under a running service.
  cleanup_policies {
    id     = "keep-recent-versions"
    action = "KEEP"
    most_recent_versions {
      keep_count = 20
    }
  }

  cleanup_policies {
    id     = "delete-untagged"
    action = "DELETE"
    condition {
      tag_state  = "UNTAGGED"
      older_than = "604800s" # 7 days
    }
  }

  # Also removes PR preview images (tagged pr<N>-<sha>, standard 07 rule 18) once they are older than 30 days.
  cleanup_policies {
    id     = "delete-old-versions"
    action = "DELETE"
    condition {
      tag_state  = "ANY"
      older_than = "2592000s" # 30 days
    }
  }
}

# Prod reads the sandbox image by digest to promote it (build once, deploy the same digest). Set on sandbox only.
resource "google_artifact_registry_repository_iam_member" "readers" {
  for_each   = toset(var.image_reader_members)
  repository = google_artifact_registry_repository.main.name
  location   = google_artifact_registry_repository.main.location
  role       = "roles/artifactregistry.reader"
  member     = each.value
}
