# 11 Domain Modelling and Errors

This standard covers how we express business rules in code: domain names, pure decision functions, time, expected failures and types for values with rules. Pure functions with explicit inputs can be unit-tested without mocks or a database. Code that reads the clock or throws for expected cases produces tests that fail at midnight and errors the user cannot act on.

## Rules

### Language and structure

1. **Code MUST use the domain's own terms for types, events, endpoints and routes.** Use the words the stakeholders use (`Customer`, `Order`, `Booking`, `Rehearsal`). See [Domain-Driven Design](../principles/architecture-and-domain.md#domain-driven-design).
2. **A term in the organisation's working language without a good English equivalent MAY be used in code and comments, and MUST be explained once where the concept is defined.** Put the explanation on the main type or event, or in a short glossary in the feature's reference docs, and use the term consistently after that. A term such as «dugnad» (a shared volunteer work effort) is used this way. Inventing an English word that nobody in the business uses makes the code harder to discuss with them.
3. **Business decisions MUST live in pure static functions that take everything they need, including the current time and helpers, as parameters.** Examples: `ApprovalBusinessLogic.Evaluate(..., DateTimeOffset now, Func<string, string> urlBuilder, ...)`, or `Validate`/`Post` methods over the loaded aggregate. They are unit-tested by calling them with values. See [Functional Core, Imperative Shell](../principles/architecture-and-domain.md#functional-core-imperative-shell).
4. **Style-dependent: event-sourced aggregates MUST hold state, `Apply` methods and read-only query methods only; relational entities SHOULD keep logic out of EF entity classes.**
   - Event sourced (Marten): the aggregate rebuilds state from events; decisions are in the slices (for example `Projects/Project.cs`).
   - Relational (EF Core): entities are persistence shapes; non-trivial rules go into named pure functions in the feature folder (for example `SuggestedItemSelector`, `InvoiceGrouping`), not into endpoint bodies or entity methods that need a `DbContext`.
5. **Events MUST be named in the past tense for what happened (`ProjectPublished`, `ApprovalWithdrawn`, `InvoiceWaived`), as sealed records with `required` or positional members.** An event is a fact; a command name (`PublishProject`) on an event confuses intent and outcome.
6. **Concepts with more than one field or with their own rules SHOULD be value records, not loose primitives.** Examples: `ProjectDefaults`, `LocalizedText`, `DbNow`, `Address`. A value record keeps defaults and rules together and gives Wolverine a distinct type it will not bind from the query string.

### Time

7. **Domain code MUST get the current time from `TimeProvider`, or from a database clock where correctness depends on database time.** Inject `TimeProvider` (tests use `FakeTimeProvider`) and pass `now` into pure functions. Use a DB clock (an `IDbClock` reading `clock_timestamp()`) when ordering against locks or other transactions matters. Replace custom `IDateTimeProvider` interfaces with `TimeProvider` when touched.

### Expected failures

8. **Expected outcomes MUST be values, not exceptions.** A rule that rejects the request returns `ProblemDetails` from `Validate`; a decision with several outcomes returns an enum or a small record (`ApprovalAction.None/Throttled/Send`); a missing optional value is `null`. Exceptions are for bugs, broken configuration and unavailable dependencies, and are mapped centrally ([09 Endpoints and API Design](09-endpoints-and-api-design.md)). See [Don't Use Exceptions as Flow Control](../principles/architecture-and-domain.md#dont-use-exceptions-as-flow-control).
9. **The Wolverine `LoadAsync` → `Validate` → `Handle` chain is the railway for endpoints and handlers; a general `Result<T>` library is not required.** Each stage short-circuits by returning a ProblemDetails or 404. Inside pure functions, return enums or records as in rule 8. Do not add a `Result<T>` type on top of Wolverine's stages. See [Railroad Programming](../principles/architecture-and-domain.md#railroad-programming).

### Parse, don't validate

10. **Identifiers and values with rules SHOULD be parsed into strong types at the boundary, so that code inside the slice cannot receive an invalid value.** Start with:
    - **Ids**: `readonly record struct ProjectId(Guid Value)` or similar for the main aggregates, so a person id cannot be passed where a process id is expected.
    - **Money and contact values**: a `Money` record with an amount and a currency instead of a bare `decimal`, and an `EmailAddress` type created only by parsing, so no code handles an amount without a currency or an unvalidated address.
    - **Dates**: `DateOnly` vs `DateTimeOffset` chosen deliberately for each field.

    Hand-written record structs or a source generator are both fine; add JSON and Marten/EF converters in one place. Validation stays for rules that need loaded data. See [Parse, Don't Validate](../principles/architecture-and-domain.md#parse-dont-validate).

## Examples

### Pure decision function with time as a parameter (rules 3, 7, 8)
```csharp
public static ApprovalEvaluation Evaluate(
    Requester requester,
    IReadOnlyList<Process> givenProcesses,
    IReadOnlyCollection<ProcessApproval> recipientExistingApprovals,
    PendingApprovalRequest? existingRequest,
    DateTimeOffset now,
    Func<string, string> urlBuilder,
    string? language)
{
    var recipient = requester.Delegate;
    if (recipient == null || givenProcesses.Count == 0)
        return new ApprovalEvaluation(ApprovalAction.None);
    ...
    if (existingRequest != null && existingRequest.CreatedAt > now.AddHours(-24))
        return new ApprovalEvaluation(ApprovalAction.Throttled, url);
```

### Aggregate with state and `Apply` only (rule 4)
```csharp
/// The project stream aggregate: the project and its tasks. State, Apply methods and query methods only;
/// decisions live in the slices.
public sealed class Project
{
    ...
    public void Apply(ProjectPublished e) => Status = ProjectStatus.Published;
    public void Apply(ProjectEnded e) => Status = ProjectStatus.Ended;
    public void Apply(ProjectArchived e) => Status = ProjectStatus.Archived;
```

### DB clock (rule 7)
`IDbClock` has a Postgres implementation reading `clock_timestamp()`. `DbNow` is a distinct record so Wolverine passes it from `LoadAsync` instead of binding it from the query string.

### Value record with defaults (rule 6)
```csharp
/// <summary>The settings a new task takes unless the admin overrides them.</summary>
public sealed record ProjectDefaults(Visibility Visibility, ResultsVisibility ResultsVisibility, bool AllowChange, int? DurationSeconds)
{
    /// <summary>The defaults every project starts with.</summary>
    public static readonly ProjectDefaults Initial = new(Visibility.Private, ResultsVisibility.AfterSubmit, AllowChange: false, DurationSeconds: null);
}
```

### Domain term explained at the event (rule 2)
```csharp
/// <summary>
/// A dugnad (shared volunteer work effort) was cancelled: it will not take place. Raised once per
/// participant, without regard to their status — the name states what happened to the dugnad, and the
/// handler that owns participant state decides what it means for the participant.
/// </summary>
public sealed record DugnadCancelled(
```

## Related
- [Domain-Driven Design](../principles/architecture-and-domain.md#domain-driven-design), [Functional Core, Imperative Shell](../principles/architecture-and-domain.md#functional-core-imperative-shell), [Parse, Don't Validate](../principles/architecture-and-domain.md#parse-dont-validate), [Railroad Programming](../principles/architecture-and-domain.md#railroad-programming), [Don't Use Exceptions as Flow Control](../principles/architecture-and-domain.md#dont-use-exceptions-as-flow-control), [Event Sourcing](../principles/architecture-and-domain.md#event-sourcing) — the principles behind these rules
- [09 Endpoints and API Design](09-endpoints-and-api-design.md) — central exception mapping
- [10 Vertical Slices and CQRS](10-vertical-slices-and-cqrs.md) — where decisions live
- [12 Data Access and Migrations](12-data-access-and-migrations.md) — persistence of events and entities
- [19 Testing Strategy](19-testing-strategy.md) — testing pure functions
- [25 Code Quality Guards](25-code-quality-guards.md) — analyzers
