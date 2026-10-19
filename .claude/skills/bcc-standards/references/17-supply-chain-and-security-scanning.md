# 17 Supply Chain and Security Scanning

How we control what third-party code enters a build and how we scan our own code, dependencies and history for known problems. GitHub settings (secret scanning, push protection, code scanning, required checks) are not visible in the repo and must be verified per repo.

## Rules

### Dependencies

1. **.NET dependencies MUST use Central Package Management with transitive pinning; every transitive pin MUST carry a comment naming the advisory it fixes, and lock files MUST be restored in locked mode in CI.** Details in [01 Repository Layout and Build](01-repository-layout-and-build.md). Lock files stop a restore from picking up a different version than the one reviewed.
2. **Every frontend MUST have the pnpm hardening block in `pnpm-workspace.yaml`: `minimumReleaseAge: 10080` with `minimumReleaseAgeStrict: true`, `trustPolicy: no-downgrade`, `blockExoticSubdeps: true`, `strictDepBuilds: true` with a reviewed `allowBuilds` list, and `engine-strict=true`.** The seven-day quarantine blunts attacks that publish a malicious version and wait to be installed; the trust policy rejects a version published with weaker provenance than its predecessor; `strictDepBuilds` stops unreviewed install scripts. Every exclusion (`minimumReleaseAgeExclude`, `trustPolicyExclude`, `allowBuilds: false`) MUST have a comment with the reason.
3. **Dependabot MUST be enabled for `nuget`, `npm` and `github-actions`, grouped, weekly, with a `cooldown` that matches `minimumReleaseAge`.** Without the cooldown Dependabot opens PRs that pnpm then refuses to install. bcc-privacy cross-references the two settings in comments.
4. **A pnpm repo SHOULD have a CI guard that fails when an npm lockfile appears.** Dependabot's `npm` ecosystem can create `package-lock.json`; orginn's `dependabot-guard.yml` blocks it.
5. **A monthly dependency checklist issue MAY be created by a scheduled workflow for updates Dependabot does not cover (SDK, tools, base images).** orginn: `monthly-dependency-task.yml`.

### Pinning

6. **Every GitHub Action MUST be pinned to a full commit SHA with the version in a comment.** A tag can be moved to malicious code; Dependabot keeps SHA pins current. vote-rewrite does this throughout.
7. **Reusable workflows and Terraform modules MUST be pinned to a tag or SHA, not `@main` or `ref=main`.** A change on the platform's `main` otherwise reaches our deploys unreviewed. This needs tagged releases from the platform team.
8. **Container and emulator images MUST be pinned by digest.** vote-rewrite pins the Core API, Firebase and Cloud Tasks emulators with `WithImageSHA256(...)`.

### Scanning

9. **Secret scanning MUST run on every PR (gitleaks), and GitHub secret scanning with push protection MUST be enabled on the repository.** gitleaks catches secrets in the PR diff; push protection stops them before they reach the remote. See Secret Management.
10. **SAST MUST run on every PR, with CodeQL (C# and JavaScript/TypeScript) or Semgrep, and high-severity findings MUST block merge.** See SAST (Static Application Security Testing).
11. **The build MUST generate an SBOM (CycloneDX for the .NET solution and the pnpm workspace) and attach it to each release or deployed image.** It answers "are we affected?" when an advisory lands. See Software Bill of Materials (SBOM).
12. **Repos MUST run the BCC reusable supply-chain scan (`bcc-code/bcc-security-scan`, `supply-chain-protections.yml`) on changes to workflows, manifests, lockfiles and Dockerfiles.** vote-rewrite does this, pinned to a SHA.
13. **Container image scanning SHOULD be enabled on the Artifact Registry repositories.** It finds OS-level vulnerabilities in base images that dependency scanning does not see.

### Secrets in the repo

14. **Only dev-only mock credentials MAY be committed, and they MUST be clearly named as such (for example in `appsettings.Development.json` or mock config) and work only against local mocks.** Real secrets come from Secret Manager at runtime.

## Example

`.github/workflows/` (gitleaks, CodeQL, supply-chain scan), `.github/dependabot.yml`, `frontend/pnpm-workspace.yaml` (hardening block), `Directory.Packages.props` (transitive pins with advisories), `.config/dotnet-tools.json`.
