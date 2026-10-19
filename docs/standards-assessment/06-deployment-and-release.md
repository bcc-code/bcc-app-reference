---
standard: 06 Deployment and Release
assessed: 2026-10-19
commit: e4b2b86
---
# 06 Deployment and Release — assessment

`deploy.yml` implements the intended flow: gate on a green push CI run for the exact commit, Terraform for sandbox, one image
build tagged with the SHA, additive asset upload, migration job with `--wait`, rollout, smoke test, an approved prod job that
copies the same digest, then a git tag and GitHub release. The design is sound, but the first deploy will likely fail because
the API is not given `Frontend__StaticPath` (see standard 04), and the rollback runbook's statement that the next deploy takes
traffic again does not hold after a pinned traffic rollback. There is no in-app "what's new".

**Score: Adequate** · ✅ 10 · ⚠️ 2 · ❌ 1 · N/A 2

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | `main` protected: PRs, required `ci`, one approval | MUST | ⚠️ | GitHub setting, not in the repo. Listed as a manual step in `docs/development.md:107-116`; `docs/runbooks/go-live.md:9` covers only the `prod` environment, not the branch ruleset. See "Not verifiable". |
| 2 | Deploy depends on green tests for the exact commit | MUST | ✅ | `workflow_run` on CI for `main`, gate requires `conclusion == 'success'` and `event == 'push'` (`deploy.yml:11-14,40-42`); manual dispatch checks the commit is on `main` and has a successful push CI run (`:60-66`). |
| 3 | Build once, tag with SHA, deploy same digest to sandbox and prod | MUST | ✅ | One `build` job tags `$SHA` and records the digest (`deploy.yml:129-145`); sandbox deploys `image@digest` (`:168`); prod copies that digest and checks it is unchanged (`:248-255`). One frontend build for all environments (`:111-112`). |
| 4 | Prod via GitHub Environment `prod`; takes sandbox digest | MUST | ✅ | `environment: prod` and `needs: [build, deploy-sandbox]`, digest from `needs.build.outputs.digest` (`deploy.yml:206-224`). Required reviewers are a GitHub setting (see "Not verifiable"). |
| 5 | Sandbox runs same image, migrations, Terraform as prod | MUST | ✅ | Same root module (`infra/`), same migration job and image digest. Prod Terraform is applied manually (`infra.yml:6`), so prod infra can lag sandbox; `deploy.yml:8-9` says to apply it before promoting. |
| 6 | Migrations complete before rollout; no cancel | MUST | ✅ | `jobs update` + `jobs execute --wait` before `services update` (`deploy.yml:185-190,258-263`); `concurrency` with `cancel-in-progress: false` (`:158-160,212-214`). |
| 7 | Hashed assets uploaded before rollout | MUST | ✅ | Upload step precedes migrate and rollout (`deploy.yml:181-182,256-257`). |
| 8 | Shared-image deploy order stated | MUST | N/A | One service per image. |
| 9 | Smoke test: `/health` and `/` → 302 | MUST | ✅ | `deploy.yml:191-196,264-268` against `vars.PUBLIC_BASE_URL`. The 302 check carries a "TODO: confirm for this app". |
| 10 | Terraform apply and deploy do not race | MUST | ✅ | Sandbox apply is a job before `build` (`deploy.yml:71-83`); prod apply and deploy share group `env-prod` (`infra.yml:107-109`, `deploy.yml:212-214`). |
| 11 | Rollback runbook with required content | MUST | ⚠️ | `docs/runbooks/rollback.md` covers traffic shift (`:6-11`), why old assets remain (`:3-4`), overwritten files (`:19-23`) and rolling forward (`:25-28`). Step 4 (`:16-17`) says "the next deploy sends traffic to the new revision", but after `update-traffic --to-revisions <rev>=100` traffic stays pinned, and `deploy.yml:190,263` uses `gcloud run services update` without `--to-latest`. The next deploy would create a revision with no traffic, and the smoke test would pass against the old revision. |
| 12 | Migrations backward compatible (expand/contract) | SHOULD | ✅ | Stated in `docs/development.md:87-89` and `rollback.md:14-15`, with the roll-forward exception. |
| 13 | SHA image tags; git tag and GitHub release per prod deploy | MUST | ✅ | `ContainerImageTags="$SHA"` (`deploy.yml:140`); `prod-<date>.<n>` tag and `gh release create --generate-notes` with the SBOM (`:270-281`). |
| 14 | In-app "what's new" from versioned content | SHOULD | ❌ | No what's-new content or page in `frontend/src` or `docs/`. |
| 15 | Temporary deployment code has a greppable marker | MUST | N/A | No temporary or migration-only deployment code. |

## Gaps to close

- **(04.13, blocks every deploy)**: Set `Frontend__StaticPath` for the Cloud Run service and job (`infra/cloudrun_api.tf`
  `local.api_env`) or default it in `FrontendConfiguration.cs`; then remove the TODO at `deploy.yml:114-115`. Details in the
  04 assessment.
- **11**: Add `--to-latest` to the rollout (`gcloud run services update-traffic template-api-<env> --to-latest` after the
  update in `deploy.yml:190,263`), or change `docs/runbooks/rollback.md` step 4 to say that traffic must be returned to the
  latest revision with `--to-latest`. Prefer the workflow change so the smoke test always tests the new revision.
- **9**: Resolve the "TODO: confirm for this app" in `deploy.yml:195` when the app's unauthenticated `/` behaviour is fixed.
- **14**: Add versioned what's-new content (for example `frontend/src/content/whats-new/<date>-<slug>.md` per language) and a
  page or dialog that shows entries newer than the user's last visit.

## Not verifiable in the repo

- Rule 1: branch ruleset on `main` (pull requests only, required check `ci`, one approving review). GitHub Settings, Rules.
- Rule 4: required reviewers on the `prod` environment, and deployment branches of `sandbox`/`prod` limited to `main`
  (Settings, Environments). The Workload Identity binding trusts the environment name (`infra/iam.tf:101-111`), so this
  setting is what stops a branch from deploying.
- Rules 2–13 end to end: no deploy has run from this template. Run one deploy to sandbox and check the smoke test and the
  release.
