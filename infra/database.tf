# The database is the platform's shared Cloud SQL instance (landing zone, use-shared-db), not part of this state. The
# group in props.shared_db.admin_group_id carries the database-side access; members connect with IAM database
# authentication (standard 03.14). The landing zone grants the group roles/cloudsql.client and roles/cloudsql.instanceUser
# on the instance's project, so no Cloud SQL role is needed in this project.
resource "google_cloud_identity_group_membership" "shared_db_access" {
  for_each = {
    # The runtime account connects through the Auth Proxy sidecar as an IAM database user.
    api = google_service_account.api.email
    # The CI account applies the schema (db-apply) from deploy.yml before the rollout, and then moves ownership of the
    # objects it created to the database owner role.
    cicd = data.google_service_account.cicd.email
  }
  group = local.props.shared_db.admin_group_id
  preferred_member_key {
    id = each.value
  }
  roles {
    name = "MEMBER"
  }
}
