---
standard: 25 Code Quality Guards
assessed: 2026-10-19
commit: e4b2b86
---
# 25 Code Quality Guards — assessment

Compiler settings, analyzers, `dotnet format` in CI, an IL scanner with a self-test, an architecture test project, the
authorization sweep, startup assertions and the frontend and contract drift checks are all present and pass. Missing: a
backend complexity limit and coverage measurement; Slopwatch is removed by ADR 0006.

**Score: Strong** · ✅ 10 · ⚠️ 3 · ❌ 0 · N/A 1

Commands run: `dotnet test --project api/tests/BccCode.Template.ArchitectureTests` (14 passed); frontend `lint`,
`format:check`, `type-check`, `knip` (all exit 0).

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors` once in `Directory.Build.props` | MUST | ✅ | `Directory.Build.props:4-7`; no project overrides them. The only per-project exception, `ASPIRE010` in `api/AppHost/AppHost.csproj:5-6`, is commented. |
| 2 | `AnalysisLevel` latest-recommended, `EnforceCodeStyleInBuild`, `.editorconfig` severities | MUST | ✅ | `Directory.Build.props:8-9`; `.editorconfig:17-35` sets style rules to `warning`, CA1848 to `warning`, and comments each rule turned off (CA1859, CA1707 in tests, IDE0005). |
| 3 | CI runs `dotnet format --verify-no-changes` | MUST | ✅ | `.github/workflows/pr.yml:66`. |
| 4 | Convention-breaking APIs banned by a failing check | MUST | ✅ | IL scanner `api/tests/BccCode.Template.ArchitectureTests/ForbiddenApiScanner.cs` bans `UtcNow`/`Now`/`Today` (`ClockRulesTests.cs:6-10`); `LoggingRulesTests.cs:12-17` bans `ILoggerFactory` parameters everywhere. No `BannedApiAnalyzers` (the rule allows either). |
| 5 | Custom scanner has a self-test against a known violation | MUST | ✅ | `ClockRulesTests.cs:12-15` (`The_scanner_detects_a_forbidden_call`) against `ScannerFixture.cs:5`. |
| 6 | Architecture test project covering the applicable rules | MUST | ✅ | NetArchTest + Mono.Cecil (`BccCode.Template.ArchitectureTests.csproj`). Static endpoints `SliceConventionTests.cs:20-23`; no services/repositories `:25-28`; sealed record responses `:39`; clock `ClockRulesTests`; log classes registered `LoggingRulesTests.cs:19-35`; cascaded values are side effects `SliceConventionTests.cs:61-62`; serverless `LayerRulesTests.cs:30-31` (`Nothing_runs_in_the_background`). Both variants are scanned (`TemplateAssemblies`). |
| 7 | Every endpoint covered by an authorization sweep | MUST | ✅ | `SecurityTests.Every_api_endpoint_has_the_policy_its_route_requires_and_none_allows_anonymous` and `Only_the_allowlisted_routes_allow_anonymous_access` in both `Api.Marten.Tests` and `Api.EfCore.Tests` (`SecurityTests.cs:20`, `:37`). |
| 8 | Security/correctness misconfiguration fails at startup | MUST | ✅ | Anonymous allowlist `AssertTemplateRoutes()` (`api/BccCode.Template.Web/TemplateWeb.cs:57-63`, called in both `Program.cs`); `AssertAllPreGeneratedTypesExist` (`Api.Marten/Configuration/WolverineConfiguration.cs:41`, `Api.EfCore/…/WolverineConfiguration.cs:48`); OIDC settings required outside Development/Test (`Web/Auth/AuthConfiguration.cs:32`, `:82`); KMS key name required (`Web/DataProtection/DataProtectionConfiguration.cs:36`). Worker/role checks do not apply (single API host). |
| 9 | Slopwatch in CI | MUST | ⚠️ | Deviation (ADR): [ADR 0006](../adrs/0006-no-slopwatch.md). |
| 10 | Frontend checks in CI in check mode | MUST | ✅ | `pr.yml:119-123`. Warning-level lint findings do not fail; see [21 Frontend](21-frontend.md) rule 21. |
| 11 | OpenAPI document and generated client drift checked in CI | MUST | ✅ | `pr.yml:73-78` (per variant `git diff --exit-code api/openapi/openapi.json`), `pr.yml:99-106` (client regenerated and compared). |
| 12 | Pre-commit hooks with lefthook | MAY | N/A | Not adopted. |
| 13 | Complexity limited: frontend oxlint ≤ 10, backend cyclomatic rule at warning | MUST | ⚠️ | Frontend: `frontend/.oxlintrc.json` `eslint/complexity` max 10 for `src/**`. Backend: no CA1502 / `CodeMetricsConfig.txt` or other complexity analyzer. |
| 14 | Coverage measured, not gating | SHOULD / MUST NOT | ⚠️ | Nothing gates on coverage. Coverage is not measured either: no coverlet / `--coverage` in `pr.yml` or the vitest config. |

## Gaps to close

- **13**: Enable CA1502 at warning with a `CodeMetricsConfig.txt` (e.g. `CA1502: 15`) added as `AdditionalFiles` in
  `Directory.Build.props`, and set `dotnet_diagnostic.CA1502.severity = warning` in `.editorconfig`. Try it on this repo first,
  as the rule asks.
- **14** (SHOULD): Collect coverage in CI without a threshold: `dotnet test … --coverage` (Microsoft.Testing.Platform code
  coverage) in the `backend` job and `vitest run --coverage` in the `frontend` job, uploaded as artifacts.
- **9**: Deviation recorded in ADR 0006. Its compensations are manual; consider a small architecture or grep check for
  `#pragma warning disable` without a comment and for `Skip =` on tests.

## Not verifiable in the repo

- Whether `ci` is a required status check on `main`, which makes all of the above a merge gate; check branch protection.
