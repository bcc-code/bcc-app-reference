---
standard: 01 Repository Layout and Build
assessed: 2026-10-19
commit: e4b2b86
---
# 01 Repository Layout and Build — assessment

The root build files follow the standard closely: `.slnx` with folders, central build settings with commented exceptions,
central package management with a cited transitive pin, lock files restored in locked mode in CI, and a pinned SDK and tool
manifest. The gaps are small: the Marten projection registration sits outside the feature, the generated TypeScript client is
not marked as generated, and two deviations are covered by ADRs.

**Score: Strong** · ✅ 10 · ⚠️ 5 · ❌ 0 · N/A 1

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | `.slnx` with solution folders | MUST | ✅ | `BccCode.Template.slnx:2-23`: `/Application/`, `/Tests/`, `/Utils/` (OidcMock), `/Files/`. No `/Domain/` folder because there is no domain project (see rule 2). |
| 2 | Few projects, each with a one-line reason | MUST | ⚠️ | Deviation (ADR): host and features share one project per variant, [ADR 0002](../adrs/0002-one-project-per-sample-variant.md). Other projects each have a clear role (`BccCode.Template.Web` shared HTTP plumbing, `ServiceDefaults`, `AppHost`, `OidcMock`, four test projects). No `Localization` project; the frontend holds its own i18n. |
| 3 | Entities, EF configs, Marten registrations live with the feature | MUST | ⚠️ | EF Core: `Features/Notes/Note.cs` and `Features/Notes/NoteConfiguration.cs` sit in the feature; `TemplateDbContext.cs:19` picks them up with `ApplyConfigurationsFromAssembly`. Marten: the `Note` snapshot projection is registered centrally in `api/BccCode.Template.Api.Marten/Configuration/TemplateStore.cs:21`, not in `Features/Notes/`. |
| 4 | Frontend MAY be in the solution as `.esproj` | MAY | N/A | Not adopted; no `frontend/*.esproj`. Optional. |
| 5 | `Directory.Build.props` sets TFM, Nullable, ImplicitUsings, TreatWarningsAsErrors | MUST | ✅ | `Directory.Build.props:3-7` (`net10.0`, `enable`, `enable`, `TreatWarningsAsErrors=true`). No project overrides them (all `*.csproj` read). |
| 6 | Every exception to a central property has a why-comment | MUST | ✅ | AppHost lock-file opt-out `Directory.Build.props:12-16`; `RuntimeIdentifiers` on deployed projects `:17-21`; `RestoreLockedMode` `:22-25`; `NoWarn ASPIRE010` commented in `api/AppHost/AppHost.csproj:5-6`; `#pragma` for `ASPIREPROXYENDPOINTS001` commented in `api/AppHost/Program.cs:11`. |
| 7 | `.editorconfig` severities that matter enforced in build | MUST | ⚠️ | `AnalysisLevel=latest-recommended` and `EnforceCodeStyleInBuild` (`Directory.Build.props:8-9`); style rules set to `warning` (`.editorconfig:18-21`), every lowered severity has a comment (`:25-35`). `IDE0005` (unused usings) stays `suggestion` (`.editorconfig:26`) because `GenerateDocumentationFile` is off; it could be enforced by turning that on. |
| 8 | Central package management with transitive pinning | MUST | ✅ | `Directory.Packages.props:3-4`. No `Version=` on any `PackageReference` in the `*.csproj` files. |
| 9 | Transitive pins only for NU1901–1904, citing advisory and chain | MUST | ✅ | Only one pin: `MessagePack 2.5.303` with GHSA-qhrr-8q5h-9q3h and the chain (Aspire.Hosting, StreamJsonRpc) in the `ItemGroup` label, `Directory.Packages.props:8-10`. |
| 10 | Labelled `ItemGroup`s; ranges and held-back versions say why | SHOULD/MUST | ✅ | Labelled groups throughout `Directory.Packages.props`; `Microsoft.OpenApi [2.12.0,3.0.0)` with reason at `:35-37`. |
| 11 | Lock files; locked restore in CI | MUST | ✅ | `RestorePackagesWithLockFile` (`Directory.Build.props:10`); `packages.lock.json` committed for all nine non-AppHost projects (`find` over `api/`); `RestoreLockedMode` when `CI=true` (`:22-25`), and workflows set `CI: 'true'` (`pr.yml:21`, `deploy.yml:26`). |
| 12 | `global.json` pins SDK, latestPatch, no prerelease, MTP runner | MUST | ✅ | `global.json:2-3`. |
| 13 | Tool manifest pins `aspire.cli`, `slopwatch`, `dotnet-ef`, rollForward false | MUST | ⚠️ | `.config/dotnet-tools.json` pins `dotnet-ef 10.0.12` and `aspire.cli 13.6.0` with `rollForward: false`. Slopwatch is absent: Deviation (ADR), [ADR 0006](../adrs/0006-no-slopwatch.md). |
| 14 | Images in Git LFS | MUST | ✅ | `.gitattributes:2-3` (`*.png`, `*.jpg` with `filter=lfs`). No images are committed yet (`git ls-files`). |
| 15 | Committed generated files marked `linguist-generated` | SHOULD | ⚠️ | `api/openapi/openapi.json` and `**/packages.lock.json` are marked (`.gitattributes:5-6`). The generated client in `frontend/src/client/**/*.gen.ts` (committed, output of `frontend/openapi-ts.config.ts:7`) is not. |
| 16 | pnpm pinned via `devEngines` with `onFail: error`; `engine-strict=true` | MUST | ✅ | `frontend/package.json:55-61` (pnpm `12.11.2`, `onFail: "error"`); `frontend/.npmrc` `engine-strict=true`; CI pins the same version with a sync comment (`.github/actions/setup/action.yml:18`). |

## Gaps to close

- **3**: Move the Marten `Note` registration next to the feature, for example a `NoteStore.Configure(StoreOptions)` (or
  `IConfigureMarten`) in `api/BccCode.Template.Api.Marten/Features/Notes/`, called from `Configuration/TemplateStore.cs`.
- **7**: Decide whether `IDE0005` should fail the build; if yes, set `GenerateDocumentationFile=true` (with `NoWarn` for
  CS1591) in `Directory.Build.props` and raise `IDE0005` to `warning` in `.editorconfig`.
- **15**: Add `frontend/src/client/** linguist-generated=true` to `.gitattributes`. Consider `frontend/pnpm-lock.yaml
  linguist-generated=true -diff` as well.

## Not verifiable in the repo

- Rule 11: that Dependabot NuGet PRs keep the lock files consistent; check that Dependabot PRs pass the locked restore in CI.
- Rule 14: that Git LFS is enabled for the GitHub repository (storage quota) once screenshots are committed.
