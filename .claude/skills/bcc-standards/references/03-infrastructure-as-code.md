# 03 Infrastructure as Code

How each repo describes its GCP resources in Terraform and how those changes reach an environment. All infrastructure is code, reviewed in PRs and applied by CI, so environments can be rebuilt and every permission has a visible reason. Runtime settings for Cloud Run itself are in [04 GCP Runtime](04-gcp-runtime.md). See Terraform and Google Cloud Platform (GCP).

## Rules

### Layout and environments

1. **Terraform MUST live in `infra/`, one file per concern (`cloudrun_api.tf`, `iam.tf`, `secrets.tf`, `kms.tf`, `pubsub.tf`, `database.tf`, `artifact_registry.tf`, `gateway_backend.tf`, ...).** A reviewer finds the resource by file name, and diffs stay small. Per-PR infrastructure goes in its own root module (`infra/preview/`, see [07 Preview Environments](07-preview-environments.md)).
2. **Environment differences MUST be expressed as locals or maps keyed on the environment, never as copied files or directories per environment.** `api_min_instances = prod ? 1 : 0` or a map `{ sandbox = [...], prod = [...] }` shows every difference in one place.
3. **Environment data (project id, region, shared DB names, environment name) MUST come from the platform props (`var.props`, `var.deployment_props`), not hard-coded values.** The landing zone owns those values; copying them into the repo creates a second source of truth.

### How changes are applied

4. **Terraform MUST be applied only by CI through the shared `bcc-platform-deploy` reusable workflows (`terraform.yml`, `landing-zone.yml`).** Nobody applies from a laptop, so state, credentials and audit trail stay in one place.
5. **References to shared workflows and Terraform modules MUST be pinned to a release tag or commit SHA, not `@main` / `?ref=main`.** A change on the platform's `main` otherwise changes our deploys and infrastructure without a commit in our repo. Update the pin through a PR (Dependabot can track `uses:` references).
6. **PRs that touch `infra/` MUST run `terraform fmt -check -recursive`, `terraform init -backend=false` and `terraform validate`.** These need no credentials and catch most syntax and reference errors before merge.
7. **PRs that touch `infra/` SHOULD also run `terraform plan` against sandbox and post it to the PR, and a scheduled job SHOULD run `plan` against each environment to detect drift.** Without a plan, the reviewer approves code without seeing what changes; without drift detection, console edits stay invisible.
8. **Terraform apply and application deploy MUST NOT race on the same push.** See [06 Deployment and Release](06-deployment-and-release.md).

### Identity and access

9. **CI MUST authenticate to GCP with Workload Identity Federation (`google-github-actions/auth` with `workload_identity_provider`); service account keys MUST NOT be created.** See Secret Management.
10. **Each runtime (API, processor, worker, job) and each caller (Pub/Sub push, Cloud Tasks, Cloud Scheduler, mocks) MUST have its own service account.** Separate identities let each receiver check who is calling (per-caller OIDC audience, Zero Trust) and limit the blast radius of one compromised service.
11. **Grant the narrowest role that works: per-resource bindings (secret, KMS key, subscription) before project bindings, and a custom role with an explicit permission list when no predefined role is narrow enough.** vote-rewrite's `voteScaler` role holds only `run.services.get` and `run.services.update`.
12. **Every IAM grant that is not obvious from its name MUST have a comment stating why it is needed, and known risks MUST be behind a variable with the risk written down.**
13. **`roles/iam.serviceAccountUser` MUST NOT be granted project-wide; grant it on the specific service account, to the specific caller (usually `sa-gha-cicd` to act as the runtime SA).** Project-wide it lets the holder act as every service account in the project.
14. **Database access MUST use Cloud SQL IAM authentication (IAM database users, Auth Proxy with `--auto-iam-authn`); no database passwords for runtime services.** Exception: throwaway preview databases, see [07 Preview Environments](07-preview-environments.md).

### Guard rails in Terraform

15. **Use `lifecycle.precondition` to mirror app-side safety checks in infrastructure.** Example: refuse `load_test_enabled` outside non-production environments, with an error message that names the C# list it must stay in sync with. Both layers then have to be wrong before an unsafe setting reaches prod.
16. **KMS keys and stateful resources (databases, buckets with data) MUST have `lifecycle { prevent_destroy = true }`; `force_destroy` and `deletion_protection = false` need a comment saying why the resource is disposable.** A destroyed KMS key makes everything it encrypted unreadable.
17. **`lifecycle.ignore_changes` MUST only be used with a comment naming who owns the ignored field instead.** The usual case is the container image, owned by the deploy workflow. An unexplained `ignore_changes` hides real drift (vote-rewrite's `scaling` entry means a max-instance change never applies).

### Secrets and registry hygiene

18. **Secret values SHOULD NOT pass through Terraform state.** Terraform creates the `google_secret_manager_secret` container, its replication and the accessor bindings; the value is added out of band (`gcloud secrets versions add`) or by a CI step that reads it from a GitHub secret. `random_password` and `secret_data = var.x` both put the value in state, readable by anyone with state access. If a repo keeps a secret in state for now, restrict state access and say so in `infra/README.md`.
19. **Artifact Registry repositories MUST have cleanup policies, introduced with `cleanup_policy_dry_run = true` first.** Read the dry-run report and compare the listed digests with what live revisions and open previews use before flipping it; a wrong policy deletes the image a rollback needs.

### Comments

20. **Comments in `.tf` files MUST explain why a resource or setting is the way it is; long design discussions belong in `docs/` or an ADR, linked from a short comment.** See Comment the Why, Not the What and [23 Documentation and Decisions](23-documentation-and-decisions.md).

## Example

`infra/` in this repo. Per-concern `.tf` files, environment maps and IAM comments are the model. Source repo for IAM and guard rails: vote-rewrite `infra/`.
