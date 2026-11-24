# The database is the platform's shared Cloud SQL instance (landing zone, use-shared-db). The CI account applies each
# preview's schema (db-apply) as a member of the access group; the runtime accounts join it in infra/preview.
data "google_service_account" "cicd" {
  account_id = "sa-gha-cicd"
}

resource "google_cloud_identity_group_membership" "cicd" {
  group = local.props.shared_db.admin_group_id
  preferred_member_key {
    id = data.google_service_account.cicd.email
  }
  roles {
    name = "MEMBER"
  }
}
