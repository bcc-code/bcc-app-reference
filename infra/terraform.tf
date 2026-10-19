terraform {
  required_version = ">= 1.9.0"

  # Partial configuration: the bucket and prefix are passed with -backend-config so the same code serves every
  # environment (see README, "State"). infra.yml passes them in CI.
  backend "gcs" {}

  required_providers {
    google      = { source = "hashicorp/google", version = "~> 7.0" }
    google-beta = { source = "hashicorp/google-beta", version = "~> 7.0" }
  }
}
