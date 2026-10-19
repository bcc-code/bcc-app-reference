---
standard: 08 Code Generation and Contracts
assessed: 2026-10-19
commit: e4b2b86
---
# 08 Code Generation and Contracts — assessment

The OpenAPI contract and the generated client are in good shape: the document is generated on build, sorted for stable
output, compared with the running app in a test and checked for drift in CI. The gaps are in Wolverine code generation on
pull requests and in previews, and in shared translations, where the backend uses string literals instead of
`BccCode.I18N.SourceGen`.

**Score: Adequate** · ✅ 7 · ⚠️ 4 · ❌ 3 · N/A 1

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | OpenAPI generated at build time and committed | MUST | ✅ | `api/BccCode.Template.Api.Marten/BccCode.Template.Api.Marten.csproj:6-16` and `api/BccCode.Template.Api.EfCore/BccCode.Template.Api.EfCore.csproj:6-16` set `OpenApiGenerateDocumentsOnBuild` (Debug builds only, explained in the csproj comment); committed as `api/openapi/openapi.json`. |
| 2 | Document byte-stable: ordinal sort, clear `Servers` | MUST | ✅ | `api/BccCode.Template.Web/Http/OpenApiConfiguration.cs:12-13,28-49` clears servers and sorts schemas, tags, paths and responses ordinally. Committed document has no `servers` member. |
| 3 | Operation ids and tags by one convention | MUST | ✅ | `api/BccCode.Template.Web/Http/OpenApiNaming.cs:14-31`, applied to every chain in `EndpointConventions.cs:16-20`. Tests `HttpConventionTests.The_operation_id_is_the_class_name_without_Endpoint` and `The_tag_is_the_feature_folder`. |
| 4 | Hey API client, formatted by generator; excluded from lint/knip with reason | MUST | ⚠️ | `frontend/openapi-ts.config.ts:4-14` uses Hey API with oxfmt `postProcess`. `frontend/eslint.config.ts:13` and `frontend/knip.config.ts:5` exclude `src/client/**`, but neither has a comment saying why. |
| 5 | CI fails on contract or client drift, says which command to run | MUST | ⚠️ | `.github/workflows/pr.yml:73-78` builds each variant and runs `git diff --exit-code api/openapi/openapi.json`; `pr.yml:99-106` regenerates the client and fails with "Run 'pnpm --dir frontend run generate:api'". The OpenAPI step prints no such hint (the tests `PersistenceTests.The_served_openapi_document_equals_the_committed_one` do, in both variants). |
| 6 | Contract rules tested against the committed document | SHOULD | ❌ | The only test on `openapi.json` is equality with the served document (`PersistenceTests.cs:68` Marten, `:119` EF Core). No test checks rules such as "an `[EmptyResponse]` endpoint answers 204 only". The committed document also advertises responses that cannot happen (404 on `GET /api/me`, `GET /api/notes`, `GET /api/admin/stats`), which such a test would catch. |
| 7 | Generator-breaking dependency constrained by range with reason | MUST | ✅ | `Directory.Packages.props:35-37`: `Microsoft.OpenApi` `[2.12.0,3.0.0)` with a comment naming the 3.x break. |
| 8 | Production runs static pre-generated Wolverine code, asserted | MUST | ⚠️ | Both `Configuration/WolverineConfiguration.cs` (Marten `:36-45`, EF Core `:43-54`) set `TypeLoadMode.Static` and `AssertAllPreGeneratedTypesExist` outside Development/Test; `deploy.yml:118-128` runs `codegen write` before the Release publish. `.github/workflows/preview.yml:176-185` publishes the Release image without `codegen write`, and the preview service sets no `ASPNETCORE_ENVIRONMENT` (`infra/preview/main.tf:45-67`), so a preview revision would run in Production mode with no generated types and fail the assertion at startup (from reading the code; not run). |
| 9 | CI generates and compiles Wolverine code for every host on PRs | MUST | ❌ | `pr.yml:70-72` states "No Wolverine code generation is needed for Debug builds". No PR job runs `codegen write` or a Release build; the only `codegen write` is in `deploy.yml:128`, after merge, for one project. |
| 10 | Skip DB/transport startup work while generating code | MUST | ⚠️ | `api/BccCode.Template.Web/Http/CodeGeneration.cs:8` detects only `GetDocument.Insider` (OpenAPI), used in `MartenConfiguration.cs:19`, `EfCoreConfiguration.cs:34`, both `WolverineConfiguration.cs` (transports and persistence disabled). The `codegen` argument is not detected; `deploy.yml:122-125` works around it with a dummy connection string. It works today only because no startup code touches the database. |
| 11 | Backend reads i18n keys through `BccCode.I18N.SourceGen` | MUST | ❌ | No reference to `BccCode.I18N.SourceGen` in `Directory.Packages.props` or any csproj. Keys are string literals: `Problems.cs:17-24`, `NoteRules.cs:9`, `NoteValues.cs:21-22,42` (both variants). Partly mitigated by the frontend test in rule 12, which fails on an `errors.*` key the API names but `en.json` lacks. |
| 12 | Test fails on missing-locale and unused keys | MUST | ✅ | `frontend/src/i18n/i18n.test.ts:71-77` (same keys in every locale), `:94-96` (unused keys, scanning frontend `.vue`/`.ts` and backend `.cs`), `:98-101` (keys the API names exist), `runtimeKeys` list at `:54`. |
| 13 | Mapping by Mapperly or hand-written, no reflection mapper | SHOULD | ✅ | Hand-written `NoteResponse.From` in `Features/Notes/NoteResponse.cs:6` (both variants); no mapper package in `Directory.Packages.props`. |
| 14 | Source-generated `[LoggerMessage]` per feature | SHOULD | ✅ | `Infrastructure/SeedLog.cs:7-14` in both variants. No feature logs anything else. |
| 15 | Generated test data from a fixed-seed script | MUST | N/A | The template has no generated test data. Seed data is three hand-written notes with fixed ids (`Seeding/SeedNotes.cs`). |

