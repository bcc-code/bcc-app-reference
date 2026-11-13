# 09 Endpoints and API Design

This standard covers how we write HTTP endpoints: one Wolverine.HTTP class per operation, task-oriented routes with one naming style, a fixed handler shape, per-endpoint DTOs and one error format. A consistent shape makes endpoints easy to review and test, keeps persistence types out of the contract, and lets the frontend handle every error the same way.

## Rules

### Shape

1. **Endpoints MUST be Wolverine.HTTP endpoints in a static class per operation, in the file of their slice.** One operation per file, named `<Feature>/<Action><Thing>.cs` and containing `<Action><Thing>Endpoint` (`PublishProjectEndpoint` in `Projects/PublishProject.cs`). No MVC controllers. See [10 Vertical Slices and CQRS](10-vertical-slices-and-cqrs.md) for the folder layout.
2. **Endpoints MUST follow the compound handler shape: `LoadAsync` → `Permission` → `Validate` → `Handle`/`Post` → optional `After`.**
   - `LoadAsync` does the IO: loads the data the decision needs, such as the caller's roles. A missing aggregate or document answers 404 through `[WriteAggregate]`/`[Entity]`, which are required by default, so later steps take non-null parameters.
   - `Permission` is pure and answers who: 403 (no body) for a caller who may not do this, so it returns `Forbidden?`. Where the resource's existence is itself personal data it answers 404 instead and returns `NotFound?` (standard 15.8). A Wolverine policy calls it by name between `LoadAsync` and `Validate`; a convention test requires it on every endpoint except an allowlist of endpoints open to any signed-in person.
   - `Validate` is pure: it takes the loaded data and the request and returns a nullable typed result, `null` to let the endpoint run. The return type names the responses: `DomainError?` for a broken business rule, `Results<NotFound, DomainError>?` only when one step can answer in more than one way. Not `ProblemDetails` with `WolverineContinue.NoProblems`, which hides the status codes from the signature.
   - `Handle`/`Post` is pure: it returns the response plus side effects as values (events, `IMartenOp`, cascading messages, audit records). It does not call buses or `SaveChanges`.
   - `After` is optional and runs work that must happen after the decision, such as writing the audit log from the audit record that `Handle`/`Post` returned, or post-commit scheduling.

   `Validate` holds business rules only (the state of the resource, the request), never who the caller is. Pure `Permission`, `Validate` and `Handle` methods are unit-tested by calling them directly, without mocks.
3. **Mark parameters that come from `LoadAsync` with `[NotBody]`, and endpoints without a response body with `[EmptyResponse]`.** Without them Wolverine may try to bind loaded types from the request, and the OpenAPI document declares a body that is never sent.

### Routes

