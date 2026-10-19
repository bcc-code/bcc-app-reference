# PR preview environments

Every pull request can get its own throwaway deployment: its own API revision, and when the TODOs below are done its own
database and login. It is created with a label and destroyed when the PR closes. The rules are standard 07; the workflow is
`.github/workflows/preview.yml`; the Terraform is `infra/preview/`.

**Status: starting point.** The workflow and the API service work. The database, the migration job, the OIDC mock and
restricted access are not in `infra/preview/` yet (see `TODO` in `infra/preview/main.tf` and `preview.yml`). Until they are,
a preview does not start a usable application.

## Getting one

Add the **`preview`** label to the pull request.

```bash
gh pr edit <number> --add-label preview
```

A comment on the PR with the URL appears when the deploy has finished. It is updated in place on every push, so there is one.
Previews work for branches in this repository only. A fork gets no OIDC token, so the workflow skips it. A PR with merge
conflicts gets no preview, because it has no merge commit.

## Using one

- The first request after an idle spell is slow: previews scale to zero.
- Background jobs do not run while the preview is idle (CPU is allocated during requests only).
- Log in through the preview's own OIDC mock. It must never use the real identity provider.
- Reset the data: run the migration job with the reset override once it exists (`preview.yml`, job `migrate`).

## What a preview deliberately does not do

- No real identity provider, e-mail, SMS or payments.
- No production data, ever.
- No telemetry export to shared collectors.

## Isolation

Previews are separated, not isolated. Each gets its own database on a shared preview Cloud SQL instance, but Cloud SQL gives
every user it creates the `cloudsqlsuperuser` role, so any preview can read any other preview's database. That is acceptable
because previews hold generated test data only. Previews are also reachable by anyone with the URL until access is restricted
(standard 07, rule 19); treat what you put in one as public.

## Destroying one

Remove the label or close the PR, merged or not:

```bash
gh pr edit <number> --remove-label preview
```

Teardown runs from `main`'s workflow and infra code through `pull_request_target`, so it works for conflicted PRs. That path
never checks out PR code. To destroy an orphan by hand: Actions, PR preview, Run workflow, with the PR number (digits only).

## Security

The label decides whether the pipeline builds an environment. **It is not a security boundary.** Preview jobs run PR code in
the GitHub environment `preview` with a service account that has `roles/run.admin` in the sandbox project, so it can create,
change and delete any Cloud Run service there. It can act only as the shared preview runtime account, not as other service
accounts. Anyone who can open a PR in the repository can run a workflow as that account, with or without this feature. The
account exists in sandbox only and is bound to the environment name `preview` (`infra/iam.tf`). Scope `run.admin` further with
IAM conditions when previews are adopted.

## Troubleshooting

- **Where the logs are:** the `PR preview` workflow run of the PR; application logs in Cloud Logging, filtered by
  `resource.labels.service_name="template-api-pr<number>"`.
- **"Error acquiring the state lock":** someone cancelled a run by hand. Release the lock in the state bucket; the workflow uses
  `cancel-in-progress: false` to avoid this.
- **Removing the label did nothing:** run the manual destroy above.

## Housekeeping

- Images tagged `pr<number>-<sha>` are removed by the Artifact Registry cleanup policy after 30 days (`infra/artifact_registry.tf`).
- Each previewed PR leaves a deleted workspace entry in the state bucket under `template/preview`.
- The shared GitHub environment `preview` holds one deployment per PR, keyed by task. Teardown deletes the deployment.

## Variables

The GitHub environment `preview` needs `GCP_PROJECT_ID`, `GCP_REGION`, `GCP_WORKLOAD_IDENTITY_PROVIDER`,
`GCP_PREVIEW_SERVICE_ACCOUNT`, `GCP_ARTIFACT_REPOSITORY` and `TF_STATE_BUCKET`, all with the sandbox values. The repository
variable `PREVIEW_HAS_DATABASE` is set to `true` when the migration job exists.
