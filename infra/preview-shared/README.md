# Shared Terraform for the preview project

What every PR preview uses: the enabled APIs, the Artifact Registry repository `ar-bcc-template-<env>` (images tagged `pr<N>-<sha>`, cleaned up after 14 days), the Data Protection key (key ring `bcc-template-<env>`, key `data-protection`) and the CI account's membership in the database's admin group. `init-app.ps1` replaces `template` with the app's slug.

Applied once to the landing zone's `dev` environment, which holds the previews, through `infra.yml` with environment `dev` (component `main`). The per-PR root in `infra/preview/` looks these up by name, not through remote state.