4. **Routes MUST describe tasks, not just resources: `POST .../{id}/publish`, `.../waive`, `.../approve`.** A task route carries the business intent, gets its own validation and permission check, and produces a named event. Generic `PUT` of a whole resource is for editing data that has no workflow. See [Task-Oriented APIs](../principles/architecture-and-domain.md#task-oriented-apis).
5. **Routes MUST use one style: a leading slash, `/api/` prefix, kebab-case segments, plural resource nouns, and ids as route parameters.** Example: `/api/admin/projects/{projectId}/tasks/{taskId}/open`. Mixed styles (a missing leading slash, camelCase segments, verbs used as resource names) make routes hard to grep and inconsistent in the generated client.
6. **Public ids in routes and payloads MUST be GUIDs (UUIDv7 for new data) or stable slugs, never sequential integers.** Sequential ids reveal volume and invite enumeration. See [GUIDs as IDs](../principles/security.md#guids-as-ids).

### Contract

7. **Each endpoint MUST have its own request and response types as `sealed record`s, and MUST NOT bind the request body to, or return, persistence entities, Marten documents or EF types.** Returning a document exposes every stored field and couples the API to the storage schema. Loading one with `[Entity]` or `[WriteAggregate]` to decide and map from is fine. Map with Mapperly or by hand. See [DTOs over Schema Exposure](../principles/security.md#dtos-over-schema-exposure).
8. **`PUT` and `DELETE` without a response body MUST answer `204 No Content`; with a response body they answer `200`.** Test this on the committed OpenAPI document (see [08 Code Generation and Contracts](08-code-generation-and-contracts.md)).
9. **Request validation SHOULD use FluentValidation with Wolverine's ProblemDetails middleware, with the validator nested in the endpoint class.** Validation messages MUST be i18n keys, taken from the generated `LanguageKeys` so they are compile-checked. Rules that need loaded data go in `Validate`.

### Errors

10. **Every error response with a body MUST be RFC 9457 ProblemDetails, sent as `application/problem+json`.** No `BadRequest<string>`, no custom JSON error bodies. One format lets the frontend show any error with one function and keeps the generated client's error types honest. A 404 (`TypedResults.NotFound()`) and a 403 (`Forbidden`) have no body: the status says all there is, and a body could show the resource's data to someone who may not see it. Return a broken business rule as a `DomainError`, an `IResult` that writes the ProblemDetails and declares it in the OpenAPI document; `TypedResults.BadRequest(problem)` writes `application/json` instead. An error that carries data (the existing id, the fields to fix) is its own record, whose properties are the extension members, so the OpenAPI document and the generated client type them: `return new ApplicationExists(existingId);`, not an untyped `Extensions["applicationId"]`.
11. **The ProblemDetails `type` MUST be a stable URI, and the i18n key MUST go in an extension member named `key`.** `title` is a short English summary for logs and tools; `detail` is optional and MUST NOT contain personal data or exception text. The frontend translates `key` (or the keys in `errors` for validation problems). This gives key-based frontend mapping while staying standards-compliant. Target shape:

    ```json
    {
      "type": "urn:myapp:errors.projectNotDraft",
      "title": "Project is not a draft",
      "status": 409,
      "key": "errors.projectNotDraft"
    }
    ```

    Derive `type` from the key in `DomainError` so the two cannot drift.
12. **Status codes MUST follow these meanings:**
    - `400` malformed input; `422` well-formed input that breaks a business rule, where the app distinguishes the two.
    - `401`/`403` for `/api` routes, never a redirect to the login page (see [14 Authentication and Sessions](14-authentication-and-sessions.md)).
    - `403` for an existing resource the caller may not read or change; `404` for a resource that does not exist, or whose existence is personal data (see [15 Authorization and Permission Checks](15-authorization-and-permission-checks.md)).
    - `409` for state conflicts and optimistic concurrency failures (map the framework's concurrency exception centrally).
    - `429` with a `Retry-After` header and a ProblemDetails body when a rate limit rejects.
    - `503` with ProblemDetails when a required dependency is down.
13. **Exceptions MUST be reserved for unexpected conditions and mapped to ProblemDetails in one place.** Expected outcomes come from `Validate` as values ([11 Domain Modelling and Errors](11-domain-modelling-and-errors.md)). The central handler logs the exception and returns `500` without the exception message; it MUST NOT turn `ArgumentException` text into a 400 body.

## Examples

### Endpoint shape and task route (rules 1 to 4)
```csharp
public static class PublishProjectEndpoint
{
    public static Task<PublishRequest> LoadAsync(string projectId, IDocumentSession session, IDbClock clock, CancellationToken ct)
    {
        return PublishRequest.LoadAsync(projectId, session, clock, ct);
    }

    public static DomainError? Validate(Project project)
    {
        return project switch
        {
            { Status: not ProjectStatus.Draft } => new DomainError(LanguageKeys.errors.projectNotDraft, StatusCodes.Status409Conflict),
            { Tasks.Count: 0 } => new DomainError(LanguageKeys.errors.publishNeedsTask),
            { Rule: NoneRule } => new DomainError(LanguageKeys.errors.publishNeedsAssignment),
            _ => null,
        };
    }

    [WolverinePost("/api/admin/projects/{projectId}/publish"), Authorize(Policy = Policies.ProjectAdmin)]
    public static (Events, IMartenOp, RoutedToEndpointMessage<RefreshAssignments>) Post(
        [WriteAggregate("projectId")] Project project, [NotBody] PublishRequest request, ClaimsPrincipal user)
    ...
```

### DomainError (rules 10, 11)
`DomainError` produces every business-rule error in the rule 11 shape: `Type` is a URI derived from the key, `Title` is a short English summary and the key goes in `Extensions["key"]`. It writes `application/problem+json` and declares the response for the OpenAPI document. The status defaults to the app's business-rule code (400 or 422, rule 12); a state conflict passes 409.

```csharp
public sealed class DomainError(string languageKey, int status = StatusCodes.Status400BadRequest)
    : IResult, IStatusCodeHttpResult, IEndpointMetadataProvider
{
    public int? StatusCode
    {
        get { return status; }
    }

    public Dictionary<string, object?> Extensions { get; } = [];

    public Task ExecuteAsync(HttpContext httpContext)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Type = $"urn:myapp:{languageKey}",
            Title = ReasonPhrases.GetReasonPhrase(status),
            Extensions = { ["key"] = languageKey },
        };
        foreach (var (name, value) in Extensions)
        {
            problem.Extensions[name] = value;
        }

        return TypedResults.Problem(problem).ExecuteAsync(httpContext);
    }

    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status400BadRequest, typeof(ProblemDetails), ["application/problem+json"]));
    }
}
```

`PopulateMetadata` is static, so it declares one status; an endpoint that can also answer 409 declares that with `ProducesResponseType`.

### Errors that carry data (rule 10)
A record per error, derived from a base per status that writes the record itself as the problem body and declares the record as the response schema. `type` comes from the record name.

```csharp
public sealed record ApplicationExists(Guid ApplicationId)
    : ConflictProblem<ApplicationExists>("There is already an application for this season.");

public static Results<DomainError, ApplicationExists>? Validate([NotBody] StartApplicationChecks checks)
{
    if (!checks.SeasonExists) return new DomainError(LanguageKeys.errors.unknownSeason);
    if (checks.ExistingApplicationId is { } existing) return new ApplicationExists(existing);
    return null;
}
```

The body is `{ "type": "urn:myapp:application-exists", "title": ..., "status": 409, "applicationId": ... }`. Keep `DomainError` for errors that are only a message; a record for each of those adds types without adding information.

The OpenAPI exporter keeps one body type per status code, so an endpoint that can answer 400 with both a `DomainError` and a typed record (or 409 with the concurrency ProblemDetails and a record) needs an operation transformer that writes the body as `oneOf` their component schemas. The generated client then has a union, such as `400: ProblemDetails | ReferenceIncomplete`.

The frontend reads the problem in one function (for example `problemKeys` in `src/lib/problems.ts`): validation `errors` first, then `key`, then a fallback by status.

### Validation, concurrency and rate limits (rules 9, 12)
Register `UseFluentValidationProblemDetailMiddleware()` for validation, map the persistence library's concurrency failure to 409 in one place (for Marten, `MapMartenConcurrencyFailuresToConflict()`), and configure the rate limiter to reject with 429, a `Retry-After` header and a ProblemDetails body.

### Task routes in a relational app (rules 2, 4)
Task routes such as `waive`, `approve` and `undo-accept` follow the same shape with EF Core: `LoadAsync` loads the entity, `Validate` checks the state, the handler returns an audit record as a value, and `After` writes the audit log.

### 403 for resources the caller may not access (rule 12)
`[WriteAggregate]`/`[Entity]` answer 404 when the resource does not exist; `Permission` answers 403 when it exists and the caller may not use it. `Permission` answers 404 only for a resource whose existence is personal data, such as an application looked up by the applicant's person id.

## Related
- [Task-Oriented APIs](../principles/architecture-and-domain.md#task-oriented-apis) — why routes carry intent
- [Don't Use Exceptions as Flow Control](../principles/architecture-and-domain.md#dont-use-exceptions-as-flow-control) — errors as values
- [08 Code Generation and Contracts](08-code-generation-and-contracts.md) — generated contract and client
- [10 Vertical Slices and CQRS](10-vertical-slices-and-cqrs.md) — folder layout of slices
- [11 Domain Modelling and Errors](11-domain-modelling-and-errors.md) — expected failures as values
- [14 Authentication and Sessions](14-authentication-and-sessions.md) — 401 behavior
- [15 Authorization and Permission Checks](15-authorization-and-permission-checks.md) — 403 versus 404
- [16 HTTP Security Hardening](16-http-security-hardening.md) — rate limiting and headers
