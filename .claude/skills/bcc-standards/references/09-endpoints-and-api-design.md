# 09 Endpoints and API Design

How we write HTTP endpoints: one Wolverine.HTTP class per operation, task-oriented routes with one naming style, a fixed handler shape, per-endpoint DTOs and one error format. A consistent shape makes endpoints easy to review and test, keeps persistence types out of the contract, and lets the frontend handle every error the same way.

## Rules

### Shape

1. **Endpoints MUST be Wolverine.HTTP endpoints in a static class per operation, in the file of their slice.** One operation per file (`PublishPollEndpoint` in `Polls/PublishPoll.cs`). No MVC controllers. See [10 Vertical Slices and CQRS](10-vertical-slices-and-cqrs.md) for the folder layout.
2. **Endpoints MUST follow the compound handler shape: `LoadAsync` → `Validate` → `Handle`/`Post` → optional `After`.**
   - `LoadAsync` does the IO: loads the aggregate, documents or entities the decision needs, and returns 404 when the resource is missing or not visible to the caller.
   - `Validate` is pure: it takes the loaded data and the request and returns a `ProblemDetails` (or `WolverineContinue.NoProblems`).
   - `Handle`/`Post` is pure: it returns the response plus side effects as values (events, `IMartenOp`, cascading messages, audit records). It does not call buses or `SaveChanges`.
   - `After` runs work that must happen after the decision, such as audit logging or post-commit scheduling.

   Pure `Validate` and `Handle` methods are unit-tested by calling them directly, without mocks.
3. **Mark parameters that come from `LoadAsync` with `[NotBody]`, and endpoints without a response body with `[EmptyResponse]`.** Without them Wolverine may try to bind loaded types from the request, and the OpenAPI document declares a body that is never sent.

### Routes

4. **Routes MUST describe tasks, not just resources: `POST .../{id}/publish`, `.../waive`, `.../approve`.** A task route carries the business intent, gets its own validation and permission check, and produces a named event. Generic `PUT` of a whole resource is for editing data that has no workflow. See Task-Oriented APIs.
5. **Routes MUST use one style: a leading slash, `/api/` prefix, kebab-case segments, plural resource nouns, and ids as route parameters.** Example: `/api/admin/polls/{pollId}/questions/{questionId}/open`. Mixed styles (`api/...` without slash, `sendTranslation`, `membershipSettings`) make routes hard to grep and inconsistent in the generated client.
6. **Public ids in routes and payloads MUST be GUIDs (UUIDv7 for new data) or stable slugs, never sequential integers.** Sequential ids reveal volume and invite enumeration. See GUIDs as IDs.

### Contract

7. **Each endpoint MUST have its own request and response types as `sealed record`s, and MUST NOT bind or return persistence entities, Marten documents or EF types.** Binding a document with `[Document]` and returning it exposes every stored field and couples the API to the storage schema. Map with Mapperly or by hand. See DTOs over Schema Exposure.
8. **`PUT` and `DELETE` without a response body MUST answer `204 No Content`.** Test this on the committed OpenAPI document (see [08 Code Generation and Contracts](08-code-generation-and-contracts.md)).
9. **Request validation SHOULD use FluentValidation with Wolverine's ProblemDetails middleware, with the validator nested in the endpoint class, and messages MUST be i18n keys.** Rules that need loaded data go in `Validate`. Keys come from the generated `LanguageKeys` so they are compile-checked.

### Errors

10. **Every error response MUST be RFC 9457 ProblemDetails.** No `BadRequest<string>`, no custom JSON error bodies. One format lets the frontend show any error with one function and keeps the generated client's error types honest.
11. **The ProblemDetails `type` MUST be a stable URI, and the i18n key MUST go in an extension member named `key`.** `title` is a short English summary for logs and tools; `detail` is optional and MUST NOT contain personal data or exception text. The frontend translates `key` (or the keys in `errors` for validation problems). This keeps vote-rewrite's key-based frontend mapping while staying standards-compliant. Target shape:

    ```json
    {
      "type": "urn:bcc:vote:errors.pollNotDraft",
      "title": "Poll is not a draft",
      "status": 409,
      "key": "errors.pollNotDraft"
    }
    ```

    Derive `type` from the key in the shared `Problems` helper so the two cannot drift.
12. **Status codes MUST follow these meanings:**
    - `400` malformed input; `422` well-formed input that breaks a business rule, where the app distinguishes the two.
    - `401`/`403` for `/api` routes, never a redirect to the login page (see [14 Authentication and Sessions](14-authentication-and-sessions.md)).
    - `404` for a resource that does not exist **or that the caller may not see** (see [15 Authorization and Permission Checks](15-authorization-and-permission-checks.md)).
    - `409` for state conflicts and optimistic concurrency failures (map the framework's concurrency exception centrally).
    - `429` with a `Retry-After` header and a ProblemDetails body when a rate limit rejects.
    - `503` with ProblemDetails when a required dependency is down.
13. **Exceptions MUST be reserved for unexpected conditions and mapped to ProblemDetails in one place.** Expected outcomes come from `Validate` as values ([11 Domain Modelling and Errors](11-domain-modelling-and-errors.md)). The central handler logs the exception and returns `500` without the exception message; it MUST NOT turn `ArgumentException` text into a 400 body.

## Example

`api/BccCode.Template.Api.Marten/Features/Notes/` and `api/BccCode.Template.Api.EfCore/Features/Notes/` (task routes such as `/api/notes/{noteId}/archive`, per-endpoint records), ProblemDetails mapping in `api/BccCode.Template.Web/`. Both samples use Wolverine.HTTP endpoints (`LoadAsync` → `Validate` → `Post`); the shape rules (one operation per file, load then decide then write, ProblemDetails for every error) still apply.
