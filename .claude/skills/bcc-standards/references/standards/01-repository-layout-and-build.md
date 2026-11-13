# 01 Repository Layout and Build

This standard covers how a repository is laid out, how the .NET solution is split into projects, and how compiler settings, package versions, the SDK and CLI tools are pinned. A small, predictable layout makes a repository easy to navigate for people and agents.

Central build settings stop a new project from quietly turning off nullable reference types or warnings-as-errors. Pinned versions mean every machine and CI run builds the same thing.

## Rules

### Solution and projects

1. **The solution MUST be a `.slnx` file with solution folders (`/Application`, `/Domain`, `/Tests`, `/Utils`, optionally `/Files`).** The XML format merges cleanly and the folders show the role of each project at a glance.
2. **Keep the number of projects small; each project MUST have a reason to exist that can be stated in one line.** The default set is: the host (`MyApp.Api`, composition root only), one feature/domain project holding all slices, `ServiceDefaults`, `AppHost` and test projects, plus `Localization` (source-generated i18n keys) and local tools (`OidcMock`, simulators, mocks) when the app needs them. Add a project only for a separate deployable (a worker host, a migration job) or a hard dependency boundary. Every extra project adds build time, references to maintain and places to look. See [code design principles](../principles/code-design.md).
3. **Entities, EF Core configurations and Marten registrations MUST live with the feature that owns them, not in separate `Entities` / `Data` / `Contracts` layer projects.** A layer split spreads one feature across three projects and makes every change touch all of them. See [10 Vertical Slices and CQRS](10-vertical-slices-and-cqrs.md).
4. **The frontend MAY be included in the solution as a JavaScript SDK project (`Frontend.esproj`).** IDE builds and the solution view then include it.

### Central build settings

5. **`Directory.Build.props` MUST set `TargetFramework`, `Nullable=enable`, `ImplicitUsings=enable` and `TreatWarningsAsErrors=true` for every project.** Per-project settings drift: one project ends up without warnings-as-errors or with `Nullable` off. Setting them centrally means a new project gets them without anyone remembering.
6. **Every exception to a central build property MUST carry a comment that says why.** Without the reason nobody dares remove the exception later. Typical exceptions are no lock file for the AppHost, and `RuntimeIdentifiers` only on deployed projects.
7. **Analyzer and code style severities belong in [25 Code Quality Guards](25-code-quality-guards.md).** This standard only requires that `.editorconfig` severities that matter are enforced in the build (`warning`/`error`), not left as `suggestion`.

### Package versions

8. **Packages MUST be managed centrally (`ManagePackageVersionsCentrally`) with `CentralPackageTransitivePinningEnabled`.** One version per package across the solution, and a way to lift a vulnerable transitive dependency.
9. **Transitive-only pins MUST have a documented reason: normally a vulnerable version reported as NU1901–NU1904 (cite the advisory GHSA/CVE id), or a bug fix or version conflict (cite the issue). Each pin MUST also name the chain that pulls the package in.** Otherwise transitive pins turn into a second, unexplained version list. State this rule in a comment at the top of `Directory.Packages.props`.
10. **`Directory.Packages.props` SHOULD group packages in labelled `ItemGroup`s (e.g. `Label="Critter stack (one Wolverine version)"`), and any version range or held-back version MUST say why.** For example, a package held to `[2.12.0,3.0.0)` carries the reason in a comment.
11. **Projects MUST restore with NuGet lock files (`RestorePackagesWithLockFile`), and CI MUST restore in locked mode (`RestoreLockedMode` when `CI=true`).** A restore in CI then gets exactly the graph that was reviewed, which is part of [17 Supply Chain and Security Scanning](17-supply-chain-and-security-scanning.md). Projects that cannot have a stable lock file (AppHost, which pulls host-OS packages) are excluded with a comment.

### SDK and tools

12. **`global.json` MUST pin the SDK version with `rollForward: latestPatch`, `allowPrerelease: false`, and select `Microsoft.Testing.Platform` as the test runner.** Everyone and CI build with the same SDK; the runner setting makes `dotnet test --project` behave the same everywhere.
13. **`.config/dotnet-tools.json` MUST pin the repo's CLI tools (`aspire.cli`, and `dotnet-ef` for EF Core repos) with `rollForward: false`.** `dotnet tool restore` then gives every machine and agent the same tool versions.

### Files and frontend tooling

