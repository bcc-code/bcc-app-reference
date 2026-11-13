# 03 Infrastructure as Code

This standard covers how each repo describes its GCP resources in Terraform and how those changes reach an environment. All infrastructure is code, reviewed in PRs and applied by CI, so environments can be rebuilt and every permission has a visible reason.

Runtime settings for Cloud Run itself are in [04 GCP Runtime](04-gcp-runtime.md). Secret and identity principles are in [security principles](../principles/security.md); the tools are described in [cloud and tools](../technology/cloud-and-tools.md#terraform).

## Rules

### Layout and environments

1. **Terraform MUST live in `infra/`, one file per concern (`cloudrun_api.tf`, `iam.tf`, `secrets.tf`, `kms.tf`, `pubsub.tf`, `database.tf`, `artifact_registry.tf`, `gateway_backend.tf`, ...).** A reviewer finds the resource by file name, and diffs stay small. Per-PR infrastructure goes in its own root module (`infra/preview/`, see [07 Preview Environments](07-preview-environments.md)).
2. **Environment differences MUST be expressed as locals or maps keyed on the environment, never as copied files or directories per environment.** `api_min_instances = local.env == "prod" ? 1 : 0` or a map `{ sandbox = [...], prod = [...] }` shows every difference in one place.
3. **Environment data (project id, region, shared DB names, environment name) MUST come from the platform props (the Terraform variables `var.props` and `var.deployment_props`, which the platform's shared deploy workflow supplies), not hard-coded values.** The landing zone owns those values; copying them into the repo creates a second source of truth.

### How changes are applied

4. **Terraform MUST be applied only by CI through the shared reusable workflows in the platform team's `bcc-platform-deploy` repository (`terraform.yml` applies a repo's infrastructure, `landing-zone.yml` applies the shared landing zone).** Nobody applies from a laptop, so state, credentials and audit trail stay in one place.
5. **References to shared workflows and Terraform modules MUST be pinned to a commit SHA (with a version comment where a tag exists), not a tag, `@main` or `?ref=main`.** A change on the platform's `main` otherwise changes our deploys and infrastructure without a commit in our repo. Update the pin through a PR (Dependabot can track `uses:` references). Exception: the `bcc-platform-deploy` reusable workflows that authenticate to GCP (`terraform.yml`, `landing-zone.yml`) MUST be called at `@main`, with a comment saying why. The platform's workload identity trusts their `job_workflow_ref` at `refs/heads/main` only, so a SHA pin is refused (403 on `getOpenIdToken`). Changes there are reviewed in the platform repo instead. See [17 Supply Chain and Security Scanning](17-supply-chain-and-security-scanning.md).
6. **PRs that touch `infra/` MUST run `terraform fmt -check -recursive`, `terraform init -backend=false` and `terraform validate`.** These need no cloud credentials and catch most syntax and reference errors before merge. If `init` must download modules from private repositories, give it a read-only token for those repositories; `-backend=false` only removes the need for state access.
7. **PRs that touch `infra/` SHOULD also run `terraform plan` against sandbox and post it to the PR, only for PRs from the same repository (not forks) and with a read-only plan identity (no write roles). A scheduled job SHOULD run `plan` against each environment to detect drift.** Without a plan, the reviewer approves code without seeing what changes; without drift detection, console edits stay invisible. A plan runs the PR's code with cloud credentials, which is why the identity is read-only and forks are excluded.
8. **Terraform apply and application deploy MUST NOT race on the same push.** See [06 Deployment and Release](06-deployment-and-release.md).

### Identity and access

9. **CI MUST authenticate to GCP with Workload Identity Federation (`google-github-actions/auth` with `workload_identity_provider`); service account keys MUST NOT be created.** A key is a long-lived secret that can leak; federation issues short-lived credentials per run.
10. **Each runtime (API, processor, worker, job) and each caller (Pub/Sub push, Cloud Tasks, Cloud Scheduler, mocks) MUST have its own service account.** Separate identities let each receiver check who is calling (each receiver verifies the token's audience, its own URL, and the calling service account in the `email` claim; zero trust) and limit the blast radius of one compromised service.
11. **Grant the narrowest role that works: per-resource bindings (secret, KMS key, subscription) before project bindings, and a custom role with an explicit permission list when no predefined role is narrow enough.** For example, a scaler role holds only `run.services.get` and `run.services.update`.
12. **Every IAM grant that is not obvious from its name MUST have a comment stating why it is needed, and a grant with a known risk MUST link to the ADR or issue that accepts the risk.**
13. **`roles/iam.serviceAccountUser` MUST NOT be granted project-wide; grant it on the specific service account, to the specific caller (usually `sa-gha-cicd` to act as the runtime SA).** Project-wide it lets the holder act as every service account in the project.
14. **Database access MUST use Cloud SQL IAM authentication (IAM database users, Auth Proxy with `--auto-iam-authn`); no database passwords for runtime services.** Exception: throwaway preview databases, see [07 Preview Environments](07-preview-environments.md).

### Guard rails in Terraform

15. **Use `lifecycle.precondition` to mirror app-side safety checks in infrastructure.** Example: refuse a flag that is only meant for non-production (one that enables a destructive tool, for instance) in any environment outside an explicit list, with an error message that names the app-side check it mirrors. Both layers then have to be wrong before an unsafe setting reaches prod.
16. **KMS keys and stateful resources (databases, buckets with data) MUST have `lifecycle { prevent_destroy = true }`; `force_destroy` and `deletion_protection = false` need a comment saying why the resource is disposable.** A destroyed KMS key makes everything it encrypted unreadable.
17. **`lifecycle.ignore_changes` MUST only be used with a comment naming who owns the ignored field instead.** The usual case is the container image, owned by the deploy workflow. An unexplained `ignore_changes` hides real drift: ignoring the whole `scaling` block, for example, also hides minimum-instance changes. Ignore the specific field and say who owns it (for example `max_instance_count`, which operations may raise in the console during an incident).

### Secrets and registry hygiene

18. **Secret values SHOULD NOT pass through Terraform state.** Terraform creates the `google_secret_manager_secret` container, its replication and the accessor bindings; the value is added out of band (`gcloud secrets versions add`) or by a CI step that reads it from a GitHub secret. `random_password` and `secret_data = var.x` both put the value in state, readable by anyone with state access. Where the Terraform version and provider support write-only attributes (for example `secret_data_wo`), prefer them so the value never enters state. If a repo keeps a secret in state for now, restrict state access and say so in `infra/README.md`.
19. **Artifact Registry repositories MUST have cleanup policies, introduced with `cleanup_policy_dry_run = true` first.** Read the dry-run report and compare the listed digests with what live revisions and open previews use before flipping it; a wrong policy deletes the image a rollback needs.

### Comments

20. **Comments in `.tf` files MUST explain why a resource or setting is the way it is; long design discussions belong in `docs/` or an ADR, linked from a short comment.** A 40-line comment on one setting makes the file read as a design document. See [23 Documentation and Decisions](23-documentation-and-decisions.md).

## Examples

### Precondition mirroring an app guard; commented `ignore_changes` (rules 15, 17)

In `infra/cloudrun_api.tf`:

```hcl
lifecycle {
  # The deploy workflow owns the image; the Cloud Run client fields and labels are set by gcloud.
  ignore_changes = [template[0].containers[0].image, client, client_version, labels]

  precondition {
    condition     = !var.destructive_tools_enabled || contains(["dev", "sandbox"], local.env)
    error_message = "destructive_tools_enabled may only be true in dev or sandbox (keep in sync with the app's own environment check for these tools)."
  }
}
```

The `ignore_changes` list carries a comment naming the owner of each ignored field. Listing `scaling` as a whole would hide minimum-instance changes, so list a specific field if one must be ignored (see the later example).

### Custom role and a known risk linked to its ADR (rules 10, 11, 12, 13)

In `infra/iam.tf`:

```hcl
resource "google_project_iam_custom_role" "scaler" {
  role_id     = "appScaler${replace(local.env, "-", "_")}"
  description = "Read and update a Cloud Run service, to change its minimum instance count."
  permissions = ["run.services.get", "run.services.update"]
}

# Risk: acting as the runtime service account lets a compromised processor redeploy the api with another
# image or environment and take over the api identity. Accepted in docs/decisions/0012-scaler-acts-as-runtime.md;
# remove the binding once the sandbox check described there passes.
resource "google_service_account_iam_member" "processor_scaler_acts_as_runtime" {
  ...
}
```

The same file creates one service account per runtime and caller (`sa-myapp-api`, `-processor`, `-pubsub-push`, `-tasks`, `-scheduler`) and grants `iam.serviceAccountUser` only on each runtime SA to `sa-gha-cicd` (a `cicd_acts_as_runtime` binding).

### `prevent_destroy` on the KMS key (rule 16)

In `infra/kms.tf`: `rotation_period = "7776000s"` with `lifecycle { prevent_destroy = true }`.

### `ignore_changes` with the owner named (rule 17)

```hcl
lifecycle {
  # min_instance_count is deliberately NOT ignored: it is per-environment configuration that
  # Terraform owns (local.api_min_instances), so an apply is what moves an environment between
  # warm and scale-to-zero. max_instance_count stays ignored so an emergency ceiling bump made
  # in the console survives the next apply.
  ignore_changes = [
    template[0].containers[0].image,
    template[0].scaling[0].max_instance_count,
```

### Cleanup policy with dry run (rule 19)

`infra/artifact_registry.tf` sets `cleanup_policy_dry_run = true` and documents the check before flipping it: "read the dry-run report, and check the digests it lists against the ones live revisions and open previews actually reference."

### Format and validate on PRs (rule 6)

A PR workflow job `infra` runs:

```
terraform fmt -check -recursive infra
terraform -chdir=infra init -backend=false
terraform -chdir=infra validate
```

### Secret in state, documented (rule 18)

When `secret_data` puts the OIDC client secret in state, `infra/secrets.tf` and `infra/README.md` say so and give the alternative: `gcloud secrets versions add myapp-oidc-client-secret`, and drop the version resource (or use `secret_data_wo` where the Terraform version and provider support it). The target pattern for binary secrets such as certificate files is a secret container without versions, uploaded out of band.

### Environment map (rule 2)

In `infra/main.tf`:

```hcl
app_allowed_origins_by_environment = { sandbox = [...], prod = [...] }
```

## Related
- [04 GCP Runtime](04-gcp-runtime.md) — Cloud Run settings that these resources create
- [05 CI Pipeline](05-ci-pipeline.md) — infra checks on PRs
- [06 Deployment and Release](06-deployment-and-release.md) — apply and deploy ordering
- [07 Preview Environments](07-preview-environments.md) — per-PR root module and throwaway databases
- [17 Supply Chain and Security Scanning](17-supply-chain-and-security-scanning.md) — pinned workflows and modules
- [23 Documentation and Decisions](23-documentation-and-decisions.md) — where long design comments go
- [26 Gateway and Frontend Hosting](26-gateway-and-frontend-hosting.md) — gateway backend resources
- [Security principles](../principles/security.md) — secrets and zero trust
- [Cloud and tools](../technology/cloud-and-tools.md#terraform) — Terraform and GCP background
