# PR previews

A preview is a copy of a pull request running on a real URL, with generated test data, so a reviewer can try the change before it is merged. Why it is built this way: [ADR 0027](adrs/0027-pr-previews-in-a-preview-project.md). The workflow is `.github/workflows/preview.yml`; the Terraform is in `infra/preview/` and `infra/preview-shared/`.

## Get a preview

Add the `preview` label to a pull request from a branch in this repository. Pull requests from forks never get a preview.

```bash
gh pr edit <number> --add-label preview
```

The workflow builds the image from the PR's head commit, applies the database schema and seeds the test data, then applies Terraform. It then posts or updates one sticky comment on the PR with the URL and the commit. A first deploy takes several minutes.

Every push to the PR updates the preview. A push applies schema changes (`db-apply`) and keeps the data. The first deploy, and a reopen, drop and recreate the preview's schema `pr<N>`, apply it and seed it again. So does a deploy that finds the schema missing. Removing and adding the label again resets the data.

A closed PR never gets a preview, even if the label is added to it.

## Use a preview

Open the URL from the comment. You sign in on the OIDC mock's login page by picking a persona (the list is in `api/OidcMock/Personas.cs`, with the roles in [personas](development/personas.md)). The mock plays BCC Signon, so the sign-in works like the local one.

The preview scales to zero. The first request after an idle spell is slow while the services start.

## What a preview does not do

- It has no BCC Signon. `api/OidcMock` stands in for it. It sends no integration events to BCC PubSub.
- Scheduled messages and retries can be late. The durability pass runs every 30 minutes, not every minute, so an idle preview can scale to zero.
- The data is not kept. It is generated test data and goes when the preview is destroyed.
- There is no gateway route and no `<app>.sandbox.bcc.no` hostname. The URL is the `run.app` one.

## Isolation

Previews run in the landing zone's `dev` project, which the app uses for nothing else. Nothing in it is shared with sandbox: no database, service account, topic or key. A preview workflow that runs PR code can reach other previews, not sandbox.

Previews are separated from sandbox and not isolated from each other. All previews use one database, one schema each, and every preview's runtime account is in the same admin group, so one preview could read another's schema. This is why a preview must hold generated test data only (standard 07.20). Never put production or personal data in one.

Each preview has its own Terraform workspace `pr<N>`, its own secrets and its own Pub/Sub topic. Wolverine's advisory locks follow the schema name, so previews do not block each other, except while two of them run `db-apply` at once.

The `data` job of the preview workflow runs the PR-built api and testdata images with the CI account's database identity, through the Cloud SQL Auth Proxy. That is code from the PR running with database access, so previews are for same-repo PRs only: `preview-decide.sh` refuses forks. This is the accepted trade-off of standard 07, together with the shared database described above.

The preview project's CI account (`sa-gha-cicd`) comes from the landing zone and may hold more roles than the preview needs. The follow-up to narrow it is in ADR 0027 (standard 07.21).

## Destroy a preview

Remove the `preview` label or close the PR. The workflow drops the schema, destroys the Terraform workspace, edits the sticky comment to say the preview is destroyed and removes the deployment records.

```bash
gh pr edit <number> --remove-label preview
```

GitHub keeps only one queued run per PR, so a later event can replace a queued teardown. Any later event destroys the preview again while the sticky comment still marks it active and the label is gone or the PR is closed.

To destroy one by hand, run the `PR preview` workflow from the Actions tab with the PR number. The number must be plain digits; the run stops otherwise. This also removes a preview whose PR was deleted or whose label state is lost.

Teardown works when the preview never finished its first apply. The schema drop tolerates a missing schema, and destroying an empty Terraform workspace succeeds.

## Security

The label is not a security boundary. Anyone with write access to the repository can add it, and `pull_request` runs the workflow file from the PR branch, so a branch can run its own code in the preview project with the preview CI account. That is why the project holds nothing but previews, and why the account should not hold more than it needs.

Teardown on `pull_request_target` (label removed, PR closed) gets secrets even for fork PRs, so it checks out only the base branch and never runs PR code. A teardown started by a `pull_request` event (a push after the label is gone) runs the PR's own Terraform, as every deploy of that same-repo PR already did.

The preview URL is public, as the exception in standards 04.2 and 07.10 allows. The API requires a sign-in on every route. The OIDC mock lets anyone pick a persona, so anyone with the URL can sign in as any of them, including the administrator. The OIDC mock is reachable on its own `run.app` URL as well. Treat the URL as open and the data as public.

## Troubleshooting

- **No comment, no run.** Look at the `decide` job's summary: it prints the event, the action, the label and whether the comment marks the preview active. A fork PR, a closed PR and a label other than `preview` all give no deploy.
- **A job fails on the `dev` environment.** The jobs that touch GCP run in the GitHub environment `dev`. It must allow PR branches and have no required reviewers. The environment `preview` only holds the per-PR deployment records.
- **`data` fails to connect.** Cloud SQL can take minutes to see the CI account's group membership after the first apply. The job retries for a few minutes, then fails; run the failed job again.
- **`provision` fails on a first deploy with "container failed to start".** The API stops when it cannot reach the database. Its account joins the database group in the `access` job, which runs while the images build; when that is not enough time, the API log shows `403 cloudsql.instances.get`. Wait a few minutes and run the failed job again.
- **Sign-in fails with `invalid_grant`.** The OIDC mock keeps its keys and codes in memory and is pinned to one instance. If it scaled to zero between the login page and the callback, the login that started before the restart fails with `invalid_grant`. Start the login again. A session that already exists survives, because sessions are in Postgres.
- **The preview is slow or answers 503 right after idle time.** The services are starting. Wait and retry.
- **The data looks wrong.** Remove and add the label to reseed.
- **"Error acquiring the state lock".** A run was cancelled or lost. Runs of one PR are queued, never cancelled, because a cancelled apply leaves the Terraform state locked. If the state is locked after a crash, unlock it from the platform's state storage before running again.
- **Where the logs are:** the `PR preview` workflow run of the PR; application logs in Cloud Logging of the `dev` project, filtered by `resource.labels.service_name="bcc-template-api-pr<number>"`.

## Housekeeping

- Images in `ar-bcc-template-dev` with a `pr` tag prefix are removed by the registry's cleanup policy when they are older than 14 days (standard 07.18). Once it deletes, a preview with no push for 14 days loses its images and cannot start from zero; push to the PR, or remove and add the label.
- A closed PR that still has a preview, such as one whose teardown failed, shows up as Cloud Run services labelled `template-pr` in the `dev` project. Destroy it by hand as above.
- The shared part (`infra/preview-shared/`) is applied once through `infra.yml` with environment `dev`, component `main`. It holds the enabled APIs, the registry, the Data Protection key and the CI account's database group membership.
- The GitHub environment `preview` collects one deployment per PR (task `preview-pr<N>`). Teardown deletes them.
