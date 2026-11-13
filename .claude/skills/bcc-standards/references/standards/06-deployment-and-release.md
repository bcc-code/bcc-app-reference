# 06 Deployment and Release

How a merged change reaches sandbox and production, and how we roll back and how we version and document each release. The aim is that production only ever runs an artifact that passed tests and ran in sandbox first, that schema changes cannot half-apply, and that a bad release can be undone from a runbook.

The main idea is to build once, promote the same image digest through the environments, run schema changes before the rollout, and verify each deploy with a smoke test.

## Rules

### Gate

1. **`main` MUST be protected: changes only through PRs, with the aggregate CI check from [05 CI Pipeline](05-ci-pipeline.md) required and at least one approving review.** Without protection, CI is advisory and anyone with write access can push untested code that deploys automatically.
2. **Deploys MUST depend on green tests for the exact commit being deployed.** Either the deploy workflow runs (or calls) the test workflow and `needs:` it, or it is triggered by `workflow_run` on a successful CI run. Running tests in a separate workflow alongside the deploy blocks nothing.

### Build once, promote

3. **Build the image once per commit, tag it with the commit SHA, push it, and deploy the same image digest to sandbox and then to prod.** Rebuilding for prod means prod runs an artifact that was never tested; dependency resolution, base image or build flags can differ. Environment-specific frontend builds MUST NOT be used; serve runtime configuration from the API instead.
4. **Production deploys MUST go through a GitHub Environment `prod` with required reviewers, and the prod job MUST take the digest that was deployed to sandbox as input.** The reviewer then approves a known artifact, not "whatever `main` is now".
5. **Where no staging environment exists, sandbox MUST run the same image, migrations and Terraform as prod, and the decision on whether a staging environment is needed MUST be recorded in an ADR.** The environment model is sandbox, staging, prod; where only sandbox and prod exist (plus dev and previews, see [07 Preview Environments](07-preview-environments.md)), sandbox is the last stop before prod. See [23 Documentation and Decisions](23-documentation-and-decisions.md) for ADRs.

### Order of operations

6. **Schema changes and migrations MUST run and complete before the new revision rolls out, in a workflow with `concurrency: cancel-in-progress: false`.** A cancelled migration can leave the schema half-applied; overlapping runs can migrate with each other's image. See [04 GCP Runtime](04-gcp-runtime.md) and [12 Data Access and Migrations](12-data-access-and-migrations.md).
7. **Hashed frontend assets MUST be uploaded before the rollout** (additively, see [04 GCP Runtime](04-gcp-runtime.md)), so the new `index.html` never references a missing bundle.
8. **When several services share an image, the deploy order MUST be stated and follow the dependency direction: consumers of new messages first, then producers.** For example, deploy a background processor before the API, so the processor understands what the new API sends.
9. **Each deploy MUST end with a smoke test against the public URL**: `/health` answers, and one app-specific check passes (for example, an unauthenticated request for a protected route returns the login redirect, or the public landing page returns 200). A deploy that rolls out a broken revision then fails visibly.
10. **Terraform apply and the app deploy MUST NOT race on the same push.** Either one workflow applies Terraform and then deploys, or the deploy waits for the Terraform workflow (`workflow_run`, or a shared `concurrency` group per environment). A push that touches both `infra/` and app code otherwise runs the two in parallel.

### Rollback

11. **Each repo MUST have a rollback runbook in `docs/runbooks/rollback.md`.** It covers: shifting Cloud Run traffic to the previous revision (in the deploy order), why old assets still exist, what to do with files that are overwritten on deploy, and when to roll forward instead (a migration that removed or renamed something).
12. **Migrations SHOULD be backward compatible with the previous release (expand, then contract in a later release), so that a traffic rollback is safe.** The runbook states the exception.

### Versioning and release notes

