---
standard: 05 CI Pipeline
assessed: 2026-10-19
commit: e4b2b86
updated: 2026-10-19 (gaps 1–4 of the index fixed)
---
# 05 CI Pipeline — assessment

`pr.yml` has the required shape: a `changes` job with in-workflow path filters, `.github/` changes running everything, an
aggregate `ci` job, check-mode linting, hang reporting, SHA-pinned actions, least-privilege permissions and timeouts on every
job, and one setup composite action. Wolverine code generation and a Release build now run on every PR, as a step in the
`backend` job rather than a separate `codegen` job. Gaps: test result files are not written for upload, the composite
action's pins are outside Dependabot's scan, and `gitleaks` does not hang off `changes`.

**Score: Adequate** · ✅ 10 · ⚠️ 4 · ❌ 0 · N/A 1

Commands run: `actionlint` over `.github/workflows` (no findings). Re-run after the fixes: `actionlint` clean; the new
codegen step's commands were run locally for both variants (Release build: 0 warnings, 0 errors).

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | One PR workflow; `changes` job first; others `needs: changes` + `if:` | MUST | ⚠️ | `pr.yml:26-52` computes filters with plain `git diff`; `backend`, `contracts`, `frontend`, `e2e`, `infra` declare `needs: changes` and an `if:`. `gitleaks` (`pr.yml:210-220`) has neither; it always runs, which is harmless but not the stated structure. |
| 2 | Path filtering inside the workflow for required checks; aggregate job | MUST | ✅ | No `paths:` on `pr.yml` triggers (`:6-11`); aggregate `ci` job with `if: always()` that fails on `failure`/`cancelled` (`:223-235`). `infra.yml` uses `paths:` (`infra.yml:12`) but provides no required check. |
| 3 | `.github/` changes run every job | MUST | ✅ | `pr.yml:47-49`. |
| 4 | `workflow_dispatch` runs everything | SHOULD | ✅ | `pr.yml:11`; non-PR events set every filter to true (`:43-45`). |
| 5 | Required jobs: backend, codegen, integration, contracts, frontend, e2e, infra, gitleaks | MUST | ⚠️ | `backend` builds, runs unit, architecture and Testcontainers integration tests (`:58-97`); OpenAPI diff per variant (`:73-78`); Wolverine codegen: the step "Wolverine pre-generated code compiles in Release" (`:79-90`) runs `codegen write` and `dotnet build -c Release` for every `api/BccCode.Template.Api*/` project; `contracts` regenerates the TS client and fails on diff (`:99-118`); `frontend` (`:120-136`); `e2e` per variant (`:141-194`); `infra` (`:196-208`); `gitleaks` (`:210-220`). Codegen and integration tests are steps in `backend` (filter `backend`) instead of jobs of their own, which covers the same changes. Slopwatch is absent: Deviation (ADR), [ADR 0006](../adrs/0006-no-slopwatch.md). |
| 6 | Checks in check mode; fail on findings | MUST | ✅ | `dotnet format --verify-no-changes` (`:66`); `lint` without `--fix`, `format:check` (`:131-132`; scripts in `frontend/package.json:9,12`). |
| 7 | Hang-prone tests use `--long-running` | MUST | ✅ | `dotnet test ... --long-running 120` (`pr.yml:69`). |
| 8 | E2E/integration upload traces, screenshots, results on failure | MUST | ⚠️ | E2E uploads `playwright-report/` and `test-results/` on failure (`:185-192`). `backend` uploads `**/TestResults/` (`:91-97`), but no `--report-trx` (or other report option) is passed (`grep` over workflows and props), so Microsoft.Testing.Platform writes no result files and the artifact is likely empty. |
| 9 | Top-level `contents: read`; widen per job | MUST | ✅ | Every workflow: `pr.yml:13-14`, `deploy.yml:22-23`, `infra.yml:38-39`, `preview.yml:26-27`, `codeql.yml:14-15`, `sbom.yml:14-15`, `dependabot-guard.yml:12-13`; `supply-chain-security.yml:25` uses `{}`. |
| 10 | PR concurrency per PR with cancel; deploy/TF without cancel | MUST | ✅ | `pr.yml:16-18` (cancel only for `pull_request`); `deploy.yml:157-159,211-213`, `infra.yml:107-109`, `preview.yml:30-32` use `cancel-in-progress: false`. |
| 11 | Every job sets `timeout-minutes` | MUST | ✅ | All jobs in all workflows (`grep`); only `workflow_call` jobs (`deploy.yml:71,197`) have none, where GitHub does not allow it; the called jobs set their own. |
| 12 | Actions SHA-pinned with version comment; Dependabot keeps them current | MUST | ⚠️ | All `uses:` are SHA-pinned with a version comment (`grep` finds no unpinned external ref). Dependabot's `github-actions` entry uses `directory: /` (`.github/dependabot.yml`), which covers `.github/workflows/` but not the composite action `.github/actions/setup/action.yml`, so `setup-dotnet`, `pnpm/action-setup` and `setup-node` there are not updated. |
| 13 | One setup composite action: SDK from `global.json`, tools, optional pnpm/Node/install | MUST | ✅ | `.github/actions/setup/action.yml:10-29` (`global-json-file`, `dotnet tool restore`, pnpm 12.11.2 with sync comment, `--frozen-lockfile`). The Node comment "keep in sync with frontend/package.json devEngines" (`:22`) is inaccurate: `devEngines` pins only pnpm; Node is in `engines` (`>=26`). |
| 14 | Cache pnpm, not NuGet | SHOULD | ✅ | `cache: pnpm` (`action.yml:23-24`); no NuGet cache. |
| 15 | PR workflows may skip drafts | MAY | N/A | Not adopted. Optional. |

## Gaps to close

- **8**: Pass `--report-trx` (and optionally `--results-directory`) to `dotnet test` in `pr.yml:69`, or set it once in
  `Directory.Build.props` (`TestingPlatformCommandLineArguments`), so the failure upload contains results.
- **12**: Change the Dependabot `github-actions` entry to `directories: ["/", "/.github/actions/*"]`.
- **1**: Give `gitleaks` `needs: changes` (it can keep running unconditionally) so every job follows the structure.
- **13**: Fix the Node comment in `.github/actions/setup/action.yml:22` (point at `engines.node`).

## Not verifiable in the repo

- Rule 2: that the branch ruleset requires only the `ci` check (GitHub Settings, Rules); `docs/development.md:110-111`
  lists it as a manual step.
- Rule 5: that the codegen step passes on a GitHub runner; it has only run locally.
- Rule 12: that Dependabot version updates are enabled for the repository (Insights, Dependency graph, Dependabot).
