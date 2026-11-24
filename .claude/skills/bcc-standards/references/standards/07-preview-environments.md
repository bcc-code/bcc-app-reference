# 07 Preview Environments

A preview environment is a throwaway deployment of one pull request, with its own database, login and mail catcher, created on request and destroyed when the PR closes. It lets reviewers, designers and stakeholders try a change on a real URL before merge, without touching sandbox.

The design is a label-driven workflow with a single `decide` job, a Terraform root module applied per PR, a shared preview database instance, and teardown that runs from `main`.

## Rules

### Trigger and decision

1. **A preview MUST be opt-in through a `preview` label on the PR.** Previews cost money and CI time, so they are not created for every PR.
2. **The workflow MUST start with a `decide` job that turns event, action and label into outputs (`deploy`, `destroy`, `first_time`, `component=pr<N>`, `tag=pr<N>-<sha>`), and every other job MUST key off those outputs.** All branching is in one place that can be read and tested; the step summary logs each decision.
3. **Only the `preview` label MUST trigger deploy or destroy.** Adding or removing any other label does nothing, because another label must never reseed the database under someone's review.
4. **PRs from forks MUST NOT deploy or destroy.** Forks get no OIDC token; the workflow skips them instead of failing with an IAM error.

### Infrastructure and data

5. **Each preview MUST be its own Terraform root module (`infra/preview/`) applied into workspace `pr<N>` in the dev project through the shared `bcc-platform-deploy` workflow, with every resource named `<project>-<thing>-pr<N>` and labelled with the PR.** A stray resource can always be traced to its PR, and the preview state is decoupled from the main state.
6. **Each preview MUST get its own database on a shared preview Cloud SQL instance owned by the main component, looked up by name (not by remote state).** One instance per PR is slow and expensive to create.
7. **The migration job MUST run after every apply; reset + seed MUST run only on first deploy and on reopen.** Pushes to the PR then keep the reviewer's data, while a new or reopened preview starts clean. Seeding goes through the seeding allowlist ([02 Local Development](02-local-development.md)), which includes `preview`.
8. **A preview MUST run its own OIDC mock and Mailpit, and MUST NOT use the real identity provider or send real e-mail, SMS or payments.** External integrations use the same mock clients as local development.
9. **Previews MUST scale to zero (`min_instance_count = 0`, `cpu_idle = true`).** The sticky comment says that the first request after idle is slow and that background jobs do not run while idle.

### URL, comment and deployment record

10. **The preview URL MUST be deterministic (`https://<service>-<project-number>.<region>.run.app`), so services can reference each other's URLs as strings without a Terraform cycle.** Preview services use the public `run.app` URL as a documented exception to the internal-load-balancer ingress rule in [04 GCP Runtime](04-gcp-runtime.md). They are protected by the app's (mock) login and MUST NOT hold real data (rule 20).
11. **The workflow MUST post one sticky PR comment, found by an HTML marker and updated in place, with the URLs, the commit, how to log in and how to reset data.** A second marker (`...:active`) records that the preview exists, which the teardown logic uses.
12. **Deployments MUST be recorded in one shared GitHub `preview` environment, keyed by `task: preview-pr<N>`.** Actions cannot delete environments, so per-PR environments pile up in Settings.

### Teardown

13. **Removing the label or closing the PR MUST destroy the preview, via `pull_request_target` so it runs from `main`'s workflow and infra even when the PR has conflicts.**
14. **The `pull_request_target` path MUST NEVER check out or run PR code, and the `decide` job MUST refuse to deploy from a `pull_request_target` event.** That event has secrets even for fork PRs.
15. **A manual destroy MUST exist (`workflow_dispatch` with a PR number), and the input MUST be validated as a plain number.** A typo would otherwise target a fresh, empty workspace and leave the real preview running.
16. **Preview workflows MUST use `concurrency` per PR with `cancel-in-progress: false`.** Cancelling mid-apply leaves the Terraform state locked.

### Documentation

17. **Each repo with previews SHOULD have `docs/pr-previews.md` covering: how to get a preview, how to use one, what a preview deliberately does not do, isolation between previews, how to destroy one, security (the label is not a security boundary), troubleshooting and housekeeping.** The section titles are free; the content is what a reviewer or developer needs.

### Security and housekeeping

18. **PR images MUST be cleaned up**: add an Artifact Registry cleanup policy on the `pr` tag prefix (introduced with dry run, see [03 Infrastructure as Code](03-infrastructure-as-code.md)). Tearing down the Terraform workspace does not remove `pr<N>-<sha>` images.
19. **Previews SHOULD NOT be reachable without login.** The app's (mock) login MUST cover every route including dev tools, because a preview on the `run.app` URL is reachable by anyone who has the URL. Putting the preview behind IAP is an optional extra.
20. **Previews MUST hold generated test data only, never production or personal data.** Where preview databases share one Cloud SQL instance, users created through the Admin API get `cloudsqlsuperuser`, so any preview can read any other preview's database. That is the price of letting each preview own its schema without manual grants; state it in the doc.
21. **The service account used by preview workflows MUST NOT hold `roles/owner`; grant only the roles the preview needs.** The Workload Identity binding SHOULD be scoped by event or environment, not only by repository, because `pull_request` runs workflow files from the PR branch, so write access to the repo otherwise gives access to everything that service account can do on the dev project.

