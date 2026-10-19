---
standard: 03 Infrastructure as Code
assessed: 2026-10-19
commit: e4b2b86
updated: 2026-10-19 (gaps 1–4 of the index fixed)
---
# 03 Infrastructure as Code — assessment

`infra/` is split per concern, environment differences are in one map, IAM is per runtime and caller with comments, stateful
resources are protected, secret values stay out of state and the registry has dry-run cleanup policies. The template applies
Terraform with its own `infra.yml` and plain variables instead of the platform's shared workflow and `props`, a deviation
written down in `infra/README.md` but not in an ADR. The preview deploy account no longer holds
`roles/iam.serviceAccountAdmin` and may act as one shared preview runtime account only. It still has project-wide
`roles/run.admin` in sandbox, so PR code can create and delete any Cloud Run service or job there and change its invoker
policy, though it cannot act as other service accounts.

**Score: Adequate** · ✅ 15 · ⚠️ 5 · ❌ 0 · N/A 0

No rule fails, but four MUST rules are only partly met: two are an unrecorded deviation from the platform workflow, and the
preview account's `run.admin` is still project-wide.

Commands run: `terraform fmt -check -recursive infra` (exit 0), `terraform init -backend=false` and `terraform validate` for
`infra/` and `infra/preview/` (both "Success! The configuration is valid."), on a copy in the scratchpad. `actionlint` on
`.github/workflows` (no findings). Re-run after the fixes: `terraform fmt` and `terraform validate` pass for `infra/` and
`infra/preview/`; `actionlint` clean.

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | Terraform in `infra/`, one file per concern; previews own root | MUST | ✅ | `infra/` has `cloudrun_api.tf`, `cloudrun_migrate.tf`, `iam.tf`, `secrets.tf`, `kms.tf`, `database.tf`, `artifact_registry.tf`, `storage.tf`, `gateway_backend.tf`, `monitoring.tf`; `infra/preview/` is its own root module with its own backend (`infra/preview/terraform.tf`). |
| 2 | Env differences as locals/maps, not copies | MUST | ✅ | `local.env_config` keyed on `sandbox`/`prod` (`infra/main.tf:8-30`); one root module for both. |
| 3 | Env data from platform props, not hard-coded | MUST | ⚠️ | Not hard-coded, but from plain variables (`variables.tf:1-18`) filled from `TF_VARS` (`infra.yml:59,114`) instead of `var.props`. Deviation explained with migration steps in `infra/README.md:23-34`; no ADR. |
| 4 | Apply only by CI through shared `bcc-platform-deploy` workflows | MUST | ⚠️ | Applied only by CI (`infra.yml:101-137`, called from `deploy.yml:71-80`) except the documented one-time bootstrap (`infra/README.md:36-50`). Uses its own `infra.yml`, not the shared `terraform.yml`; same README deviation, no ADR. |
| 5 | Shared workflows and modules pinned to tag/SHA | MUST | ✅ | Gateway modules pinned to a commit SHA with a reason (`gateway_backend.tf:16-20,31`); reusable workflow `bcc-security-scan` pinned by SHA (`supply-chain-security.yml:31`); every action SHA-pinned (`grep` for `uses:` without a 40-hex SHA finds only local actions). |
| 6 | PRs touching `infra/` run fmt, init -backend=false, validate | MUST | ✅ | `pr.yml:196-208` (`infra` job, both root modules); filter `^infra/` at `pr.yml:52`. |
| 7 | Plan against sandbox posted to PR; scheduled drift plan | SHOULD | ⚠️ | `infra.yml:46-99` runs a read-only plan on PRs but writes it to the job summary, not a PR comment, and skips when `TF_STATE_BUCKET` is unset. No scheduled drift job (no `schedule:` in `infra.yml`). |
| 8 | Terraform apply and app deploy do not race | MUST | ✅ | Sandbox: `deploy.yml` applies (`infra-sandbox`) before `build` and `deploy-sandbox`. Prod: manual `infra.yml` apply and `deploy-prod` share concurrency group `env-prod` with `cancel-in-progress: false` (`infra.yml:107-109`, `deploy.yml:211-213`). |
| 9 | CI uses Workload Identity Federation; no SA keys | MUST | ✅ | Pool and provider with repository condition (`iam.tf:51-73`); every workflow uses `google-github-actions/auth` with `workload_identity_provider`. No `google_service_account_key` or `credentials_json` in `infra/` or `.github/`. |
| 10 | Own service account per runtime and caller | MUST | ✅ | `api`, `migrate` (`iam.tf:6-14`), `deploy`, `terraform`, `plan` (`:82-99`), `preview` deployer (`:227-231`) and a separate `preview_runtime` account that previews run as (`:254-258`), looked up by `infra/preview/main.tf:23-27`. Previews share the runtime account; the comment at `iam.tf:250-253` states that they must not hold per-PR secrets. |
| 11 | Narrowest role; per-resource before project bindings | MUST | ⚠️ | Mostly per-resource: secrets (`secrets.tf:12-23`), KMS (`kms.tf:18-22`), registry, bucket, Cloud Run service and job (`iam.tf:134-161`). The preview account no longer has `roles/iam.serviceAccountAdmin` or a project-wide `serviceAccountUser`; it has `roles/run.admin` and `roles/serviceusage.serviceUsageConsumer` project-wide (`iam.tf:240-248`) and `serviceAccountUser` on the preview runtime account only (`:261-266`). `run.admin` is still project-wide: PR code can create, change and delete every Cloud Run service and job in sandbox, including `template-api-sandbox`, and set their invoker policy. It cannot act as any account other than the preview runtime account, so it cannot reach the Terraform accounts. |
| 12 | Non-obvious grants commented; known risks behind a variable | MUST | ⚠️ | Grants are commented (`iam.tf:17-30,119-131,141,156,163-165,185,193,206-207,250-253,260`). The preview risk comment (`iam.tf:222-226`) now matches the grants, but the resources are gated on `local.env == "sandbox"`, not on a variable such as `previews_enabled`. |
| 13 | `serviceAccountUser` never project-wide | MUST | ✅ | Deploy and Terraform accounts get it on the two runtime accounts only (`iam.tf:121-126,186-191`). The preview account gets it on the shared preview runtime account only (`google_service_account_iam_member.preview_acts_as_runtime`, `iam.tf:261-266`); the conditional project-wide binding is removed. |
| 14 | Cloud SQL IAM auth, Auth Proxy `--auto-iam-authn`, no DB passwords | MUST | ✅ | `cloudsql.iam_authentication` flag (`database.tf:14-17`), IAM users (`:50-60`), proxy args (`cloudrun_api.tf:101`, `cloudrun_migrate.tf:51`); connection strings have no password (`cloudrun_api.tf:5`, `cloudrun_migrate.tf:2`). |
| 15 | `lifecycle.precondition` mirrors app-side safety checks | MUST | ✅ | `precondition` refusing prod without alert recipients (`cloudrun_api.tf:123-126`); `environment` validation (`variables.tf:14-17`). No infra setting in the sample mirrors an app-side gate (seeding is not configured in GCP). |
| 16 | KMS and stateful resources `prevent_destroy`; disposables commented | MUST | ✅ | KMS key (`kms.tf:12-15`), Cloud SQL (`database.tf:38-41`), assets bucket (`storage.tf:13-16`). `deletion_protection = false` for sandbox has a reason (`main.tf:16`), preview service too (`infra/preview/main.tf:33`). |
| 17 | `ignore_changes` only with owner comment | MUST | ✅ | Image owned by `deploy.yml` (`cloudrun_api.tf:118-121`, `cloudrun_migrate.tf:67-69`). `client`/`client_version` are set by gcloud; not named, but a known provider pattern. |
| 18 | Secret values not in state | SHOULD | ✅ | `secrets.tf:1-10` creates the container only; value added with `gcloud secrets versions add`. No `random_password` or `secret_data`. |
| 19 | Registry cleanup policies, dry run first | MUST | ✅ | `artifact_registry.tf:8-40` (`cleanup_policy_dry_run = true`, keep 20 newest, delete untagged after 7 days, any after 30 days). |
| 20 | `.tf` comments explain why | MUST | ✅ | For example `cloudrun_api.tf:13-15,30-31,72-73,83-85`, `database.tf:24-25,62-64`, `storage.tf:1-3`. |

