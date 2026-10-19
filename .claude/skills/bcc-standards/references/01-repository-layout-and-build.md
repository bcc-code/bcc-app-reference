# 01 Repository Layout and Build

How a repository is laid out, how the .NET solution is split into projects, and how compiler settings, package versions, SDK and tools are pinned. A small, predictable layout makes a repo easy to navigate for people and agents, and central build settings stop a new project from quietly turning off nullable or warnings-as-errors.

## Rules

### Solution and projects

1. **The solution MUST be a `.slnx` file with solution folders (`/Application`, `/Domain`, `/Tests`, `/Utils`, optionally `/Files`).** The XML format merges cleanly and the folders show the role of each project at a glance.
2. **Keep the number of projects small; each project MUST have a reason to exist that can be stated in one line.** The default set is: the host (`<App>.Api`, composition root only), one feature/domain project holding all slices, `Localization` (source-generated i18n keys), `ServiceDefaults`, `AppHost`, local tools (`OidcMock`, simulators, mocks) and test projects. Add a project only for a separate deployable (a worker host, a migration job) or a hard dependency boundary. See Reduce Accidental Complexity.
3. **Entities, EF Core configurations and Marten registrations MUST live with the feature that owns them, not in separate `Entities` / `Data` / `Contracts` layer projects.** A layer split spreads one feature across three projects and makes every change touch all of them. See Vertical Slices and [10 Vertical Slices and CQRS](10-vertical-slices-and-cqrs.md).
4. **The frontend MAY be included in the solution as a JavaScript SDK project (`Frontend.esproj`).**

### Central build settings

5. **`Directory.Build.props` MUST set `TargetFramework`, `Nullable=enable`, `ImplicitUsings=enable` and `TreatWarningsAsErrors=true` for every project.** Per-project settings drift: one project ends up without warnings-as-errors or with `Nullable` off. See Nullable Reference Types and Project Setup Checklist.
6. **Every exception to a central build property MUST carry a comment that says why.** Without the reason nobody dares remove the exception later. vote-rewrite comments each one (no lock file for AppHost, `RuntimeIdentifiers` only on deployed projects).
7. **Analyzer and code style severities belong in [25 Code Quality Guards](25-code-quality-guards.md).** This standard only requires that `.editorconfig` severities that matter are enforced in the build (`warning`/`error`), not left as `suggestion`.

### Package versions

8. **Packages MUST be managed centrally (`ManagePackageVersionsCentrally`) with `CentralPackageTransitivePinningEnabled`.** One version per package across the solution, and a way to lift a vulnerable transitive dependency.
9. **Transitive-only pins MUST exist only to lift a vulnerable version reported as NU1901–NU1904, and each MUST cite the advisory (GHSA/CVE id) and the chain that pulls it in.** Otherwise transitive pins turn into a second, unexplained version list.
10. **`Directory.Packages.props` SHOULD group packages in labelled `ItemGroup`s (e.g. `Label="Critter stack (one Wolverine version)"`), and any version range or held-back version MUST say why.** vote-rewrite holds `Microsoft.OpenApi` to `[2.12.0,3.0.0)` with the reason in a comment.
11. **Projects MUST restore with NuGet lock files (`RestorePackagesWithLockFile`), and CI MUST restore in locked mode (`RestoreLockedMode` when `CI=true`).** A restore in CI then gets exactly the graph that was reviewed, which is part of [17 Supply Chain and Security Scanning](17-supply-chain-and-security-scanning.md). Projects that cannot have a stable lock file (AppHost, which pulls host-OS packages) are excluded with a comment.

### SDK and tools

12. **`global.json` MUST pin the SDK version with `rollForward: latestPatch`, `allowPrerelease: false`, and select `Microsoft.Testing.Platform` as the test runner.** Everyone and CI build with the same SDK; the runner setting makes `dotnet test --project` behave the same everywhere.
13. **`.config/dotnet-tools.json` MUST pin the repo's CLI tools (`aspire.cli`, `slopwatch`, and `dotnet-ef` for EF Core repos) with `rollForward: false`.** `dotnet tool restore` then gives every machine and agent the same tool versions.

### Files and frontend tooling

14. **Screenshots and other binary images MUST be stored in Git LFS (`*.png`, `*.jpg` in `.gitattributes`).** Committed screenshots are regenerated often; without LFS every regeneration grows the clone permanently. See [20 Screenshots and Visual Documentation](20-screenshots-and-visual-documentation.md).
15. **Generated files that are committed (test data, generated clients) SHOULD be marked `linguist-generated` (and `-diff` where they are large).** GitHub then collapses them in PR diffs so reviewers see the real change.
16. **The frontend MUST use pnpm with the exact version in `package.json` `devEngines.packageManager` and `onFail: "error"`, and `.npmrc` MUST set `engine-strict=true`.** A wrong pnpm version fails at once instead of rewriting the lockfile. Keep the version in the CI setup action in sync (comment both places).

## Example

The template follows this at the repo root: `BccCode.Template.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `.config/dotnet-tools.json`, `.gitattributes`, `frontend/pnpm-workspace.yaml`. Known deviation from rule 2: host and feature code share one project per variant (`api/BccCode.Template.Api.Marten/`, `api/BccCode.Template.Api.EfCore/`); the ADR in `docs/adrs/` explains why.
