# 08 Code Generation and Contracts

How we generate the API contract, the frontend client, Wolverine handler code, translation keys, mappers and log methods, and how CI keeps the generated output in step with the source. Generated code replaces hand-written glue that drifts silently; a committed, deterministic output makes every contract change visible in review and lets CI fail on drift.

## Rules

### API contract and client

1. **The OpenAPI document MUST be generated from the API at build time and committed.** Use `OpenApiGenerateDocumentsOnBuild` in the API project. The committed file is the contract the frontend and reviewers read; a contract change then shows up as a diff in the PR.
2. **The generated document MUST be byte-stable across machines.** Add a document transformer that sorts paths, responses and tags ordinally and clears `Servers`. Without it, endpoint discovery order differs between machines and every regeneration produces a large reordering diff that hides the real change.
3. **Operation ids and tags MUST be derived by one convention from the endpoint type, not set by hand per endpoint.** Typical rule: tag = feature namespace, operation id = endpoint class name without the `Endpoint` suffix. Stable ids keep the generated client's method names stable when files move inside a feature.
4. **The frontend API client MUST be generated with Hey API from the committed document, never hand-written, and formatted by the generator config.** Configure `openapi-ts.config.ts` to post-process with oxfmt (and oxlint where used), and exclude the output folder from eslint and knip with a comment saying why.
5. **CI MUST fail when the committed document or the committed client differs from what the current code produces.** The job builds the API, runs `git diff --exit-code` on the document, regenerates the client and runs `git diff --exit-code` on the client folder. The error message tells the developer which command to run.
6. **Contract rules that the generator cannot enforce SHOULD be tested against the committed document.** Example: every `PUT` and `DELETE` answers `204` and nothing else; polymorphic collections are arrays of the base schema. A test on the JSON file is cheap and catches a wrong `[EmptyResponse]` or return type before the client is regenerated.
7. **When a generator breaks on a new major version of a dependency, the version MUST be constrained by a range with a comment naming the break.** Example: `Microsoft.OpenApi` `[2.12.0,3.0.0)` because 3.x breaks the ASP.NET Core OpenAPI source generator. A bare pin without the reason gets "fixed" by the next Dependabot PR.

### Wolverine code generation

8. **Production MUST run pre-generated Wolverine/Marten code with `TypeLoadMode.Static` and `AssertAllPreGeneratedTypesExist = true`.** Runtime compilation costs seconds of cold start on Cloud Run and can fail in production for a handler that compiled fine locally. Generated files are written by `codegen write` in the build and MAY be gitignored.
9. **CI MUST generate and compile the Wolverine code for every host on pull requests.** This is the only pre-merge check that every handler chain resolves its dependencies; a missing registration otherwise fails at startup after deploy.
10. **Startup code that touches the database, transports or external services MUST be skipped while code generation runs.** Check `CodeGeneration.IsRunningGeneration()` (entry assembly `GetDocument.Insider` or a `codegen` argument) before applying schema, seeding data or connecting to brokers, so OpenAPI and codegen builds work without infrastructure.

### Shared translations

11. **The frontend i18n JSON files MUST be the single source of translation keys, and the backend MUST read them through `BccCode.I18N.SourceGen`.** The generator produces typed `LanguageKeys.*` and `Language.*` for C#, so a validation message, ProblemDetails key or e-mail template that names a missing key fails to compile.
12. **A test MUST fail on keys that are missing in any locale and on keys that neither frontend nor backend uses.** Scan frontend `.vue`/`.ts` and backend `.cs` sources, and list keys built at run time as explicit patterns with the place that builds them. This keeps the translation files complete and free of dead entries.

### Other generators

13. **Object mapping SHOULD use a source generator (Mapperly) or plain hand-written code, never a reflection-based mapper.** Mapperly reports unmapped members at compile time; reflection mappers fail at run time and hide which properties are exposed.
14. **Log methods SHOULD be source-generated with `[LoggerMessage]`, grouped in one log class per feature.** They avoid boxing and string formatting on disabled levels, give each message a fixed template that log-based metrics can match, and make it easy to review what a feature logs (see [22 Observability and Operations](22-observability-and-operations.md)).
15. **Generated test data MUST come from a script with fixed seeds that produces identical output on every run.** A file-based `dotnet run` script is enough. Deterministic data makes test failures reproducible and keeps the committed fixtures stable in review. See Realistic Test Data.

## Example

`api/openapi/openapi.json` (committed contract, identical for both variants), the generated client under `frontend/`, and the drift jobs in `.github/workflows/`. Run the generate commands from `AGENTS.md` after any endpoint or DTO change.
