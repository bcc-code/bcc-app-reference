terraform {
  required_version = ">= 1.9.0"

  # One workspace per PR (pr<N>), created by preview.yml. Bucket and prefix come from -backend-config.
  backend "gcs" {}

  required_providers {
    google = { source = "hashicorp/google", version = "~> 7.0" }
  }
}
