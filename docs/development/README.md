# Development

How to run, test and change the application locally, split by task (standard 23.3).

| Page | What |
|---|---|
| [Run locally](run-locally.md) | Prerequisites, `aspire run`, what starts |
| [Isolated mode](isolated-mode.md) | Worktrees, parallel agents and second checkouts |
| [Personas](personas.md) | Test users in the OIDC mock |
| [Tests](tests.md) | Unit, integration, architecture and E2E tests, and the checks CI runs |
| [Contract flow](contract-flow.md) | Change the API and regenerate the OpenAPI document and the client |
| [Database and migrations](database-and-migrations.md) | EF Core migrations, history twins, the schema step, previews |
| [Messaging locally](messaging-locally.md) | Internal messages, the local inbox and the durability pass |
| [Repository settings](repository-settings.md) | What cannot be set from the repository |

Other places: decisions in [adrs/](../adrs/README.md), operations in [runbooks/](../runbooks/README.md), previews in
[pr-previews.md](../pr-previews.md), infrastructure in [../../infra/README.md](../../infra/README.md).

## Deploying

- `ci.yml` (the CI workflow) runs on pull requests and on pushes to `main`. Its aggregate job `ci` is the one required check.
- `deploy.yml` starts after a green CI run on `main`: build the image once, Terraform for sandbox through the platform workflow,
  push, the schema step, rollout to sandbox, smoke test, then wait for approval on the GitHub environment `prod`, which pushes the
  same image tarball to the prod registry and checks that the digest equals sandbox's
  ([ADR 0014](../adrs/0014-build-once-deploy-after-green-ci.md)).
- `infra.yml` plans, applies or destroys `infra/` by hand from the Actions tab (the only way prod infrastructure is applied).
- `preview.yml` creates a preview per PR with the `preview` label ([pr-previews.md](../pr-previews.md)).
- `provision-project.yml` provisions a project through the landing zone. Bringing a new environment up:
  [first-deploy runbook](../runbooks/first-deploy.md). Rollback: [rollback runbook](../runbooks/rollback.md).

Prod has its own project and registry, so the image is not pulled from sandbox: `deploy-prod` loads the same tarball and pushes it, and stops before anything changes if the registry digest differs from sandbox's. Prod runs the artifact that sandbox tested.

## Conventions worth knowing

- Every GitHub Action is pinned to a full commit SHA with the version as a comment. Dependabot keeps the pins current. Pin
  reusable workflows and Terraform modules to a tag or SHA, never `main`. The platform's `terraform.yml@main` is the exception
  its federation requires ([ADR 0013](../adrs/0013-cloud-run-behind-the-platform-gateway.md)).
- Comments in code and Terraform explain why, not what. Longer reasoning belongs in an ADR.
- Out-of-scope findings go in [tech-debt/](../tech-debt/README.md), one file each.
