variable "project_id" {
  type        = string
  description = "GCP project of this environment. One project per environment (sandbox, prod)."
}

variable "region" {
  type        = string
  description = "Region of Cloud Run, Cloud SQL, Artifact Registry and the KMS key ring."
}

variable "environment" {
  type        = string
  description = "Environment name. Selects the sizing in local.env_config."
  validation {
    condition     = contains(["sandbox", "prod"], var.environment)
    error_message = "environment must be sandbox or prod."
  }
}

variable "github_repository" {
  type        = string
  description = "The GitHub repository allowed to authenticate through Workload Identity Federation, as OWNER/REPO."
}

variable "state_bucket" {
  type        = string
  description = "Name of the GCS bucket that holds the Terraform state. Not managed here; the plan identity needs access to it."
}

variable "oidc_authority" {
  type        = string
  description = "Authority (issuer URL) of the OIDC provider used for the BFF login."
}

variable "oidc_client_id" {
  type        = string
  description = "Client id registered at the OIDC provider for this environment. Not secret."
}

variable "public_hostname" {
  type        = string
  default     = ""
  description = "Public host name served by the platform gateway (for example template.sandbox.example.com). Empty skips the uptime check."
}

variable "alert_emails" {
  type        = list(string)
  default     = []
  description = "Recipients of the monitoring alerts. Must be set for prod: an alert policy without a channel alerts nobody."
}

variable "runbook_base_url" {
  type        = string
  description = "URL of docs/runbooks on the default branch, without a trailing slash. Alerts link to <url>/<runbook>.md."
}

variable "image_reader_members" {
  type        = list(string)
  default     = []
  description = <<-EOT
    IAM members (serviceAccount:...) that may read this environment's Artifact Registry repository. Set it on sandbox to the
    prod deploy account, so the prod deploy can copy the image digest that ran in sandbox (docs/development.md, Promotion).
  EOT
}

variable "gateway_app" {
  type        = string
  default     = "template"
  description = "Application slug, the same as the app field of the route file in the platform gateway repository."
}

variable "migrate_args" {
  type        = list(string)
  default     = ["resources", "setup"]
  description = <<-EOT
    Arguments that make the API image apply the database schema and exit. The default is the Marten variant (JasperFx
    "resources setup", the same command the AppHost runs locally). For the EF Core variant use the command of its migration
    step, as the AppHost lists it for the schema resource: ["migrate"] (the API's own command, which applies the EF Core
    migrations and Wolverine's tables).
  EOT
}
