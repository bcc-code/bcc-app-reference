# 17 Supply Chain and Security Scanning

How we control what third-party code enters a build and how we scan our own code, dependencies and history for known problems. The main ideas are pinned and quarantined dependencies, pinned workflows and images, and automated scanning on every PR. GitHub settings (code scanning, required checks) are not visible in the repo and must be verified per repo.

## Rules

### Dependencies

1. **.NET dependencies MUST use Central Package Management with transitive pinning; every transitive pin MUST carry a comment naming the reason (an advisory id, a bug fix or a version conflict), and lock files MUST be restored in locked mode in CI.** Details in [01 Repository Layout and Build](01-repository-layout-and-build.md). Lock files stop a restore from picking up a different version than the one reviewed.
2. **Every frontend MUST have the pnpm hardening block in `pnpm-workspace.yaml`: `minimumReleaseAge: 10080` with `minimumReleaseAgeStrict: true`, `trustPolicy: no-downgrade`, `blockExoticSubdeps: true`, `strictDepBuilds: true` with a reviewed `allowBuilds` list, and `engineStrict: true`, plus `engine-strict=true` in the frontend `.npmrc` so npm (for example run by a bot without pnpm support) refuses to install (both check the `engines` field, not `devEngines`).** The seven-day quarantine blunts attacks that publish a malicious version and wait to be installed; the trust policy rejects a version published with weaker provenance than its predecessor; `strictDepBuilds` stops unreviewed install scripts. Every exclusion (`minimumReleaseAgeExclude`, `trustPolicyExclude`, `allowBuilds: false`) MUST have a comment with the reason.
3. **Dependabot MUST be enabled for `nuget`, `npm` and `github-actions`, grouped, weekly, with a `cooldown` that matches `minimumReleaseAge`.** Without the cooldown Dependabot opens PRs that pnpm then refuses to install. Cross-reference the two settings in comments.
4. **A pnpm repo SHOULD have a CI guard that fails when an npm lockfile appears.** Dependabot's `npm` ecosystem can create `package-lock.json`; a guard workflow blocks it.
5. **A monthly dependency checklist issue MAY be created by a scheduled workflow for updates Dependabot does not cover (SDK, tools, base images).**

### Pinning

6. **Every GitHub Action MUST be pinned to a full commit SHA, with the version in a comment where a tag exists.** A tag can be moved to malicious code; Dependabot keeps SHA pins current.
7. **Reusable workflows and Terraform modules MUST be pinned to a full commit SHA (with a version comment where a tag exists); `@main` and `?ref=main` are not allowed.** A change on the platform's `main` (for example in `bcc-platform-deploy` workflows or the gateway Terraform modules) otherwise reaches our deploys unreviewed. Exception: the `bcc-platform-deploy` workflows that authenticate to GCP (`terraform.yml`, `landing-zone.yml`) are called at `@main` with a comment, because the platform's workload identity only trusts their `job_workflow_ref` at `refs/heads/main` (03.5).
8. **Container images that are deployed MUST be pinned by digest; local emulator images SHOULD be, and the update path MUST be stated.** Dependabot does not update digests written in C# app host code, so name how they are refreshed (for example the monthly checklist in rule 5). For example, pin emulators started by the local app host with `WithImageSHA256(...)`.

### Scanning

9. **Secret scanning MUST run on every PR (gitleaks).** gitleaks catches secrets in the PR diff. It runs after the push, so a secret it finds is already on the remote and must be rotated. See [Security principles](../principles/security.md).
10. **SAST MUST run on every PR, with CodeQL (C# and JavaScript/TypeScript) or Semgrep, and high-severity findings MUST block merge.** See [Security principles](../principles/security.md).
11. **The build MUST generate an SBOM (CycloneDX for the .NET solution and the pnpm workspace) and attach it to each release or deployed image.** It answers "are we affected?" when an advisory lands.
12. **Repos MUST run the BCC reusable supply-chain scan (`bcc-code/bcc-security-scan`, `supply-chain-protections.yml`) on changes to workflows, manifests, lockfiles and Dockerfiles.** Pin it to a SHA.
13. **Container image scanning SHOULD be enabled on the Artifact Registry repositories.** It finds OS-level vulnerabilities in base images that dependency scanning does not see.

### Secrets in the repo

14. **Only dev-only mock credentials MAY be committed, and they MUST be clearly named as such (for example in `appsettings.Development.json` or mock config) and work only against local mocks.** An OIDC-mock or emulator value is acceptable. Real secrets come from Secret Manager at runtime.

## Examples

### pnpm hardening block (rule 2)

```yaml
engineStrict: true
minimumReleaseAge: 10080 # 7 days
minimumReleaseAgeStrict: true
minimumReleaseAgeExclude:
  - '@bcc-code/*'
trustPolicy: no-downgrade
...
blockExoticSubdeps: true
strictDepBuilds: true
allowBuilds:
  esbuild: true
  vue-demi: true
  # Reviewed: <package>'s postinstall script does X, which we do not need; skipping it is safe.
  '<package>': false
```

The `@bcc-code/*` exclusion is explained in a comment ("our own npm scope, published by us"). Document each `trustPolicyExclude` and `overrides` entry with the reason (advisory ids where they apply).

### Dependabot cooldown aligned with pnpm (rule 3)

```yaml
# Matches minimumReleaseAge/minimumReleaseAgeExclude in frontend/pnpm-workspace.yaml.
cooldown:
  default-days: 7
  exclude:
    - "@bcc-code/*"
```

### npm lockfile guard (rule 4)

A `dependabot-guard.yml` workflow fails a Dependabot PR that contains `package-lock.json` or lacks `pnpm-lock.yaml`.

### SHA-pinned actions and supply-chain scan (rules 6, 12)

```yaml
uses: bcc-code/bcc-security-scan/.github/workflows/supply-chain-protections.yml@<sha> # v1
```

### gitleaks on PRs (rule 9)

A `gitleaks` job in the PR workflow runs the gitleaks CLI in a container (`--redact`, PR range only), because `gitleaks-action` needs a paid licence for organisation repos.

### Lock files in locked mode (rule 1)

In `Directory.Build.props`, set `RestorePackagesWithLockFile` and enable `RestoreLockedMode` when `CI=true`.

## Related

- [01 Repository Layout and Build](01-repository-layout-and-build.md) — Central Package Management and lock files
- [03 Infrastructure as Code](03-infrastructure-as-code.md) — pinned Terraform modules
- [05 CI Pipeline](05-ci-pipeline.md) — where the scans run
- [25 Code Quality Guards](25-code-quality-guards.md) — other automated checks
- [Security principles](../principles/security.md) — SAST, SBOM, dependency scanning and secret management
- [Ways of working](../principles/ways-of-working.md#secops-shift-left) — shifting security left
