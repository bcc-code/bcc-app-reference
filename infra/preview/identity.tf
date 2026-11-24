# The API's runtime account. It connects through the Auth Proxy as an IAM database user (its email without
# ".gserviceaccount.com").
resource "google_service_account" "api" {
  account_id   = "sa-${local.app}-${local.slug}"
  display_name = "${local.app}-api (${local.slug})"
}

# The OIDC mock gets an account of its own. It reaches the database for its signing keys, but holds neither the API's KMS key nor
# the client secret.
resource "google_service_account" "mock" {
  account_id   = "sa-${local.app}-mock-${local.slug}"
  display_name = "${local.app} OIDC mock (${local.slug})"
}

locals {
  # A preview sends no traces or metrics to the BCC collector, so the accounts only write logs.
  runtime_roles = [
    "roles/cloudsql.client",
    "roles/logging.logWriter",
  ]
  mock_roles = [
    "roles/cloudsql.client",
    "roles/logging.logWriter",
  ]
}

resource "google_project_iam_member" "api_runtime" {
  for_each = toset(local.runtime_roles)
  project  = local.project_id
  role     = each.value
  member   = "serviceAccount:${google_service_account.api.email}"
}

resource "google_project_iam_member" "mock_runtime" {
  for_each = toset(local.mock_roles)
  project  = local.project_id
  role     = each.value
  member   = "serviceAccount:${google_service_account.mock.email}"
}

# Deploying a service that runs as an account is checked as actAs on that account.
resource "google_service_account_iam_member" "cicd_acts_as" {
  for_each = {
    api  = google_service_account.api.name
    mock = google_service_account.mock.name
  }
  service_account_id = each.value
  role               = "roles/iam.serviceAccountUser"
  member             = "serviceAccount:${data.google_service_account.cicd.email}"
}

# Database access for the API: the shared instance's admin group as in infra/database.tf.
resource "google_cloud_identity_group_membership" "api" {
  group = local.props.shared_db.admin_group_id
  preferred_member_key {
    id = google_service_account.api.email
  }
  roles {
    name = "MEMBER"
  }
}

# Database access for the OIDC mock's signing keys, in the same group.
resource "google_cloud_identity_group_membership" "mock" {
  group = local.props.shared_db.admin_group_id
  preferred_member_key {
    id = google_service_account.mock.email
  }
  roles {
    name = "MEMBER"
  }
}

# On the shared key only, not project-wide.
resource "google_kms_crypto_key_iam_member" "api_data_protection" {
  crypto_key_id = data.google_kms_crypto_key.data_protection.id
  role          = "roles/cloudkms.cryptoKeyEncrypterDecrypter"
  member        = "serviceAccount:${google_service_account.api.email}"
}

# Identity the push subscription signs its OIDC token as. /internal/pubsub accepts only tokens from it.
resource "google_service_account" "pubsub_push" {
  account_id   = "sa-${local.app}-push-${local.slug}"
  display_name = "${local.app} Pub/Sub push to the API (${local.slug})"
}

# The Pub/Sub service agent mints the OIDC tokens for the push account.
resource "google_service_account_iam_member" "pubsub_push_token_creator" {
  service_account_id = google_service_account.pubsub_push.name
  role               = "roles/iam.serviceAccountTokenCreator"
  member             = local.pubsub_service_agent
}

# Identity the scheduler job signs its OIDC token as. /internal/scheduler accepts only tokens from it.
resource "google_service_account" "scheduler" {
  account_id   = "sa-${local.app}-sched-${local.slug}"
  display_name = "${local.app} Cloud Scheduler to the API (${local.slug})"
}

# The Cloud Scheduler service agent mints the OIDC tokens for the scheduler account.
resource "google_service_account_iam_member" "scheduler_token_creator" {
  service_account_id = google_service_account.scheduler.name
  role               = "roles/iam.serviceAccountTokenCreator"
  member             = "serviceAccount:service-${data.google_project.main.number}@gcp-sa-cloudscheduler.iam.gserviceaccount.com"
}
