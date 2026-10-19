# 05 CI Pipeline

What runs on every pull request, and how the workflows themselves are written. The PR pipeline is the gate that keeps `main` deployable: it must cover backend, frontend, contracts, infrastructure and secrets, fail on problems instead of hiding them, and be fast enough that people wait for it. Deploy workflows are in [06 Deployment and Release](06-deployment-and-release.md), scanning tools in [17 Supply Chain and Security Scanning](17-supply-chain-and-security-scanning.md).

## Rules

### Structure

1. **Each repo MUST have one PR workflow whose first job, `changes`, computes path filters; every other job declares `needs: changes` and an `if:` on its output.** Jobs whose inputs did not change are skipped, so a frontend-only PR does not wait for integration tests. vote-rewrite computes the filters with plain `git diff` (no third-party action); orginn does the same and calls reusable workflows (`test.yml`, `frontend.yml`, `openapi.yml`, `wolverine-codegen.yml`) so the deploy workflow can reuse them.
2. **Path filtering MUST be done inside the workflow (the `changes` job), not with `on.pull_request.paths`, for any workflow that provides a required check.** When the trigger filter skips the workflow, its required check never reports and the PR stays pending. A job skipped by `if:` reports as skipped, which satisfies a required check. Simplest setup: one final `ci` job with `needs: [all jobs]` and `if: always()` that fails when any needed job failed or was cancelled, and make only that job required. orginn records the caveat on its trigger: "A required status check would stay pending on PRs this filter skips."
3. **Changes under `.github/` MUST run every job.** A workflow or setup-action change can break any job, and the PR is the only place to see it.
4. **A `workflow_dispatch` trigger SHOULD run everything**, so a maintainer can re-run the full pipeline on demand.

### Jobs

5. **The PR workflow MUST include these jobs, each with its own path filter:**

| Job | Contents |
|---|---|
| `backend` | `dotnet build`, unit tests, architecture tests, `slopwatch analyze --fail-on warning` ([25 Code Quality Guards](25-code-quality-guards.md)) |
| `codegen` | Wolverine pre-generation (`codegen write`) in Release and a Release build of the result |
| `integration` | Integration tests on Testcontainers ([19 Testing Strategy](19-testing-strategy.md)) |
| `contracts` | Build regenerates the OpenAPI document; `git diff --exit-code` on it; regenerate the TS client; `git diff --exit-code` on it ([08 Code Generation and Contracts](08-code-generation-and-contracts.md)) |
| `frontend` | lint, `vue-tsc` type-check, vitest, knip, `vite build` ([21 Frontend](21-frontend.md)) |
| `e2e` | Aspire AppHost in E2E mode + Playwright; upload traces and screenshots on failure |
| `infra` | `terraform fmt -check`, `init -backend=false`, `validate` ([03 Infrastructure as Code](03-infrastructure-as-code.md)) |
| `gitleaks` | Secret scan of the PR's commits, redacted output |
| domain checks | Any repo-specific check, e.g. Firestore rules against the emulator |

6. **Every check MUST run in check mode and fail the job on a finding.** `oxlint --fix`, `eslint --fix` or `oxfmt` without `--check` in CI hide problems: the run goes green and the fix only exists on the runner. An autofix workflow that commits fixes to same-repo PRs MAY exist in addition (orginn, bcc-privacy), but the required check MUST be the non-fixing one.
7. **Hang-prone test jobs MUST report the hanging test: use `dotnet test --long-running <seconds>` on Microsoft.Testing.Platform.** A timeout alone tells you the job hung, not which test.
8. **E2E and integration jobs MUST upload Playwright traces, screenshots and test results as artifacts on failure** (`if: failure()`, `if-no-files-found: ignore`).

### Workflow hygiene

9. **Every workflow MUST declare `permissions: contents: read` at the top and widen permissions only on the job that needs it.** The default `GITHUB_TOKEN` scope may be write; least privilege limits what a compromised action can do.
10. **PR workflows MUST use `concurrency` keyed on the PR number with `cancel-in-progress: true`. Deploy, migration and Terraform workflows MUST use `cancel-in-progress: false`.** Cancelling a superseded PR run saves minutes; cancelling a deploy can abort a migration halfway or leave the Terraform state locked.
11. **Every job MUST set `timeout-minutes`.** The default is six hours.
12. **Every action MUST be pinned to a full commit SHA with the version as a comment (`uses: actions/checkout@<sha> # v7.0.1`), and Dependabot (github-actions ecosystem) MUST keep the pins current.** A tag can be moved to malicious code; a SHA cannot. See [17 Supply Chain and Security Scanning](17-supply-chain-and-security-scanning.md).
13. **Toolchain setup MUST be one composite action (`.github/actions/setup`) that reads the SDK version from `global.json` (`global-json-file`), restores dotnet tools, and optionally installs pnpm (version in sync with `devEngines`), Node and frontend dependencies with `--frozen-lockfile`.** Jobs stay short and the SDK version has one source of truth ([01 Repository Layout and Build](01-repository-layout-and-build.md)).
14. **CI SHOULD cache pnpm packages (`setup-node` with `cache: pnpm`) and SHOULD NOT cache NuGet packages.** The NuGet package folder of a .NET solution with Aspire, Wolverine, Marten and test tooling is large; the cache would be bigger than what it saves on restore and would crowd out other caches in the repository's cache quota.
15. **PR workflows MAY skip draft PRs** (`if: github.event.pull_request.draft == false`, plus the `ready_for_review` event type).

## Example

`.github/workflows/` (PR workflow with a `changes` job and an aggregate `ci` job), `.github/actions/setup/` (toolchain), `.github/dependabot.yml`.
