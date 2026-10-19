---
standard: 17 Supply Chain and Security Scanning
assessed: 2026-10-19
commit: e4b2b86
---
# 17 Supply Chain and Security Scanning — assessment

Dependency controls are in place: Central Package Management with locked restore, the pnpm hardening block, Dependabot
with matching cooldowns, SHA-pinned actions and modules, gitleaks, CodeQL and the BCC supply-chain scan. Container images
are not pinned by digest, the deploy workflow claims a base-image setting that does not exist, and Artifact Registry
scanning is not enabled.

**Score: Adequate** · ✅ 9 · ⚠️ 2 · ❌ 2 · N/A 1

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | CPM with transitive pinning, pins cite advisories, locked restore in CI | MUST | ✅ | `Directory.Packages.props:3-10` (`CentralPackageTransitivePinningEnabled`, MessagePack pin cites GHSA-qhrr-8q5h-9q3h); `Directory.Build.props:10,22-25` (`RestorePackagesWithLockFile`, `RestoreLockedMode` when `CI=true`); `CI: 'true'` in `pr.yml:21`, `deploy.yml:26`. The AppHost has no lock file, with the reason at `Directory.Build.props:12-16` |
| 2 | pnpm hardening block | MUST | ✅ | `frontend/pnpm-workspace.yaml` — `minimumReleaseAge: 10080`, `minimumReleaseAgeStrict: true`, `trustPolicy: no-downgrade`, `blockExoticSubdeps: true`, `strictDepBuilds: true`, `allowBuilds` (esbuild, vue-demi), `engineStrict: true`; `frontend/.npmrc` `engine-strict=true`. The one exclusion (`@bcc-code/*`) has its reason in the comment above it |
| 3 | Dependabot for nuget, npm, actions; grouped, weekly, cooldown = release age | MUST | ✅ | `.github/dependabot.yml` — all three (plus terraform), weekly, grouped; npm `cooldown.default-days: 7` with the same `@bcc-code/*` exclusion, cross-referenced in a comment |
| 4 | CI guard against npm lockfiles | SHOULD | ✅ | `.github/workflows/dependabot-guard.yml` fails a Dependabot PR that adds `package-lock.json` |
| 5 | Monthly dependency checklist workflow | MAY | N/A | Optional; not present |
| 6 | Actions pinned to full SHA with version comment | MUST | ✅ | Every `uses:` of a third-party action in `.github/` is a 40-char SHA with `# vX` (grep for non-SHA `uses:` returns only local `./` actions and workflows) |
| 7 | Reusable workflows and Terraform modules pinned | MUST | ✅ | `supply-chain-security.yml:31` pinned to SHA (`# v1.0.1`); `infra/gateway_backend.tf:20,31` `?ref=<sha>` |
| 8 | Container and emulator images pinned by digest | MUST | ❌ | `api/AppHost/Program.cs:10` `AddPostgres(...).WithImageTag("18")`; `AppFixture.cs:32` (both test projects) `postgres:18`; the runtime base image is the SDK default (no `ContainerBaseImage` in any project file). `deploy.yml:130` says "chiseled base image is set in the project file", which is not true. Only the gitleaks image is digest-pinned (`pr.yml:208`) |
| 9 | gitleaks on every PR; GitHub secret scanning + push protection | MUST | ✅ | `pr.yml:198-208` runs on every PR (no path filter) and feeds the required `ci` job (`:213`). Push protection is a repo setting, listed below |
| 10 | SAST on every PR (CodeQL C# + JS/TS), high severity blocks | MUST | ✅ | `.github/workflows/codeql.yml` (PR, push, weekly; both languages). Blocking depends on the ruleset, listed below |
| 11 | CycloneDX SBOM for .NET and pnpm, attached to releases | MUST | ⚠️ | `sbom.yml` generates both; `deploy.yml:198-200,244,278-280` attaches them to the production GitHub release. `sbom.yml:36` has a TODO: the .NET tool was not verified with the `.slnx` solution |
| 12 | BCC supply-chain scan on workflow/manifest/lockfile/Dockerfile changes | MUST | ⚠️ | `supply-chain-security.yml` uses the SHA-pinned reusable workflow, but its path filter (`:7-19`) covers npm/pnpm files, actions and Dockerfiles only, not NuGet manifests (`Directory.Packages.props`, `*.csproj`, `packages.lock.json`). `fail-on-warn: false`, with the reason |
| 13 | Container image scanning on Artifact Registry | SHOULD | ❌ | `containerscanning.googleapis.com` is not in the enabled services (`infra/main.tf:60-73`) |
| 14 | Only clearly named dev-only mock credentials committed | MUST | ✅ | `appsettings.Development.json` holds only the public client id; the mock is a public PKCE client; `infra/*.tfvars.example` state they hold placeholders; the OIDC secret comes from Secret Manager (`infra/secrets.tf`, `cloudrun_api.tf:59-62`) |

## Gaps to close

- **8**: Pin the Postgres image by digest in `api/AppHost/Program.cs` (`WithImageSHA256(...)` or tag plus digest) and in both
  `api/tests/*/Infrastructure/AppFixture.cs`; set `ContainerBaseImage` with a digest (for example a chiseled ASP.NET image) in
  the deployed API project, or correct the step name in `.github/workflows/deploy.yml:130`. Keep the pins current through
  Dependabot's `docker` ecosystem or a documented manual process.
- **11**: Run `sbom.yml` once and confirm `dotnet-cyclonedx` accepts `BccCode.Template.slnx`; remove the TODO or switch to per
  project runs.
- **12**: Add `Directory.Packages.props`, `**/*.csproj` and `**/packages.lock.json` to the path filter in
  `.github/workflows/supply-chain-security.yml`, after checking whether the reusable scan analyses NuGet at all.
- **13**: Enable `containerscanning.googleapis.com` in `infra/main.tf` (and decide who reads the findings).

## Not verifiable in the repo

- GitHub secret scanning and push protection, and the branch ruleset that makes the `ci` check required and blocks merge on
  high-severity code scanning alerts. `docs/development.md:110-112` lists them as setup steps; check the repository settings.
- Whether Dependabot security updates are enabled for the repository (repository settings).