13. **Images MUST be tagged with the commit SHA; each production deploy MUST create a git tag (e.g. `prod-2026-10-15.1` or a semantic version) and a GitHub release with notes generated from merged PRs.** Without them, "what is in prod" can only be answered by reading workflow logs.
14. *(Retired.)* In-app "what's new" content is a product feature, not an engineering standard.

### Temporary deployment code

15. **Code that exists only for a migration between platforms or deployment paths MUST carry a greppable marker comment (e.g. `LEGACY-HOST-ONLY`) stating when to delete it.** All of it can then be found and removed with one search.

## Examples

### Migrations before rollout, never cancelled (rules 6, 10)

```yaml
# Serialised and never cancelled: overlapping runs can migrate with each other's image, and a
# cancel can abort a migration halfway.
concurrency:
  group: api-deploy-${{ github.ref }}
  cancel-in-progress: false
...
# EF migrations, then the messaging schema. --wait fails the job on a failed execution, so
# `deploy` never rolls a revision against a missing schema.
schema:
  needs: [push]
```

### Deploy order and smoke test (rules 8, 9)

```yaml
- name: Deploy processor
  run: gcloud run services update "myapp-processor-$ENV" --region "$REGION" --container app --image "$IMAGE"
- name: Deploy api
  run: gcloud run services update "myapp-api-$ENV" --region "$REGION" --container app --image "$IMAGE"
- name: Smoke test
  run: |
    curl -fsS "$APP_BASE_URL/health"
    # App-specific check: here, an unauthenticated request for a protected route redirects to login.
    test "$(curl -s -o /dev/null -w '%{http_code}' -H 'Accept: text/html' "$APP_BASE_URL/app")" = 302
```

The same job applies the schema (through the Cloud SQL Auth Proxy, then `REASSIGN OWNED`, a PostgreSQL command that transfers ownership of the objects the migration user created to the application's database role) before either service is updated.

### Rollback runbook (rules 11, 12)

A runbook starts like this:

> 1. In Cloud Run, open `myapp-processor-<env>`, then `myapp-api-<env>`. Under Revisions, send 100% of the traffic to the previous revision (Manage traffic). Do the processor first, as the deploy does.
> 2. Check the environment: `/health` answers and a signed-in user can open the start page.
> 3. The database schema is only extended (new tables and columns are added), so the previous revision runs against the newer schema. If a release removed or renamed something, roll forward with a fix instead.

It also covers any file that is overwritten on deploy and has no bucket versioning, such as a widget loader.

### Marker comments (rule 15)

```csharp
// LEGACY-HOST-ONLY: delete with DatabaseMigrator once production has moved off the old host.
```

```yaml
# LEGACY-HOST-ONLY. The API migrates its own schema at startup there, so no schema step is needed.
```

### Build once, promote (rules 3, 4, 13)

1. `build` job on push to `main`: test, publish image tagged `${{ github.sha }}`, push to Artifact Registry, output the digest.
2. `deploy-sandbox` (`environment: sandbox`): Terraform (or wait for it), schema job, assets, rollout by digest, smoke test.
3. `deploy-prod` (`environment: prod`, required reviewers, `needs: deploy-sandbox`): copy the same digest to the prod registry (`gcloud artifacts docker tags add` or `crane copy`), schema job, assets, rollout, smoke test, create release tag.

## Related

- [03 Infrastructure as Code](03-infrastructure-as-code.md) — Terraform that the deploy must not race with
- [04 GCP Runtime](04-gcp-runtime.md) — Cloud Run revisions and asset upload
- [05 CI Pipeline](05-ci-pipeline.md) — the aggregate check that gates `main`
- [07 Preview Environments](07-preview-environments.md) — per-PR deployments
- [12 Data Access and Migrations](12-data-access-and-migrations.md) — expand and contract migrations
- [22 Observability and Operations](22-observability-and-operations.md) — health checks and runbooks
- [23 Documentation and Decisions](23-documentation-and-decisions.md) — ADRs and runbooks
- [Security principles](../principles/security.md) — shift-left practices