## Examples

### Triggers (rules 13, 14)

```yaml
on:
  # Deploys. A conflicted PR has no merge commit, so these events do not run for it.
  pull_request:
    types: [labeled, synchronize, reopened]
  # Teardowns, from the base branch so they run for conflicted PRs too. Secrets reach fork PRs
  # here: this path must never check out or run PR code.
  pull_request_target:
    types: [unlabeled, closed]
  # Manual destroy of an orphaned preview.
  workflow_dispatch:
```

### The `decide` job (rules 2, 3, 14)

```bash
labeled)
  # Any other label must not reseed: that drops the preview's database.
  if [ "$LABEL" = "preview" ]; then
    deploy=true
    first_time=true      # brand-new database
  fi
  ;;
synchronize)
  if [ "$LABELLED" = "true" ]; then
    deploy=true          # new image, existing database: no reseed
...
# pull_request_target has secrets even for forks; it must never deploy PR code.
if [ "$EVENT" = "pull_request_target" ] && [ "$deploy" = "true" ]; then
  echo "refusing to deploy from a pull_request_target run" >&2
  exit 1
fi
```

### Migrate every apply, seed only the first time (rule 7)

The `migrate` job runs `gcloud run jobs execute "$JOB" --wait`, and adds `--container app --update-env-vars SEEDER__RESETFIRST=true` when `first_time` is true.

### Shared deployment environment (rule 12)

The `deployment` job records all previews in the shared `preview` environment, keyed by task: Actions cannot delete environments, so per-PR ones would pile up. It marks earlier deployments for the same task inactive before creating the new one.

### Deterministic URLs and naming (rules 5, 10)

```hcl
# Every preview resource is named "<project>-<thing>-<slug>" so a stray resource can always be
# traced back to its PR, and so nothing can collide with the main component's
# "<project>-<thing>-<app_environment>" names.
api_service = "${local.props.project_name}-api-${local.slug}"
...
api_url = "https://${local.api_service}-${data.google_project.main.number}.${local.props.gcp.location}.run.app"
```

### Per-PR database and the isolation caveat (rules 6, 20)

The preview database file explains why it uses password auth (Cloud SQL gives Admin-API users `cloudsqlsuperuser`, so the user owns its schema) and states the cost: any preview's user can read and write any other preview's database on this instance. Previews are separated, not isolated. It also forces `depends_on = [google_sql_user.main]` so destroy drops the database before the role.

### Preview documentation structure (rule 17)

An example layout for `docs/pr-previews.md`: Getting one, Using one, What a preview deliberately does not do, Isolation: read this once, Destroying one, Security, Troubleshooting, Housekeeping. The Security section says: "The label decides whether the pipeline builds an environment. **It is not a security boundary.**"

### Adopting previews in an app (rules 5 to 9)

An app that already has an OIDC mock, a seeder and emulators locally has what a preview needs. Steps:

1. Add a preview Cloud SQL instance to the main `infra/` (`<project>-preview-dev`, small tier) and an `infra/preview/` root module with API service, OIDC mock, database and user, secrets, migration job and service account, named `<project>-<thing>-pr<N>`.
2. Create `preview.yml` with the image list, job names and comment text for the app. Keep `decide`, the `pull_request_target` guard, the numeric input check and the shared `preview` deployment environment as described above.
3. Make the seeder runnable as a Cloud Run job with the allowlist and a `ResetFirst` override.
4. Move schema setup out of app startup, so the migration job is the only thing that changes the schema, as in production.
5. Write `docs/pr-previews.md` covering the content in rule 17.

For external systems the preview depends on, decide per service between two options:

- **Emulators in the preview** (for example a Core API emulator or Cloud Tasks): closest to local and E2E runs, nothing shared between previews, but in-memory and lost on scale-to-zero.
- **Real GCP services with per-PR names** (Pub/Sub topics and subscriptions `...-pr<N>`, a named Firestore database per PR, a Cloud Tasks queue per PR): exercises IAM and push delivery as in production, but needs more resources to create and destroy, and Firebase Auth users are project-wide. Cloud Scheduler jobs can be omitted; the sticky comment should list how to trigger the jobs by hand.

Record the choice in an ADR.

## Related

- [02 Local Development](02-local-development.md) — mocks, seeder and seeding allowlist
- [03 Infrastructure as Code](03-infrastructure-as-code.md) — Terraform and cleanup policies
- [04 GCP Runtime](04-gcp-runtime.md) — Cloud Run settings
- [05 CI Pipeline](05-ci-pipeline.md) — workflow conventions
- [06 Deployment and Release](06-deployment-and-release.md) — the sandbox and prod path
- [18 Personal Data and GDPR](18-personal-data-and-gdpr.md) — test data only in previews
- [26 Gateway and Frontend Hosting](26-gateway-and-frontend-hosting.md) — protecting hosts that must not be public
- [Ways of working](../principles/ways-of-working.md#realistic-test-data) — realistic test data
