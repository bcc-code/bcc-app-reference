---
standard: 07 Preview Environments
assessed: 2026-10-19
commit: e4b2b86
updated: 2026-10-20 (ServerlessWithOutbox, ADR 0007)
---
# 07 Preview Environments — assessment

The workflow side of previews is complete and follows orginn's design: label trigger, a `decide` job, fork and
`pull_request_target` guards, teardown from `main`, validated manual destroy, per-PR concurrency, a sticky comment and one
shared deployment environment. The image now carries Wolverine's pre-generated code and the container gets
`Frontend__StaticPath`, and the preview deploy account lost `roles/iam.serviceAccountAdmin`: previews run as one shared
runtime account that the main Terraform creates. The infrastructure side is still a placeholder: `infra/preview/` creates
only a public Cloud Run service with no database, migration job, OIDC mock, connection string or OIDC settings. Because the
API requires OIDC and KMS settings outside Development, a preview most likely fails to start, although `preview.yml` and
`docs/pr-previews.md` say the API service works. Since ADR 0007 the API also requires `Messaging__PubsubProjectId` and the
four `Internal__*` settings outside Development and Test; `infra/preview/main.tf:8-9` lists the Pub/Sub topic, push
subscription and durability pass job as one more TODO.

**Score: Weak** · ✅ 13 · ⚠️ 7 · ❌ 1 · N/A 0

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | Opt-in via `preview` label | MUST | ✅ | `preview.yml:86-104`. |
| 2 | `decide` job with deploy/destroy/first_time/component/tag outputs | MUST | ✅ | `preview.yml:43-140`; every other job keys off its outputs; decision written to the step summary (`:140`). |
| 3 | Only the `preview` label triggers deploy/destroy | MUST | ✅ | `LABEL == "preview"` checks (`preview.yml:88,94`). |
| 4 | Forks never deploy or destroy | MUST | ✅ | `SAME_REPO` guard (`preview.yml:66,84`). |
| 5 | Own root module, workspace `pr<N>`, shared workflow, named/labelled per PR | MUST | ⚠️ | `infra/preview/` root module, `terraform workspace select -or-create pr<N>` (`preview.yml:225-229`), `template-api-pr<N>` with `app`/`pr` labels (`infra/preview/main.tf:13-17,32-34,43`). Applied by `preview.yml` itself, not by the shared `bcc-platform-deploy` workflow (same deviation as 03.4), into the sandbox project rather than a dev project. |
| 6 | Own DB on shared preview Cloud SQL instance, looked up by name | MUST | ⚠️ | TODO placeholder: not implemented (`infra/preview/main.tf:3-9`). No shared preview instance in `infra/database.tf`. |
| 7 | Migration after every apply; reset+seed only on first deploy/reopen | MUST | ⚠️ | `migrate` job runs after `provision` (`preview.yml:234-261`) but is skipped until `PREVIEW_HAS_DATABASE` is set, and the job it executes does not exist in `infra/preview/`. Seeding is not wired: the comment at `preview.yml:257-259` says "The seeder and its allowlist ... do not exist in the template yet", which is false (`Seeding/SeedCommand.cs`, `Seeding/SeedingGate.cs:10` with `Preview`). |
| 8 | Own OIDC mock and Mailpit; no real IdP, mail, payments | MUST | ⚠️ | TODO placeholder: only the unused variable `oidc_mock_image` (`infra/preview/variables.tf:24-28`). The service gets no `OIDC__*` settings at all. No e-mail in the sample, so Mailpit is not needed. |
| 9 | Scale to zero; comment says so | MUST | ✅ | `min_instance_count = 0`, `cpu_idle = true` (`infra/preview/main.tf:45,60`); sticky comment text (`preview.yml:317-318`). |
| 10 | Deterministic URL | MUST | ✅ | Built from service name, project number and region (`preview.yml:289-293`), matching `infra/preview/main.tf:32`. |
| 11 | One sticky comment by marker, with URLs, commit, login, reset; `:active` marker | MUST | ⚠️ | Marker `<!-- template-preview -->` and `<!-- template-preview:active -->`, updated in place (`preview.yml:294-329`), with URL and commit. No "how to reset data" line, and the login text ("Log in through the preview's own mock identity provider", `:314`) describes a mock that is not deployed. |
| 12 | Deployments in one shared `preview` environment, `task: preview-pr<N>` | MUST | ✅ | `preview.yml:333-361`; deleted on teardown (`:434-438`). |
| 13 | Unlabel/close destroys via `pull_request_target` | MUST | ✅ | `pull_request_target: [unlabeled, closed]` (`preview.yml:16-17`), `teardown` job (`:363-401`). |
| 14 | `pull_request_target` never runs PR code; decide refuses deploy | MUST | ✅ | Teardown checks out the default branch (`preview.yml:378-380`); `decide` exits on deploy from `pull_request_target` (`:126-129`). |
| 15 | Manual destroy with numeric PR input | MUST | ✅ | `workflow_dispatch` input validated with `^[0-9]+$` (`preview.yml:76-81`); also a Terraform validation (`infra/preview/variables.tf:13-16`). |
| 16 | Per-PR concurrency, no cancel | MUST | ✅ | `preview.yml:30-32`. |
| 17 | `docs/pr-previews.md` with the eight sections | MUST | ⚠️ | All sections exist (getting, using, does not do, isolation, destroying, security, troubleshooting, housekeeping). The status line "The workflow and the API service work" (`docs/pr-previews.md:7`) is not supported by the code (see "Not verifiable"). The security section (`:54-61`) states the current grants. |
| 18 | PR images cleaned up by registry policy | MUST | ✅ | `delete-old-versions` (any tag, 30 days) covers `pr<N>-<sha>` (`infra/artifact_registry.tf:32-40`). Still in dry run (`:11`), so nothing is deleted until that flag is flipped. |
| 19 | Previews not publicly reachable | SHOULD | ❌ | `INGRESS_TRAFFIC_ALL` and `allUsers` invoker (`infra/preview/main.tf:37-39,84-89`), marked TODO. |
| 20 | Generated test data only; shared `cloudsqlsuperuser` stated | MUST | ✅ | `docs/pr-previews.md:36-41`; sticky comment repeats it (`preview.yml:314-315`). |
| 21 | Preview WIF binding scoped by event/environment | SHOULD | ⚠️ | Bound to environment `preview` (`infra/iam.tf:233-238`), which has no branch restriction by design, so any PR branch can use the account. What the account can do is now limited: no IAM administration, `serviceAccountUser` on the shared preview runtime account only (`iam.tf:261-266`), so it cannot act as the Terraform or sandbox API accounts. It keeps project-wide `roles/run.admin` (`iam.tf:240-248`), so PR code can still create, change and delete any Cloud Run service or job in sandbox and change its invoker policy (see 03.11). |

