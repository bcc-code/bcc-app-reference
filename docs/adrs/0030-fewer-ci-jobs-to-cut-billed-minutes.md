# 0030. Fewer CI jobs to cut billed Actions minutes

- Status: Proposed
- Date: 2026-11-25
- Drafted with AI assistance; a person must review it before it is accepted.

## Context

The `bcc-code` organisation used up its GitHub Actions minutes for the month. GitHub bills each job on a hosted runner rounded up to a whole minute, and each job repeats checkout and toolchain setup. A job that does 15 seconds of work costs a full minute.

Measured on this repository before the change: CI on a pull request billed 15 minutes for about 8 minutes of job time (9 jobs), and CI on a push to `main` billed 20 (12 jobs). `contracts` ran about 66 s, most of it setup, and `gitleaks` 8 s. Apps made from the template copy its workflows, so the template sets the cost for all of them. bcc-code/vote-rewrite, which follows the same standard, billed about 45 minutes per pull request run and made the same change (bcc-code/vote-rewrite#140).

Standard 05.5 lists `contracts` and `gitleaks` as jobs of their own, each with its own path filter.

## Decision

We will run the contract checks and the secret scan as steps in jobs that already run, and deviate from standard 05.5 on job boundaries only. Every check the standard lists still runs, in check mode, on the same changes as before.

- The contract checks (OpenAPI document and generated client drift, SBOM generation) are steps at the end of `backend`. `backend` runs when the backend or the frontend changed; its build, test and format steps run only when the backend changed (step-level `if`), so a frontend-only change runs only the contract steps, as the `contracts` job did.
- `gitleaks` is the first step of `sast`, which runs on every change, as `gitleaks` did.
- A check that moved into another job still runs when an earlier step of that job failed (`if: ${{ !cancelled() }}`, plus the step it depends on having succeeded), so a red run reports as many findings as the separate jobs did.
- There is no NuGet package cache. A clean restore of the solution and its tools is 2.0 GB in 267 packages (`microsoft.playwright` alone is 0.8 GB), and `dotnet restore` from nuget.org took 12–23 s per job. Downloading and unpacking a cache that size takes about as long, and it would take a large share of the repository's 10 GB cache quota.

## Consequences

| | |
|---|---|
| Positive | About 3 fewer billed minutes per run (two jobs fewer, minus about 30 s of frontend setup in `backend`). Apps made from the template start with the lower cost. |
| Negative | A failed contract check shows as a failed `backend` job, and a gitleaks finding as a failed `sast` job; the step name and the `::error::` annotation say which check failed. `backend` gets longer by about 30 s on a backend change, and its path filter now lives partly on steps. |
| Follow-up | A merged pull request runs the full CI again on `main` before `deploy.yml`, roughly doubling the minutes per change ([tech debt](../tech-debt/ci-runs-twice-per-merged-change.md)). Propose an amendment to standard 05.5 that allows checks to share a job when each keeps its path condition. |

## Alternatives considered

- **Keep the jobs as the standard lists them** — no deviation, but the template keeps paying setup and rounding for two jobs that do seconds of work.
- **Contract checks in `frontend`** — `frontend` already installs pnpm, but it would then have to run on every backend change, and its lint, type-check, tests and build take longer than the build in `backend`.
- **NuGet package cache** (`actions/cache`, saved by `backend` on `main` only) — measured above: no faster than a restore, and a large share of the cache quota. Revisit if restores get slow or nuget.org rate-limits.
- **Larger runners or self-hosted runners** — larger runners bill more per minute; self-hosted runners need a platform decision outside this repository.
