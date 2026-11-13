# 05 CI Pipeline

This standard covers what runs on every pull request, and how the workflows themselves are written. The PR pipeline is the gate that keeps `main` deployable. It must cover backend, frontend, contracts, infrastructure and secrets, fail on problems instead of hiding them, and be fast enough that people wait for it.

Deploy workflows are in [06 Deployment and Release](06-deployment-and-release.md), scanning tools in [17 Supply Chain and Security Scanning](17-supply-chain-and-security-scanning.md).

## Rules

### Structure

1. **Each repo MUST have one PR workflow whose first job, `changes`, computes path filters; every other job declares `needs: changes` and an `if:` on its output.** Jobs whose inputs did not change are skipped, so a frontend-only PR does not wait for integration tests. Compute the filters with plain `git diff` (no third-party action). Reusable workflows (`test.yml`, `frontend.yml`, `openapi.yml`, `wolverine-codegen.yml`) are fine, so the deploy workflow can reuse them.
2. **Path filtering MUST be done inside the workflow (the `changes` job), not with `on.pull_request.paths`, for any workflow that provides a required check.** When the trigger filter skips the workflow, its required check never reports and the PR stays pending. A job skipped by `if:` reports as skipped, which satisfies a required check. Simplest setup: one final `ci` job with `needs: [all jobs]` and `if: always()` that fails when any needed job failed or was cancelled, and make only that job required. If a trigger filter must stay, record the caveat next to it: "A required status check would stay pending on PRs this filter skips."
3. **Changes under `.github/` MUST run every job.** A workflow or setup-action change can break any job, and the PR is the only place to see it.
4. **A `workflow_dispatch` trigger SHOULD run everything**, so a maintainer can re-run the full pipeline on demand.

### Jobs

5. **The PR workflow MUST include each of these jobs that applies to the repo, each with its own path filter:**

| Job | Contents |
|---|---|
| `backend` | `dotnet build`, unit tests, `dotnet format --verify-no-changes` |
| `codegen` | Wolverine pre-generation (`codegen write`) in Release and a Release build of the result |
| `integration` | Integration tests on Testcontainers ([19 Testing Strategy](19-testing-strategy.md)) |
| `contracts` | The OpenAPI document is regenerated; `git diff --exit-code` on it; regenerate the TS client; `git diff --exit-code` on it ([08 Code Generation and Contracts](08-code-generation-and-contracts.md)) |
| `frontend` | lint, `vue-tsc` type-check, vitest, knip, `vite build` ([21 Frontend](21-frontend.md)) |
| `e2e` | Aspire AppHost in E2E mode + Playwright; upload traces and screenshots on failure |
| `infra` | `terraform fmt -check`, `init -backend=false`, `validate` ([03 Infrastructure as Code](03-infrastructure-as-code.md)) |
| `gitleaks` | Secret scan of the PR's commits, redacted output |
| domain checks | Any repo-specific check, e.g. Firestore rules against the emulator |

6. **Every check MUST run in check mode and fail the job on a finding.** `oxlint --fix`, `eslint --fix` or `oxfmt` without `--check` in CI hide problems: the run goes green and the fix only exists on the runner. An autofix workflow that commits fixes to same-repo PRs MAY exist in addition, but the required check MUST be the non-fixing one.
7. **Hang-prone test jobs MUST report the hanging test: use the framework's option (for xUnit v3 on Microsoft.Testing.Platform, `dotnet test --long-running <seconds>`) or the Microsoft.Testing.Platform hang dump extension (`--hangdump --hangdump-timeout`).** A timeout alone tells you the job hung, not which test.
8. **E2E jobs MUST upload Playwright traces, screenshots and test results as artifacts on failure, and integration jobs MUST upload their test results and logs** (`if: failure()`, `if-no-files-found: ignore`).

### Workflow hygiene

9. **Every workflow MUST declare `permissions: contents: read` at the top and widen permissions only on the job that needs it.** The default `GITHUB_TOKEN` scope may be write; least privilege limits what a compromised action can do.
10. **PR workflows MUST use `concurrency` keyed on the PR number with `cancel-in-progress: true`. Deploy, migration and Terraform workflows MUST use `cancel-in-progress: false`.** Cancelling a superseded PR run saves minutes; cancelling a deploy can abort a migration halfway or leave the Terraform state locked.
11. **Every job MUST set `timeout-minutes`.** The default is six hours.
12. **Every action MUST be pinned to a full commit SHA with the version as a comment (`uses: actions/checkout@<sha> # vX`), and Dependabot (github-actions ecosystem) MUST keep the pins current.** A tag can be moved to malicious code; a SHA cannot. See [17 Supply Chain and Security Scanning](17-supply-chain-and-security-scanning.md).
13. **Toolchain setup MUST be one composite action (`.github/actions/setup`) that reads the SDK version from `global.json` (`global-json-file`), restores dotnet tools, and optionally installs pnpm (version in sync with `devEngines`), Node and frontend dependencies with `--frozen-lockfile`.** Jobs stay short and the SDK version has one source of truth ([01 Repository Layout and Build](01-repository-layout-and-build.md)).
14. **CI SHOULD cache pnpm packages (`setup-node` with `cache: pnpm`) and MAY cache NuGet packages keyed on `packages.lock.json` (`setup-dotnet` with `cache: true` and `cache-dependency-path`).** Measure restore time with and without the NuGet cache and drop it if it saves little: the package folder of a solution with Aspire, Wolverine, Marten and test tooling is large and can crowd out other caches in the repository's cache quota.
15. **PR workflows MAY skip draft PRs** (`if: github.event.pull_request.draft == false`, plus the `ready_for_review` event type).

