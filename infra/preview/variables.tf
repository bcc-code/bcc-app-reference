# The platform's terraform.yml sets these two (TF_VAR_props, TF_VAR_deployment_props).
variable "props" {
  type        = string
  description = "JSON with the project properties from bcc-platform-deploy (project_name, app_environment, gcp.project_id, gcp.location)."
  validation {
    condition     = can(jsondecode(var.props))
    error_message = "props must be valid JSON."
  }
}

variable "deployment_props" {
  type        = string
  description = "JSON with the deployment properties from bcc-platform-deploy (repo_name, repo_owner, repo_path, component_name)."
  validation {
    condition     = can(jsondecode(var.deployment_props))
    error_message = "deployment_props must be valid JSON."
  }
}

variable "pr_number" {
  type        = string
  description = "The pull request number. Goes on every resource as a label."
  validation {
    condition     = can(regex("^[0-9]+$", var.pr_number))
    error_message = "pr_number must be digits only."
  }
  # Labels and names must agree, or teardown could miss this preview's resources.
  validation {
    condition     = "pr${var.pr_number}" == merge(jsondecode(var.props), jsondecode(var.deployment_props)).component_name
    error_message = "pr_number must match the component name (pr<N>)."
  }
}

# Full references, by digest or tag, from the preview Artifact Registry repository.
variable "api_image" {
  type        = string
  description = "The API image."
}

variable "oidcmock_image" {
  type        = string
  description = "The OIDC mock image."
}
