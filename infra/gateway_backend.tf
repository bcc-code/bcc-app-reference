# Backends for the BCC platform gateway. The route itself is in bcc-platform-gateway, routes/<app>.yaml
# (the hostname is in the route file): default backend `api`, /assets and /assets/* to `assets` (standard 26.2).
#
# Name contract: the gateway finds these by constructed name, so app and component must match the route file.
#   app "<app>" + component "api"    -> backendServices/bs-<app>-api-<env>
#   app "<app>" + component "assets" -> backendBuckets/bb-<app>-assets-<env>
# A mismatch is not caught until the gateway plan.
#
# Order: apply this first, then merge the route PR. Remove the route before renaming or destroying a backend (26.5).
#
# Both modules are pinned to the commit of 2026-09-08 ("enable CDN by default"); the repository has no tags
# (standards 03.5, 26.6).
module "gateway_backend_api" {
  source = "git::https://github.com/bcc-code/bcc-platform-gateway-tf-modules//gateway-backend?ref=69e6a4923b19858b9061e3679172d4b598764536"

  app         = local.app
  component   = "api"
  environment = local.env

  cloud_run_service = google_cloud_run_v2_service.api.name
  # A NEG in another region than the service resolves to nothing at request time (standard 26.3).
  region = google_cloud_run_v2_service.api.location
  # This instance keeps the default grant_gateway_access = true, the project-scoped gateway grant. The bucket
  # instance below must not hold it too (standard 26.4).
}

module "gateway_backend_assets" {
  source = "git::https://github.com/bcc-code/bcc-platform-gateway-tf-modules//gateway-backend-bucket?ref=69e6a4923b19858b9061e3679172d4b598764536"

  app         = local.app
  component   = "assets"
  environment = local.env

  bucket = google_storage_bucket.assets.name
  # Lets the load balancer's service agent read the private bucket (standard 26.10). It can fail on the first apply,
  # because the agent is created with the project's first backend bucket; apply again.
  grant_object_access  = true
  grant_gateway_access = false
}