## Examples

### `changes` job with plain `git diff`, `.github/` runs everything (rules 1, 3)

In `.github/workflows/pr.yml`:

```yaml
files=$(git diff --name-only "$BASE_SHA" HEAD)
workflows=$(echo "$files" | grep -Eq '^\.github/' && echo true || echo false)
has() { if [ "$workflows" = true ] || echo "$files" | grep -Eq "$1"; then echo true; else echo false; fi; }
echo "backend=$(has '^(api/|Directory\.|global\.json|MyApp\.slnx|\.config/|frontend/src/i18n|test-data/|tools/)')" >> "$GITHUB_OUTPUT"
echo "frontend=$(has '^(frontend/|api/MyApp\.Api/MyApp\.Api\.json)')" >> "$GITHUB_OUTPUT"
```

### Hang reporting and failure artifacts (rules 7, 8, 11)

```yaml
integration:
  timeout-minutes: 20
  steps:
    # --long-running (xUnit v3) names a test that runs past 120 s, so a hang shows which test it is.
    - run: dotnet test --project api/tests/MyApp.IntegrationTests --long-running 120
...
    - if: failure()
      uses: actions/upload-artifact@<sha> # vX
      with: { name: integration-results, path: TestResults/, if-no-files-found: ignore }
e2e:
  steps:
    ...
    - if: failure()
      uses: actions/upload-artifact@<sha> # vX
      with: { name: e2e-traces, path: e2e-results/, if-no-files-found: ignore }
```

### Contracts drift (rule 5)

Job `contracts`:

```yaml
- run: dotnet build api/MyApp.Api
- name: The committed OpenAPI document is current
  run: git diff --exit-code -- api/MyApp.Api/MyApp.Api.json || { echo "::error::Rebuild api/MyApp.Api and commit MyApp.Api.json"; exit 1; }
- run: pnpm --dir frontend openapi-ts
- name: The generated client is current
  run: git diff --exit-code -- frontend/src/client || { echo "::error::Run pnpm --dir frontend openapi-ts and commit src/client"; exit 1; }
```

### Gitleaks without a licence (rule 5)

```
docker run --rm -v "$PWD:/repo" ghcr.io/gitleaks/gitleaks:v8.30.1 git /repo --redact --log-opts="$BASE_SHA..HEAD"
```

### Composite setup reading `global.json` (rules 12, 13)

In `.github/actions/setup/action.yml`:

```yaml
- uses: actions/setup-dotnet@<sha> # vX
  with:
    global-json-file: global.json
- shell: bash
  run: dotnet tool restore
- if: inputs.frontend == 'true'
  uses: pnpm/action-setup@<sha> # vX
  with:
    version: 12.8.1 # keep in sync with frontend/package.json devEngines
```

### Reusable workflows and concurrency (rules 1, 10)

In `.github/workflows/pr-ci.yml`:

```yaml
# Keyed on the PR number: head_ref has no owner, so same-named fork branches would collide.
concurrency:
  group: ${{ github.workflow }}-${{ github.event.pull_request.number || github.ref }}
  cancel-in-progress: ${{ github.event_name == 'pull_request' }}
```

### Timeouts and draft skip (rules 11, 15)

Set `timeout-minutes` on every job, and skip drafts with `if: github.event_name == 'push' || github.event.pull_request.draft == false`.

### Aggregate required job (rule 2)

A final job that is the only required check:

```yaml
ci:
  if: always()
  needs: [changes, backend, codegen, integration, contracts, frontend, e2e, infra, gitleaks]
  runs-on: ubuntu-latest
  timeout-minutes: 5
  steps:
    - run: |
        echo '${{ toJSON(needs) }}' | jq -e 'all(.[]; .result == "success" or .result == "skipped")'
```

## Related
- [01 Repository Layout and Build](01-repository-layout-and-build.md) — `global.json`, lock files and pnpm pin read by CI
- [03 Infrastructure as Code](03-infrastructure-as-code.md) — the `infra` job
- [06 Deployment and Release](06-deployment-and-release.md) — deploy workflows and their concurrency
- [08 Code Generation and Contracts](08-code-generation-and-contracts.md) — the `contracts` job
- [17 Supply Chain and Security Scanning](17-supply-chain-and-security-scanning.md) — pinned actions, Dependabot, secret scanning
- [19 Testing Strategy](19-testing-strategy.md) — integration and E2E jobs
- [21 Frontend](21-frontend.md) — the `frontend` job
- [25 Code Quality Guards](25-code-quality-guards.md) — analyzers and formatting
- [Ways of working](../principles/ways-of-working.md) — shift-left checks
