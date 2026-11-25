# 0030. Fewer CI jobs and no second run on main, to cut billed Actions minutes

- Status: Proposed
- Date: 2026-11-25
- Drafted with AI assistance; a person must review it before it is accepted.

## Context

The `bcc-code` organisation used up its GitHub Actions minutes for the month. GitHub bills each job on a hosted runner rounded up to a whole minute, and each job repeats checkout and toolchain setup. A job that does 15 seconds of work costs a full minute.

Measured on this repository before the change: CI on a pull request billed 15 minutes for about 8 minutes of job time (9 jobs), and CI on a push to `main` billed 20 (12 jobs). `contracts` ran about 66 s, most of it setup, and `gitleaks` 8 s. A merged pull request then ran the full CI again on `main`, on files its pull request run had already tested, before `deploy.yml` could start. Apps made from the template copy its workflows, so the template sets the cost for all of them. bcc-code/vote-rewrite, which follows the same standard, billed about 45 minutes per pull request run and about 46 per push to `main`, and made the same changes (bcc-code/vote-rewrite#140, bcc-code/vote-rewrite#141).

Standard 05.5 lists `contracts` and `gitleaks` as jobs of their own, each with its own path filter. Standard 06.2 requires green tests for the exact commit being deployed.

## Decision

### Fewer jobs

We will run the contract checks and the secret scan as steps in jobs that already run, and deviate from standard 05.5 on job boundaries only. Every check the standard lists still runs, in check mode, on the same changes as before.

- The contract checks (OpenAPI document and generated client drift, SBOM generation) are steps at the end of `backend`. `backend` runs when the backend or the frontend changed; its build, test and format steps run only when the backend changed (step-level `if`), so a frontend-only change runs only the contract steps, as the `contracts` job did.
- `gitleaks` is the first step of `sast`, which runs on every change, as `gitleaks` did.
- A check that moved into another job still runs when an earlier step of that job failed (`if: ${{ !cancelled() }}`, plus the step it depends on having succeeded), so a red run reports as many findings as the separate jobs did.
- There is no NuGet package cache. A clean restore of the solution and its tools is 2.0 GB in 267 packages (`microsoft.playwright` alone is 0.8 GB), and `dotnet restore` from nuget.org took 12–23 s per job. Downloading and unpacking a cache that size takes about as long, and it would take a large share of the repository's 10 GB cache quota.

### No second run on main for a tree a pull request already tested

- When CI passes for a pull request, the `ci` job uploads an empty marker artifact named `ci-passed-<tree>-<base>`: the tree of the tested merge commit (`refs/pull/N/merge`) and the commit the path filter compared it with (`pull_request.base.sha`). It is kept 30 days.
- On a push to `main`, the `tested` step of `changes` takes the merge commit's second parent (the pull request's head), looks for a successful same-repository pull request run of that head with a marker for the pushed commit's tree, and checks that `<base>` has a successful CI run on `main`. When both hold, every path filter is false: the path-filtered jobs are skipped, `sast` still runs, and `ci` succeeds.
- Anything else runs in full: a squash or rebase merge (no second parent), `main` moved after the pull request's last run (another tree), a fork, a failed or cancelled pull request run, an expired marker, a base without a green run on `main` (a stacked pull request, a broken `main`), a manual run, or any error in the step (`continue-on-error`).
- `deploy.yml` is unchanged: the push run still succeeds for the exact commit, so its `workflow_run` trigger and its gate work as before. We read standard 06.2 as met: the deployed commit's tree is the one the pull request run tested, and the jobs that run skipped cover files that are the same in `<base>`, which passed them on `main`. The base check is what makes that hold; without it, a frontend-only pull request merged onto a broken `main` would deploy a backend that failed.
- The skip needs merge commits, so the repository should allow only "Create a merge commit", and should require branches to be up to date before merging, so the tree on `main` is the tree that was tested.

## Consequences

| | |
|---|---|
| Positive | About 3 fewer billed minutes per run from the job merge. A merge whose tree was tested bills about 3 minutes on `main` (`changes`, `sast`, `ci`) instead of about 17–20, so deploys also start sooner. Apps made from the template start with the lower cost. |
| Negative | A failed contract check shows as a failed `backend` job, and a gitleaks finding as a failed `sast` job; the step name and the `::error::` annotation say which check failed. `backend` gets longer by about 30 s on a backend change, and its path filter now lives partly on steps. The marker comes from the pull request's own `ci.yml`, so a reviewed pull request that weakens CI can also produce a marker; that is the same exposure as weakening CI directly. The skip depends on the merge settings: with a squash merge, every push to `main` runs in full again. |
| Follow-up | Measure both changes once the Actions budget allows runs. Propose an amendment to standard 05.5 that allows checks to share a job when each keeps its path condition. |

## Alternatives considered

- **Keep the jobs as the standard lists them** — no deviation, but the template keeps paying setup and rounding for two jobs that do seconds of work.
- **Contract checks in `frontend`** — `frontend` already installs pnpm, but it would then have to run on every backend change, and its lint, type-check, tests and build take longer than the build in `backend`.
- **NuGet package cache** (`actions/cache`, saved by `backend` on `main` only) — measured above: no faster than a restore, and a large share of the cache quota. Revisit if restores get slow or nuget.org rate-limits.
- **Merge queue** (`merge_group`) — tests the merged result once before it lands, and works with squash merges. It needs a ruleset change, `merge_group` in `ci.yml`, and a different signal for `deploy.yml`, whose gate looks for a successful push run.
- **Link the push to the pull request through the API** (`commits/{sha}/pulls`) instead of the second parent — would work with squash merges too, but the squashed commit's tree only equals the tested tree when the branch was up to date, and it adds another API call that can fail.
- **Larger runners or self-hosted runners** — larger runners bill more per minute; self-hosted runners need a platform decision outside this repository.
