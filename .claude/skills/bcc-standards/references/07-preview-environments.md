# 07 Preview Environments

A preview environment is a throwaway deployment of one pull request, with its own database, login and mail catcher, created on request and destroyed when the PR closes. It lets reviewers, designers and stakeholders try a change on a real URL before merge, without touching sandbox. orginn has a complete, documented implementation; this standard adopts its design as the template for the other repos.

## Rules

### Trigger and decision

1. **A preview MUST be opt-in through a `preview` label on the PR.** Previews cost money and CI time, so they are not created for every PR.
2. **The workflow MUST start with a `decide` job that turns event, action and label into outputs (`deploy`, `destroy`, `first_time`, `component=pr<N>`, `tag=pr<N>-<sha>`), and every other job MUST key off those outputs.** All branching is in one place that can be read and tested; the step summary logs each decision.
3. **Only the `preview` label MUST trigger deploy or destroy; adding or removing other labels MUST do nothing.** Another label must never reseed the database under someone's review.
4. **PRs from forks MUST NOT deploy or destroy.** Forks get no OIDC token; the workflow skips them instead of failing with an IAM error.

### Infrastructure and data

5. **Each preview MUST be its own Terraform root module (`infra/preview/`) applied into workspace `pr<N>` in the dev project through the shared `bcc-platform-deploy` workflow, with every resource named `<project>-<thing>-pr<N>` and labelled with the PR.** A stray resource can always be traced to its PR, and the preview state is decoupled from the main state.
6. **Each preview MUST get its own database on a shared preview Cloud SQL instance owned by the main component, looked up by name (not by remote state).** One instance per PR is slow and expensive to create.
7. **The migration job MUST run after every apply; reset + seed MUST run only on first deploy and on reopen.** Pushes to the PR then keep the reviewer's data, while a new or reopened preview starts clean. Seeding goes through the seeding allowlist ([02 Local Development](02-local-development.md)), which includes `preview`.
8. **A preview MUST run its own OIDC mock and Mailpit, and MUST NOT use the real identity provider or send real e-mail, SMS or payments.** External integrations use the same mock clients as local development.
9. **Previews MUST scale to zero (`min_instance_count = 0`, `cpu_idle = true`).** The sticky comment says that the first request after idle is slow and that background jobs do not run while idle.

### URL, comment and deployment record

10. **The preview URL MUST be deterministic (`https://<service>-<project-number>.<region>.run.app`), so services can reference each other's URLs as strings without a Terraform cycle.**
11. **The workflow MUST post one sticky PR comment, found by an HTML marker and updated in place, with the URLs, the commit, how to log in and how to reset data.** A second marker (`...:active`) records that the preview exists, which the teardown logic uses.
12. **Deployments MUST be recorded in one shared GitHub `preview` environment, keyed by `task: preview-pr<N>`.** Actions cannot delete environments, so per-PR environments pile up in Settings.

### Teardown

13. **Removing the label or closing the PR MUST destroy the preview, via `pull_request_target` so it runs from `main`'s workflow and infra even when the PR has conflicts.**
14. **The `pull_request_target` path MUST NEVER check out or run PR code, and the `decide` job MUST refuse to deploy from a `pull_request_target` event.** That event has secrets even for fork PRs.
15. **A manual destroy MUST exist (`workflow_dispatch` with a PR number), and the input MUST be validated as a plain number.** A typo would otherwise target a fresh, empty workspace and leave the real preview running.
16. **Preview workflows MUST use `concurrency` per PR with `cancel-in-progress: false`.** Cancelling mid-apply leaves the Terraform state locked.

### Documentation

17. **Each repo with previews MUST have `docs/pr-previews.md` with these sections: getting one, using one, what a preview deliberately does not do, isolation, destroying one, security (the label is not a security boundary), troubleshooting, housekeeping.** orginn's file is the template.

### Known gaps to address when adopting

18. **PR images MUST be cleaned up**: add an Artifact Registry cleanup policy on the `pr` tag prefix (introduced with dry run, see [03 Infrastructure as Code](03-infrastructure-as-code.md)). orginn's teardown does not remove `pr<N>-<sha>` images.
19. **Previews SHOULD NOT be publicly reachable.** Put them behind the platform gateway / IAP, or at minimum require app login on every route including dev tools. orginn previews use `INGRESS_TRAFFIC_ALL` and are reachable by anyone with the URL.
20. **Preview databases share `cloudsqlsuperuser`, so any preview can read any other preview's database. Previews MUST hold generated test data only, never production or personal data.** This is the price of letting each preview own its schema without manual grants; state it in the doc.
21. **The Workload Identity binding used by preview workflows SHOULD be scoped by event or environment, not only by repository.** orginn's doc notes that `pull_request` runs workflow files from the PR branch, so write access to the repo already gives access to an `owner` service account on the dev project.

## Example

`.github/workflows/preview.yml` (label-driven, teardown from `main`), `infra/preview/` and `docs/pr-previews.md`, adapted from orginn. Project-specific values are marked TODO.