## Gaps to close

- **6, 7, 8**: Complete `infra/preview/main.tf`: a per-PR database and user on a shared preview Cloud SQL instance created by
  the main `infra/` (looked up with a `data` source by name), a `template-migrate-pr<N>` job, an OIDC mock service, the Auth
  Proxy sidecar, and the env vars the API needs outside Development (`OIDC__Authority`, `OIDC__ClientId`,
  `Gcp__DataProtectionKmsKeyName` or `ASPNETCORE_ENVIRONMENT=Preview` with a preview-safe key setting,
  `ConnectionStrings__template`, and since ADR 0007 `Messaging__PubsubProjectId` and the `Internal__*` settings with a per-PR topic, push subscription and durability pass job). `Frontend__StaticPath` is already set (`infra/preview/main.tf:51-55`). Then wire seeding
  through `SeedCommand` on first deploy and fix the stale comment at `preview.yml:257-259`.
- **17, 11**: Until the above is done, change `docs/pr-previews.md:7` and the header of `preview.yml:3-4` to say the API
  service does not start yet; add a "reset the data" line and a correct login line to the sticky comment.
- **21** (and 03.11): Narrow the preview account's project-wide `roles/run.admin` to the per-PR service and job names; see
  the 03 assessment.
- **19**: Put previews behind the platform gateway or IAP, or at minimum `INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER` with a
  gateway route per PR.
- **5**: Covered by the ADR proposed in 03 (own Terraform workflow instead of `bcc-platform-deploy`).

## Not verifiable in the repo

- Whether a preview starts at all. Reading the code: the image runs as `Production` (no `ASPNETCORE_ENVIRONMENT` in
  `infra/preview/main.tf`), and `AuthConfiguration.cs:82` and `DataProtectionConfiguration.cs:36` throw outside
  Development/Test when OIDC or KMS settings are missing. `Frontend__StaticPath` and the pre-generated Wolverine code no
  longer block startup. Expect `terraform apply` to fail on an unready revision. Confirm with one labelled PR in sandbox.
- That the preview deployer can deploy as `sa-template-preview-run` with only the account-level `serviceAccountUser`
  binding, and that `infra/` has been applied in sandbox so the account exists (see 03.13).
- Rule 21: that the GitHub environment `preview` exists with the variables in `docs/pr-previews.md:76-80`.
