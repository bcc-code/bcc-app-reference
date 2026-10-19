# Development

How to run, test and change the application locally.

## Prerequisites

- .NET SDK pinned in `global.json` (10.0.300 with `latestPatch`).
- Aspire CLI 13.x.
- Node and pnpm as pinned in `frontend/package.json` (`devEngines`): Node 26, pnpm 12.11.2.
- Docker (Postgres and the tests run in containers).
- Terraform 1.9 or newer, only to work on `infra/`.

## Run locally

```bash
dotnet tool restore
pnpm --dir frontend install
aspire run
```

`aspire run` starts everything the application needs: Postgres, the schema migration step, the OIDC mock, the API and the
frontend dev server. No secret or `.env` file is needed. `aspire describe` prints the real URLs and ports of the running
resources, and the dashboard lists them.

The AppHost setting `Template:Persistence` selects the API that runs: `marten` (default) or `efcore`.

```bash
Template__Persistence=efcore aspire run   # PowerShell: $env:Template__Persistence="efcore"; aspire run
```

Only one of the two APIs runs at a time. After `scripts/init-app.ps1` the setting is gone.

The browser talks to one HTTPS origin. The API proxies the Vite dev server, so cookies, CSRF and the content security policy
behave as in production (standard 02, rule 15).

## Isolated mode: worktrees and parallel agents

In a git worktree, and for every parallel agent or second checkout, always start the app isolated:

```bash
aspire start --isolated      # or: aspire run --isolated
aspire describe              # the actual URLs of this instance
```

Isolated mode uses random ports, a throwaway database and no persistent containers. Without it, two checkouts share the fixed
ports and the persistent Postgres container, and one instance silently reads and writes the other's data. The sole,
non-isolated instance uses the fixed ports listed by the AppHost.

## Personas

The OIDC mock lets you pick a persona at login. Nobody uses real credentials locally.

| Persona | Use |
|---|---|
| `user` | A plain member. Owns its own notes |
| `user2` | A second member. Use it to check that one user cannot see or change another user's notes |
| `admin` | Has the admin role and can call `GET /api/admin/stats` |

## Run the tests

```bash
dotnet test --solution BccCode.Template.slnx           # all backend tests, both persistence variants
pnpm --dir frontend run test                            # frontend unit tests (vitest)
```

The backend integration tests start Postgres with Testcontainers, so Docker must be running. They include the architecture
tests, and a test that fails when the committed OpenAPI document is stale. The checks that CI runs are listed in
`.github/workflows/pr.yml`: `dotnet format --verify-no-changes`, and in the frontend `lint`, `format:check`,
`type-check`, `test`, `knip`, `build`, plus the Playwright E2E tests against the AppHost's `E2E=true` topology for each persistence variant. Run the same commands before you push.

## Change the API contract

`api/openapi/openapi.json` is committed and is the contract, identical for both variants. A Debug build of the API project
writes it:

```bash
dotnet build api/BccCode.Template.Api.Marten           # writes api/openapi/openapi.json (Debug only)
pnpm --dir frontend run generate:api                    # regenerates the TypeScript client from it
git status                                              # commit both
```

Release builds do not write the document. CI fails when the document or the generated client is stale. Both variants must
produce the same document; the tests check it.

## Database changes

The schema changes in a separate step that runs before the API (standard 12): locally an AppHost resource that the API waits
for, in GCP the Cloud Run job `template-migrate-<env>`. The API never changes the schema when it starts. Write migrations
that the previous release can still run against (expand first, contract in a later release).

## Deploying

- `pr.yml` (workflow name `CI`) runs on pull requests and on pushes to `main`.
- `deploy.yml` starts after a green CI run on `main`: Terraform for sandbox, build the image once, migrate, roll out to sandbox,
  smoke test, then wait for approval on the GitHub environment `prod` and promote the same image digest.
- `infra.yml` plans `infra/` on pull requests and applies it. Prod infrastructure is applied from the Actions tab.
- `preview.yml` creates a preview per PR with the `preview` label. See [pr-previews.md](pr-previews.md).
- Rollback: [runbooks/rollback.md](runbooks/rollback.md). Bootstrapping a new environment: [../infra/README.md](../infra/README.md).

### Promotion

The prod deploy copies the image digest that ran in sandbox into the prod registry, so prod runs the artifact that sandbox
tested. The prod deploy account needs read access to the sandbox repository (`image_reader_members` in the sandbox tfvars).

## Repository settings

These cannot be set from the repository and are checked by hand when a repository is created from the template (standard 06,
17):

- Branch ruleset on `main`: pull requests only, required check `ci` (the aggregate job of `pr.yml`), at least one approving
  review, and code scanning results that block merge on high severity.
- Secret scanning and push protection on.
- Dependabot alerts and security updates on.
- GitHub environments `sandbox`, `prod` (required reviewers) and `preview`, with the variables listed in `infra/README.md`.
  Limit the deployment branches of `sandbox` and `prod` to `main`: the Workload Identity binding trusts the environment name.
- Actions: allow the reusable workflow `bcc-code/bcc-security-scan` and the actions pinned in `.github/workflows`.

## Conventions worth knowing

- Every GitHub Action is pinned to a full commit SHA with the version as a comment. Dependabot keeps the pins current. Pin
  reusable workflows and Terraform modules to a tag or SHA, never `main`.
- Comments in code and Terraform explain why, not what. Longer reasoning belongs in an ADR ([adrs/](adrs/README.md)).
- Out-of-scope findings go in [tech-debt/](tech-debt/README.md), one file each.
