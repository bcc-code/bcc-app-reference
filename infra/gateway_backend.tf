# Connects the API and the assets bucket to the BCC platform gateway. The route itself (host name, which paths go to the
# bucket) is registered in the gateway repository as routes/<app>.yaml, with this shape (standard 26):
#
#   backends:
#     api:    { type: cloud_run }       # bs-<app>-api-<env>
#     assets: { type: backend_bucket }  # bb-<app>-assets-<env>
#   hosts:
#     - name: web
#       default_backend: api            # the API serves index.html
#       routes:
#         - paths: ["/assets", "/assets/*"]
#           backend: assets
#
# Apply order: apply this file first, then merge the route; remove the route before destroying or renaming a backend.
#
# The modules are pinned to a commit of bcc-code/bcc-platform-gateway-tf-modules. The repository has no release tags yet
# (checked 2026-10-19); switch to a tag when the platform team publishes one. Dependabot's terraform ecosystem does not
# follow SHA refs, so update the pin by hand.
module "gateway_backend_api" {
  source = "git::https://github.com/bcc-code/bcc-platform-gateway-tf-modules//gateway-backend?ref=69e6a4923b19858b9061e3679172d4b598764536"

  app         = var.gateway_app
  component   = "api"
  environment = local.env

  cloud_run_service = google_cloud_run_v2_service.api.name
  region            = google_cloud_run_v2_service.api.location # must be the service's own region (standard 26 rule 3)
}

module "gateway_backend_assets" {
  source = "git::https://github.com/bcc-code/bcc-platform-gateway-tf-modules//gateway-backend-bucket?ref=69e6a4923b19858b9061e3679172d4b598764536"

  app         = var.gateway_app
  component   = "assets"
  environment = local.env

  bucket = google_storage_bucket.assets.name
  # Only one module instance per project may hold the project-scoped gateway grant (standard 26 rule 4): the API one.
  grant_gateway_access = false
  # Lets the load balancer's service agent read the private bucket. Can fail on the first apply, because the agent is
  # created with the project's first backend bucket; apply again.
  grant_object_access = true
}
