# The preview's secrets are generated per PR and held in Secret Manager. The values are also in this workspace's state, which
# is acceptable because they protect generated test data only. Containers read them through secret_key_ref.
locals {
  # The API reads the client secret as Oidc__ClientSecret (the host requires it outside Development and Test). The OIDC mock
  # registers a public client (PKCE, no secret) and ignores the value.
  secrets = {
    oidc-client-secret = { readers = [google_service_account.api] }
  }

  secret_readers = merge([
    for name, secret in local.secrets : {
      for reader in secret.readers : "${name}/${reader.account_id}" => { secret = name, member = reader.email }
    }
  ]...)
}

resource "random_password" "secret" {
  for_each = local.secrets
  length   = 48
  special  = false
}

resource "google_secret_manager_secret" "secret" {
  for_each  = local.secrets
  secret_id = "${local.app}-${each.key}-${local.slug}"
  labels    = local.labels

  replication {
    auto {}
  }
}

resource "google_secret_manager_secret_version" "secret" {
  for_each    = local.secrets
  secret      = google_secret_manager_secret.secret[each.key].id
  secret_data = random_password.secret[each.key].result
}

# On each secret only, not project-wide.
resource "google_secret_manager_secret_iam_member" "reader" {
  for_each  = local.secret_readers
  secret_id = google_secret_manager_secret.secret[each.value.secret].id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${each.value.member}"
}
