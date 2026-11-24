# 0014. Build once and deploy after green CI, Terraform first

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADR 0015.

## Context

Standard 06 requires that a deploy depends on green tests for the exact commit, that the same image digest moves through the environments, that Terraform and the app deploy do not race, and that each deploy ends with a smoke test.

## Decision

We will deploy from `.github/workflows/deploy.yml`, triggered when the CI workflow succeeds on a push to `main` (`workflow_run`), and manually. One run:

1. Builds the frontend, generates the Wolverine code ([0012](0012-pre-generated-wolverine-code-in-production.md)) and publishes the container image once, tagged with the commit SHA.
2. Applies the Terraform for the environment through the platform's reusable `terraform.yml`, so infrastructure is in place before the rollout and never races it. The apply runs only when the deployed commit is the tip of `main`, because the called workflow checks out the tip: an older-commit deploy, such as a re-deploy of a known-good commit, keeps the current infrastructure.
3. Pushes the image, applies the database schema ([0016](0016-schema-applied-by-a-deploy-step.md)), uploads the content-hashed assets to the bucket without deleting old ones, rolls out the Cloud Run service by image digest, and runs a smoke test.

The sandbox Terraform and deploy jobs and `infra.yml` (manual plan, apply and destroy) share one concurrency group per environment and are never cancelled; the group is on the jobs, and `deploy-prod` has its own, so a pending prod approval does not block later sandbox deploys. The smoke test goes through the gateway and fails with an error when `APP_BASE_URL` is empty. The sandbox job outputs the digest; the prod job runs behind the `prod` environment with required reviewers, pushes the same image tarball to the prod registry and stops if the digest differs from the sandbox digest, so prod runs the artifact that ran in sandbox. Prod infrastructure is applied by hand with `infra.yml` and must be current before a release that changes `infra/`. A traffic rollback is described in the [rollback runbook](../runbooks/rollback.md).

## Consequences

| | |
|---|---|
| Positive | Only commits with green CI reach sandbox; prod runs the artifact that ran in sandbox. A broken rollout fails the run. |
| Negative | Every deploy runs a Terraform apply, which adds about a minute. A deploy starts only after the whole CI workflow has finished on `main`. |
| Follow-up | Schema changes are expand-only, so the previous revision runs against the new schema and a traffic rollback stays safe (standard 06.12). |

## Alternatives considered

- **Separate infra and deploy workflows on the same push** — they would race on a push that touches both (standard 06.10).
- **Call the CI workflow from the deploy workflow** — runs every check twice on `main`.
- **Rebuild per environment** — prod would run an artifact that was never tested (standard 06.3).
