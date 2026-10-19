# The values a GitHub environment needs (infra/README.md, "GitHub environment variables").

output "workload_identity_provider" {
  description = "Value of the GCP_WORKLOAD_IDENTITY_PROVIDER variable."
  value       = google_iam_workload_identity_pool_provider.github.name
}

output "deploy_service_account" {
  description = "Value of the GCP_DEPLOY_SERVICE_ACCOUNT variable."
  value       = google_service_account.deploy.email
}

output "terraform_service_account" {
  description = "Value of the GCP_TERRAFORM_SERVICE_ACCOUNT variable."
  value       = google_service_account.terraform.email
}

output "plan_service_account" {
  description = "Value of the GCP_PLAN_SERVICE_ACCOUNT variable."
  value       = google_service_account.plan.email
}

output "artifact_registry_repository" {
  description = "Repository id inside the project (variable GCP_ARTIFACT_REPOSITORY)."
  value       = google_artifact_registry_repository.main.repository_id
}

output "api_service_name" {
  value = google_cloud_run_v2_service.api.name
}

output "migrate_job_name" {
  value = google_cloud_run_v2_job.migrate.name
}

output "assets_bucket" {
  value = google_storage_bucket.assets.name
}

output "database_connection_name" {
  value = google_sql_database_instance.main.connection_name
}

output "api_iam_database_user" {
  description = "Needed for the database privileges in infra/README.md."
  value       = google_sql_user.api.name
}

output "migrate_iam_database_user" {
  description = "Needed for the database privileges in infra/README.md."
  value       = google_sql_user.migrate.name
}

output "preview_service_account" {
  description = "Value of the GCP_PREVIEW_SERVICE_ACCOUNT variable of the GitHub environment preview. Sandbox only."
  value       = local.env == "sandbox" ? google_service_account.preview[0].email : null
}
