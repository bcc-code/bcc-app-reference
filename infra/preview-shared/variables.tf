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
