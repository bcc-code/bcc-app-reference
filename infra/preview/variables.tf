variable "project_id" {
  type        = string
  description = "The sandbox project. Previews run next to sandbox, never in prod."
}

variable "region" {
  type = string
}

variable "pr_number" {
  type        = string
  description = "Pull request number. Digits only: a typo must not address another preview."
  validation {
    condition     = can(regex("^[0-9]+$", var.pr_number))
    error_message = "pr_number must be a plain number."
  }
}

variable "api_image" {
  type        = string
  description = "Full image reference of this PR's build (tag pr<N>-<sha>). Ignored by a destroy."
}

variable "oidc_mock_image" {
  type        = string
  description = "Image of the OIDC mock that the preview logs in through. Never the real identity provider (standard 07 rule 8)."
  default     = ""
}
