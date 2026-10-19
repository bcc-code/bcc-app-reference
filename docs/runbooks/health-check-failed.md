# Health check failing

Alert: `template <env>: /health check failing`. The uptime check on `https://<public host>/health` failed from more than one
location for 5 minutes. `/health` runs all checks, including the database. `/alive` is a self check only and is what Cloud Run
uses for liveness, so a database outage fails `/health` without restarting instances.

1. Open the URL yourself. Outcomes:
   - Does not resolve, TLS error, or a gateway error page (404 or 502 from the gateway, not from the app): the problem is the
     gateway route or the Cloud Run ingress, not the app. Check that the route for `<app>` exists in the gateway repository and
     that the backend `bs-template-api-<env>` exists. Cloud Run ingress must be `internal-and-cloud-load-balancing`.
   - 503 or a body that names a failing check: read the body. It lists which check failed.
2. Database check failing: look at the Cloud SQL instance status and the Auth Proxy sidecar logs of the API revision (container
   `cloud-sql-proxy`). Typical causes are an instance restart or maintenance, a revoked `roles/cloudsql.instanceUser`, or a
   changed IAM database user.
3. Service not running: check that revisions are ready. A failed deploy leaves the previous revision serving; a failed startup
   shows in the revision's logs. Roll back if the last revision is bad ([rollback.md](rollback.md)).
4. Sandbox only: with scale to zero the first check after idle waits for a cold start. A single failed check there is not an
   incident; the alert needs more than one location failing.
5. If the cause is the OIDC provider, `/health` does not check it, so it is not this alert.
