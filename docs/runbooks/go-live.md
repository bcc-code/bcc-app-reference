# Go-live checklist

Before the first production release of an application made from this template (standard 22, rule 17). Tick each item in the
pull request or ticket that releases it. The one-time steps for an environment are in [first-deploy.md](first-deploy.md).

## Platform

- [ ] Prod project is provisioned through the landing zone (`provision-project.yml`, `use-shared-db: true`) and [first-deploy.md](first-deploy.md) is done for prod.
- [ ] GitHub environment `prod` has required reviewers and deployment branches limited to `main`.
- [ ] `main` is protected: pull requests only, `ci` required, one approving review, the `sast` job (Semgrep) is part of `ci` and fails on any finding.
- [ ] Secret scanning and push protection are on for the repository.
- [ ] Gateway route is registered and live; Cloud Run ingress is the internal load balancer.
- [ ] `public_hostname_by_environment` is set so the `/health` uptime check exists, and `APP_BASE_URL` is set in the `prod` environment (its ingress is internal, so the smoke test goes through the gateway).
- [ ] Prod infrastructure is current: `infra.yml` (environment `prod`, `apply`) was run after the last change to `infra/`, because `deploy.yml` applies Terraform for sandbox only.

## Operations

- [ ] `ALERT_EMAILS_PROD` (a JSON list) is set, so `alert_emails` is not empty for prod (Terraform refuses to apply without it) and a test alert reaches the recipients.
- [ ] Every alert in `infra/monitoring.tf` links to a runbook in `docs/runbooks/` that matches the real setup.
- [ ] Startup and liveness probes are in place: TCP startup, `/alive` liveness without the database, `/health` for monitoring.
- [ ] Prod minimum instances and billing model are as intended (`api_scaling_by_environment` in `infra/cloudrun_api.tf`).
- [ ] Artifact Registry cleanup policies were read in dry-run mode, then switched on.
- [ ] Rollback was tried in sandbox once ([rollback.md](rollback.md)).
- [ ] Telemetry reaches the BCC collector from sandbox and the loss of spans was measured ([ADR 0026](../adrs/0026-telemetry-export-through-the-bcc-monitoring-collector.md)).

## Secrets and identity

- [ ] The BCC Signon client for prod is registered with the prod redirect and back-channel logout URLs (first-deploy, step 3).
- [ ] The client secret value is in Secret Manager (added out of band), not in Terraform state or the repository.
- [ ] The database admin group contains the runtime account and `sa-gha-cicd`, and the schema step ran in prod (`db-apply`, then `REASSIGN OWNED`).
- [ ] The Data Protection KMS key exists, and startup fails without its name outside Development.
- [ ] The OIDC mock and test personas are not deployed to prod, and the seeder does not run there (`SeedingGate`).
- [ ] An administrator signs in with MFA, and a person without it is refused.

## Data and privacy

- [ ] [The data inventory](../data-protection/data-inventory.md) is reviewed by BCC's privacy function and no longer marked as a draft.
- [ ] Retention periods and legal basis are agreed with the privacy function (standard 18, rule 4), and the erasure job exists and is tested.
- [ ] No production data is used for tests or previews.

## Release

- [ ] Sandbox ran the same image digest that is pushed to prod: the `deploy-prod` job pushes the sandbox run's tarball and stops when the digest differs. The smoke test passed in both.
- [ ] Schema changes are additive, or a stated exception exists ([rollback.md](rollback.md)).
- [ ] The release notes are generated from merged pull requests (the deploy workflow creates the release).
