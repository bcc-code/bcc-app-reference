terraform {
  # State is in the platform's Azure storage account; the platform's terraform.yml passes the backend configuration.
  backend "azurerm" {}

  # The platform's terraform.yml runs 1.15.x. Keep this in step when the platform moves.
  required_version = "~> 1.15"

  required_providers {
    google = { source = "hashicorp/google", version = "~> 7.0" }
    random = { source = "hashicorp/random", version = "~> 3.0" }
  }
}