14. **Screenshots and other binary images in documentation and screenshot folders MUST be stored in Git LFS (for example `docs/**/*.png` in `.gitattributes`); other images SHOULD stay in normal Git unless they are large or change often.** Committed screenshots are regenerated often; without LFS every regeneration grows the clone permanently. LFS has setup and quota costs, so limit it to the paths that need it. See [20 Screenshots and Visual Documentation](20-screenshots-and-visual-documentation.md).
15. **Generated files that are committed (test data, generated clients) SHOULD be marked `linguist-generated` (and `-diff` where they are large).** GitHub then collapses them in PR diffs so reviewers see the real change.
16. **The frontend MUST use pnpm with the exact version in `package.json` `devEngines.packageManager` with `onFail: "error"` or `"download"` (and `pmOnFail` to match in `pnpm-workspace.yaml`), and no `packageManager` field; where the Node version is pinned in `engines`, `engine-strict=true` MUST be set as well.** A wrong pnpm version fails or downloads the pinned one instead of rewriting the lockfile, and pnpm writes the pinned version's integrity hashes to the lockfile and checks them. `devEngines` enforces the package manager; `engine-strict` only applies to the `engines` field. Depending on the pnpm version, the setting lives in `.npmrc` or `pnpm-workspace.yaml`; check the pnpm documentation for the version in use. Keep the version in the CI setup action in sync (comment both places).

## Examples

### Central build settings and locked restore (rules 5, 6, 11)

`Directory.Build.props` sets the shared properties, comments each exception, and turns on locked restore in CI.

```xml
<TargetFramework>net10.0</TargetFramework>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
...
<!-- The Aspire SDK adds packages for the host OS (Aspire.Dashboard.Sdk.<rid>), so a lock file for the AppHost, or for
     the E2E tests that reference it, cannot match on both Windows and the Linux runners. Neither is deployed. -->
...
<PropertyGroup Condition="'$(CI)' == 'true' or '$(ContinuousIntegrationBuild)' == 'true'">
  <!-- CI (GitHub Actions sets CI=true) restores exactly what packages.lock.json says. -->
  <RestoreLockedMode>true</RestoreLockedMode>
```

### Project split (rules 1-4)

`MyApp.slnx` groups the projects into solution folders:

- `/Application/`: AppHost, `MyApp.Api`, ServiceDefaults
- `/Domain/`: `MyApp` (all slices), `MyApp.Localization`
- `/Tests/`: unit, architecture, integration and E2E tests
- `/Utils/`: OidcMock, simulators, host helpers for local tools
- `/Files/`: build props, `global.json`, `.editorconfig`

### Transitive pin rule (rules 8, 9)

`Directory.Packages.props` states the rule once and each pin cites the advisory and the chain:

```xml
<!-- Transitive-only pins (CentralPackageTransitivePinningEnabled) need a documented reason: an advisory
     (NU1901-NU1904), a bug fix or a version conflict. Each cites the advisory or issue and the chain. -->
<!-- MyApp.Storage resolved a vulnerable Newtonsoft.Json 10.0.3 (GHSA-5crp-9r3c-p9vr, DoS).
     13.0.4 is patched, and the floor other transitives (Google.Api.Gax, BccCode.Core.Api.Client)
     already require, so it only ever upgrades, never downgrades. -->
<PackageVersion Include="Newtonsoft.Json" Version="13.0.4" />
```

### Labelled groups (rule 10)

Group packages in `ItemGroup`s with labels such as `"Critter stack (one Wolverine version)"`, `"GCP and BCC"`, `"Web"` and `"Telemetry"`. A group of transitive pins carries the chain and advisory ids in its label, for example `"Transitive pins (SomeLib depends on Microsoft.AspNetCore.Mvc 2.2.0, which brings the vulnerable 4.5.0; ...)"`.

### SDK and tools (rules 12, 13)

`global.json`:

```json
{
  "sdk": { "version": "10.0.300", "rollForward": "latestPatch", "allowPrerelease": false },
  "test": { "runner": "Microsoft.Testing.Platform" }
}
```

`.config/dotnet-tools.json` lists `aspire.cli` and, for EF Core repos, `dotnet-ef`, each with `"rollForward": false`.

### LFS and generated files (rules 14, 15)

`.gitattributes`:

```
docs/**/*.png filter=lfs diff=lfs merge=lfs -text
docs/**/*.jpg filter=lfs diff=lfs merge=lfs -text
test-data/large/*.ndjson -diff linguist-generated=true
```

### pnpm pin (rule 16)

In `frontend/package.json`:

```json
"devEngines": { "packageManager": { "name": "pnpm", "version": "12.8.1", "onFail": "download" } }
```

and, where `engines` pins Node, `engine-strict=true` in `frontend/.npmrc` (or the equivalent in `pnpm-workspace.yaml`).

## Related
- [02 Local Development](02-local-development.md) — the AppHost project and tool manifest used to run the system
- [05 CI Pipeline](05-ci-pipeline.md) — locked restore and pnpm version in CI
- [10 Vertical Slices and CQRS](10-vertical-slices-and-cqrs.md) — why features own their entities and configuration
- [17 Supply Chain and Security Scanning](17-supply-chain-and-security-scanning.md) — lock files and transitive pins
- [20 Screenshots and Visual Documentation](20-screenshots-and-visual-documentation.md) — screenshots stored in LFS
- [25 Code Quality Guards](25-code-quality-guards.md) — analyzer and `.editorconfig` severities
- [Code design principles](../principles/code-design.md) — keeping accidental complexity low
