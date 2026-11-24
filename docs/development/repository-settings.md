# Repository settings

These cannot be set from the repository and are checked by hand when a repository is created from the template (standard 06, 17):

- Branch ruleset on `main`: pull requests only, required check `ci` (the aggregate job of the CI workflow), at least one approving
  review. The `ci` job includes the Semgrep `sast` job, so there is no separate code scanning check to require, and no CodeQL (it needs a paid licence on a private repository).
- Secret scanning and push protection on.
- Dependabot alerts and security updates on.
- GitHub environments `sandbox`, `prod` (required reviewers), `dev` (for previews, allows PR branches, no required reviewers) and
  `preview` (collects the per-PR deployment records). The landing zone writes the variables of `sandbox`, `prod` and `dev`
  ([first-deploy runbook](../runbooks/first-deploy.md)). Limit the deployment branches of `sandbox` and `prod` to `main`: the
  workload identity binding trusts the environment name.
- Actions: allow the reusable workflows `bcc-code/bcc-security-scan` and `bcc-code/bcc-platform-deploy`, and the actions pinned in
  `.github/workflows`.
- The org-level variables and secrets for telemetry (`OTEL_EXPORTER_OTLP_*`) must be visible to the repository.
