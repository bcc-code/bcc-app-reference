# 08 Code Generation and Contracts

This standard covers how we generate the API contract, the frontend client, Wolverine handler code, translation keys, mappers and log methods, and how CI keeps the generated output in step with the source. Generated code replaces hand-written glue that drifts silently. A committed, deterministic output makes every contract change visible in review and lets CI fail on drift.

## Rules

### API contract and client

1. **The OpenAPI document MUST be generated automatically from the API and committed.** The committed file is the contract the frontend and reviewers read; a contract change then shows up as a diff in the PR.
2. **The generated document MUST be byte-stable across machines.** Add a document transformer that sorts paths, responses and tags ordinally and clears `Servers`. Without it, endpoint discovery order differs between machines and every regeneration produces a large reordering diff that hides the real change.
3. **Operation ids and tags MUST be derived by one convention from the endpoint type, not set by hand per endpoint.** Typical rule: tag = feature namespace, operation id = endpoint class name without the `Endpoint` suffix. Stable ids keep the generated client's method names stable when files move inside a feature.
4. **The frontend API client MUST be generated with Hey API from the committed document, never hand-written, and formatted by the generator config.** Configure `openapi-ts.config.ts` to post-process with oxfmt (and oxlint where used), and exclude the output folder from eslint and knip with a comment saying why.
5. **CI MUST fail when the committed document or the committed client differs from what the current code produces.** The job builds the API, runs `git diff --exit-code` on the document, regenerates the client and runs `git diff --exit-code` on the client folder. The error message tells the developer which command to run.
6. **Contract rules that the generator cannot enforce SHOULD be tested against the committed document.** Example: every `PUT` and `DELETE` without a response body answers `204` and nothing else, and one with a body answers `200`; polymorphic collections are arrays of the base schema. A test on the JSON file is cheap and catches a wrong `[EmptyResponse]` or return type before the client is regenerated.
7. **When a generator breaks on a new major version of a dependency, the version MUST be constrained by a range with a comment naming the break.** Example: `Microsoft.OpenApi` `[2.12.0,3.0.0)` because 3.x breaks the ASP.NET Core OpenAPI source generator. A bare pin without the reason gets "fixed" by the next Dependabot PR.

### Wolverine code generation

8. **Production MUST run pre-generated Wolverine/Marten code with `TypeLoadMode.Static` and `AssertAllPreGeneratedTypesExist = true`.** Runtime compilation costs seconds of cold start on Cloud Run and can fail in production for a handler that compiled fine locally. Generated files are written by `codegen write` in the build and MAY be gitignored.
9. **CI MUST generate and compile the Wolverine code for every host on pull requests.** This is the only pre-merge check that every handler chain resolves its dependencies; a missing registration otherwise fails at startup after deploy.
10. **Startup code that touches the database, transports or external services MUST be skipped while code generation runs.** Check a guard such as `CodeGeneration.IsRunningGeneration()` (an in-house helper that returns true while any code generation runs) before applying schema, seeding data or connecting to brokers, so OpenAPI and codegen builds work without infrastructure.

### Shared translations

11. **When the backend produces user-facing text, the frontend i18n JSON files MUST be the single source of translation keys, and the backend MUST read them through `BccCode.I18N.SourceGen`.** The generator produces typed `LanguageKeys.*` and `Language.*` for C#, so a validation message, ProblemDetails key or e-mail template that names a missing key fails to compile.
12. **Where rule 11 applies, a test MUST fail on keys that are missing in any locale and on keys that neither frontend nor backend uses.** Scan frontend `.vue`/`.ts` and backend `.cs` sources, and list keys built at run time as explicit patterns with the place that builds them. This keeps the translation files complete and free of dead entries.

### Other generators

13. **Object mapping SHOULD use a source generator (Mapperly) or plain hand-written code, never a reflection-based mapper.** Mapperly reports unmapped members at compile time; reflection mappers fail at run time and hide which properties are exposed.
14. **Log methods SHOULD be source-generated with `[LoggerMessage]`, grouped in one log class per feature.** They avoid boxing and string formatting on disabled levels, give each message a fixed template that log-based metrics can match, and make it easy to review what a feature logs (see [22 Observability and Operations](22-observability-and-operations.md)).
15. **Generated test data MUST come from a script with fixed seeds that produces identical output on every run.** A file-based `dotnet run` script is enough. Deterministic data makes test failures reproducible and keeps the committed fixtures stable in review. See [19 Testing Strategy](19-testing-strategy.md).