## Gaps to close

- **11**: Narrow the preview account's project-wide `roles/run.admin` (`infra/iam.tf:240-248`), for example with an IAM
  condition on the `template-api-pr` / `template-migrate-pr` name prefix, or a custom role, so PR code cannot change the
  sandbox API service or its invoker policy.
- **3, 4**: Record the "no landing zone, own `infra.yml`" deviation as an ADR in `docs/adrs/` and link it from
  `infra/README.md`, so it is reviewed like ADR 0002/0006.
- **12**: Put the preview resources in `infra/iam.tf` behind a variable (for example `previews_enabled`, default `false`)
  with the risk in its description.
- **7**: Post the plan as a sticky PR comment in `infra.yml` (needs `pull-requests: write` on that job only) and add a
  `schedule:` trigger that plans sandbox and prod and fails on changes.
- Docs: `infra/README.md:80` says `infra.yml` applies sandbox "on pushes to `main`"; it has no push trigger, `deploy.yml`
  calls it after CI. Correct the sentence.

## Not verifiable in the repo

- Rule 13: that the preview deployer can deploy a service as `sa-template-preview-run` with only the account-level
  `serviceAccountUser` binding. The account is created by the main Terraform in sandbox, so `infra/` must be applied before
  the first preview. Test with one preview in sandbox.
- Rule 4: that nobody holds Terraform-capable roles in sandbox/prod outside the CI accounts (check project IAM in the console).
- Rule 9: that the state buckets restrict access to the Terraform/plan accounts and infra admins (`infra/README.md:40`).
