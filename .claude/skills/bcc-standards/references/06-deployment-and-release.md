# 06 Deployment and Release

How a merged change reaches sandbox and production, and how we roll back, version and announce it. The aim is that production only ever runs an artifact that passed tests and ran in sandbox first, that schema changes cannot half-apply, and that a bad release can be undone from a runbook. See Environments for the principle.

## Rules

### Gate

1. **`main` MUST be protected: changes only through PRs, with the aggregate CI check from [05 CI Pipeline](05-ci-pipeline.md) required and at least one approving review.** Without protection, CI is advisory and anyone with write access can push untested code that deploys automatically.
2. **Deploys MUST depend on green tests for the exact commit being deployed.** Either the deploy workflow runs (or calls) the test workflow and `needs:` it, or it is triggered by `workflow_run` on a successful CI run.

### Build once, promote

3. **Build the image once per commit, tag it with the commit SHA, push it, and deploy the same image digest to sandbox and then to prod.** Rebuilding for prod means prod runs an artifact that was never tested; dependency resolution, base image or build flags can differ. Environment-specific frontend builds (bcc-privacy builds per env) must be replaced by runtime configuration served by the API.
4. **Production deploys MUST go through a GitHub Environment `prod` with required reviewers, and the prod job MUST take the digest that was deployed to sandbox as input.** The reviewer then approves a known artifact, not "whatever `main` is now".
5. **A staging environment that mirrors production is not in place yet.** Environments describes Sandbox → Staging → Prod; we run sandbox and prod only (plus dev and previews in orginn). Until staging exists, sandbox is the last stop before prod and must run the same image, migrations and Terraform as prod.

### Order of operations

6. **Schema changes and migrations MUST run and complete before the new revision rolls out, in a workflow with `concurrency: cancel-in-progress: false`.** A cancelled migration can leave the schema half-applied; overlapping runs can migrate with each other's image. See [04 GCP Runtime](04-gcp-runtime.md) and [12 Data Access and Migrations](12-data-access-and-migrations.md).
7. **Hashed frontend assets MUST be uploaded before the rollout** (additively, see [04 GCP Runtime](04-gcp-runtime.md)), so the new `index.html` never references a missing bundle.
8. **When several services share an image, the deploy order MUST be stated and follow the dependency direction: consumers of new messages first, then producers.** vote-rewrite deploys `vote-processor` before `vote-api`, so the processor understands what the new api sends.
9. **Each deploy MUST end with a smoke test against the public URL**: `/health` answers, and an unauthenticated request for `/` returns the login redirect (302). A deploy that rolls out a broken revision then fails visibly.
10. **Terraform apply and the app deploy MUST NOT race on the same push.** Either one workflow applies Terraform and then deploys, or the deploy waits for the Terraform workflow (`workflow_run`, or a shared `concurrency` group per environment).

### Rollback

11. **Each repo MUST have a rollback runbook in `docs/runbooks/rollback.md`.** It covers: shifting Cloud Run traffic to the previous revision (in the deploy order), why old assets still exist, what to do with files that are overwritten on deploy, and when to roll forward instead (a migration that removed or renamed something).
12. **Migrations SHOULD be backward compatible with the previous release (expand, then contract in a later release), so that a traffic rollback is safe.** The runbook states the exception.

### Versioning and release notes

13. **Images MUST be tagged with the commit SHA; each production deploy MUST create a git tag (e.g. `prod-2026-10-15.1` or a semantic version) and a GitHub release with notes generated from merged PRs.**
14. **User-facing changes SHOULD be announced in-app from versioned content files ("what's new").** orginn keeps them as markdown per language in `Frontend/src/content/whats-new/` (`2026-09-14-fakturamodul.md`, `.en.md`, `.de.md`).

### Temporary deployment code

15. **Code that exists only for a migration between platforms or deployment paths MUST carry a greppable marker comment (e.g. `AZURE-ONLY`) stating when to delete it.** orginn marks its Azure App Service path this way, so all of it can be found and removed with one search.

## Example

`.github/workflows/` (deploy workflows, when present) and `docs/runbooks/rollback.md`. Source repos: orginn and vote-rewrite deploy workflows for the migration step and smoke test.