## Examples

### Deterministic document (rules 1, 2)
A document transformer clears `Servers` and sorts every dictionary in the document by ordinal key order. Variants of this transformer can also fix enum components and name discriminated variants.

```csharp
options.AddDocumentTransformer((document, _, _) =>
{
    document.Servers?.Clear();
    SortForDeterministicOutput(document);
    return Task.CompletedTask;
});
...
private static void ReorderByKey<T>(IDictionary<string, T> dictionary)
{
    var sorted = dictionary.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToList();
    dictionary.Clear();
    foreach (var entry in sorted)
        dictionary.Add(entry.Key, entry.Value);
}
```

### Naming convention (rule 3)
One method applies tag and operation id to every Wolverine HTTP chain. The generated client takes its method names from the operation id.

```csharp
public static void Apply(HttpChain chain)
{
    var type = chain.Method.HandlerType;
    var operationId = OperationId(type);
    chain.WithTags(Tag(type));
    chain.WithName(operationId);   // Hey API derives the client method name from the operation id
}
```

### Drift check in CI (rules 4, 5)
A `contracts` job builds the API, then fails if the committed document or the committed client differs from the regenerated output.

```yaml
- run: dotnet build api/MyApp.Api
- name: The committed OpenAPI document is current
  run: git diff --exit-code -- api/MyApp.Api/MyApp.Api.json || { echo "::error::Rebuild api/MyApp.Api and commit MyApp.Api.json"; exit 1; }
- run: pnpm --dir frontend openapi-ts
- name: The generated client is current
  run: git diff --exit-code -- frontend/src/client || { echo "::error::Run pnpm --dir frontend openapi-ts and commit src/client"; exit 1; }
```

### Tests on the committed document (rule 6)
Read the committed JSON and assert the contract rule over every matching operation.

```csharp
var operations =
    from path in document.RootElement.GetProperty("paths").EnumerateObject()
    from operation in path.Value.EnumerateObject()
    where operation.Name is "put" or "delete"
    ...
operations.Should().AllSatisfy(o => o.Successes.Should().Equal(["204"], o.Name));
```

### Static Wolverine code (rules 8, 10)
The generation guard returns before any infrastructure is configured; production then asserts and loads pre-generated types.

```csharp
builder.Services.CritterStackDefaults(critterStack =>
{
    if (CodeGeneration.IsRunningGeneration())
        return;
    ...
    critterStack.Production.AssertAllPreGeneratedTypesExist = true;
    critterStack.Production.GeneratedCodeMode = TypeLoadMode.Static;
    critterStack.Production.SourceCodeWritingEnabled = false;
```

### Version range with reason (rule 7)
```xml
<!-- Pinned to 2.x: Microsoft.AspNetCore.OpenApi 10.0.x is built against Microsoft.OpenApi 2.x.
     3.x makes IOpenApiMediaType.Example read-only, breaking the AspNetCore OpenAPI source generator. -->
<PackageVersion Include="Microsoft.OpenApi" Version="[2.12.0,3.0.0)" />
```

### i18n, mappers, log methods and test data (rules 11 to 15)
- When rule 11 applies, the i18n source generator lives in a `MyApp.Localization` project that reads the frontend JSON files. The completeness test (rule 12) is a frontend test, for example `frontend/src/i18n/i18n.test.ts`, that scans the `.vue`, `.ts` and `.cs` sources.
- Mapperly mappers sit in the feature folders next to the endpoints that use them.
- `[LoggerMessage]` methods live in one log class per feature (for example `OrderLog`).
- A file-based script (for example `test-data/generate.cs`) generates fixtures with a fixed seed per data set, using Bogus with its own seeded randomizer.

## Related
- [05 CI Pipeline](05-ci-pipeline.md) — where the drift and codegen jobs run
- [09 Endpoints and API Design](09-endpoints-and-api-design.md) — endpoint conventions the generated contract reflects
- [19 Testing Strategy](19-testing-strategy.md) — deterministic test data
- [21 Frontend](21-frontend.md) — consumption of the generated client and translations
- [22 Observability and Operations](22-observability-and-operations.md) — logging conventions
- [25 Code Quality Guards](25-code-quality-guards.md) — tests that enforce these conventions