## Gaps to close

- **9**: Add a PR job (or a step in `backend` in `.github/workflows/pr.yml`) that, for every `api/BccCode.Template.Api*/`
  project, runs `codegen write` and then `dotnet build -c Release`, so a handler chain that cannot resolve fails before merge.
- **11**: Add `BccCode.I18N.SourceGen` to `Directory.Packages.props` and the API projects, point it at `frontend/src/i18n/*.json`,
  and replace the literals in `api/BccCode.Template.Web/Http/Problems.cs`, `Features/Notes/NoteRules.cs` and
  `Features/Notes/NoteValues.cs` (both variants) with `LanguageKeys.*`.
- **8**: Run `codegen write` before `dotnet publish` in `.github/workflows/preview.yml` (as in `deploy.yml:118-128`), and set the
  preview container's environment in `infra/preview/main.tf`.
- **10**: Extend `CodeGeneration` in `api/BccCode.Template.Web/Http/CodeGeneration.cs` with an `IsRunningGeneration()` that also
  returns true when the command line contains `codegen`, and use it where `IsGeneratingOpenApi()` guards startup work.
- **5**: In `pr.yml:73-78`, print an `::error::` naming `dotnet build <project>` and the file to commit when the diff fails.
- **4**: Add a comment at `frontend/eslint.config.ts:13` and `frontend/knip.config.ts:5` saying the folder is generated by
  `generate:api` and checked for drift in CI.
- **6**: Add a test that reads `api/openapi/openapi.json` and checks contract rules: endpoints with `[EmptyResponse]` declare
  only 204 as success, and read endpoints do not declare a 404 they cannot return.

## Not verifiable in the repo

- Whether a preview revision actually fails to start (rule 8): deploy a preview and read the Cloud Run revision logs for the
  pre-generated type assertion.
- That branch protection requires the `ci` job, so the drift checks in rule 5 block merge (GitHub repository settings).
