output "api_url" {
  description = "Public URL of the preview application."
  value       = local.api_url
}

output "oidc_url" {
  description = "Public URL of the preview's mock identity provider."
  value       = local.oidc_url
}

output "schema" {
  description = "The preview's Postgres schema (Persistence__Schema)."
  value       = local.slug
}
