# Go-live checklist

Before the first production release of an application made from this template (standard 22, rule 17). Tick each item in the
pull request or ticket that releases it.

## Platform

- [ ] Prod project exists, state bucket exists, and `infra/README.md` bootstrap is done for prod.
- [ ] GitHub environment `prod` has required reviewers and deployment branches limited to `main`.
- [ ] `main` is protected: pull requests only, `ci` required, one approving review, CodeQL results with high severity blocking.
- [ ] Secret scanning and push protection are on for the repository.
- [ ] Gateway route is registered and live; Cloud Run ingress is the internal load balancer.
- [ ] `public_hostname` is set so the `/health` uptime check exists.

## Operations

- [ ] `alert_emails` is set for prod (Terraform refuses to apply without it) and a test alert reaches the recipients.
- [ ] Every alert in `infra/monitoring.tf` links to a runbook in `docs/runbooks/` that matches the real setup.
- [ ] Startup and liveness probes are in place: TCP startup, `/alive` liveness without the database, `/health` for monitoring.
- [ ] Prod minimum instances and billing model are as intended (`env_config` in `infra/main.tf`).
- [ ] Artifact Registry cleanup policies were read in dry-run mode, then switched on.
- [ ] Rollback was tried in sandbox once ([rollback.md](rollback.md)).

## Secrets and identity

- [ ] OIDC client for prod is registered with the prod redirect and back-channel logout URLs.
- [ ] OIDC client secret value is in Secret Manager (added out of band), not in Terraform state or the repository.
- [ ] Database privileges for the migration and API users are granted (`infra/README.md`).
- [ ] The Data Protection KMS key exists, and startup fails without its name outside Development.
- [ ] The OIDC mock and test personas are not deployed to prod.

## Data and privacy

- [ ] `docs/privacy/data-inventory.md` is reviewed by BCC's privacy function and no longer marked as a draft.
- [ ] Retention periods and legal basis are agreed with the privacy function (standard 18, rule 4).
- [ ] No production data is used for tests or previews.

## Release

- [ ] Sandbox ran the same image digest that is promoted, and the smoke test passed.
- [ ] A migration plan exists: backward compatible with the previous release, or a stated exception.
- [ ] The release notes are generated from merged pull requests (the deploy workflow creates the release).
