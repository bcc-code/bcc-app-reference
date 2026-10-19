# 11 Domain Modelling and Errors

How we express business rules in code: domain names, pure decision functions, time, expected failures and types for values with rules. Pure functions with explicit inputs can be unit-tested without mocks or a database, and code that reads the clock or throws for expected cases produces tests that fail at midnight and errors the user cannot act on.

## Rules

### Language and structure

1. **Code MUST use the domain's own terms for types, events, endpoints and routes.** Use the words the stakeholders use (`DataSubject`, `Poll`, `Ballot`, `Rehearsal`). See Domain-Driven Design.
2. **A Norwegian domain term without a good English equivalent MAY be used in code and comments, and MUST be explained once where the concept is defined.** Put the explanation on the main type or event, or in a short glossary in the feature's reference docs, and use the term consistently after that. orginn uses «kontingentkrav» and «frafall» this way; inventing an English word that nobody in the business uses makes the code harder to discuss with them.
3. **Business decisions MUST live in pure static functions that take everything they need, including the current time and helpers, as parameters.** Examples: bcc-privacy `FamilyConsentBusinessLogic.Evaluate(..., DateTimeOffset now, Func<string, string> urlBuilder, ...)`, vote-rewrite's `Validate`/`Post` methods over the loaded aggregate. They are unit-tested by calling them with values. See Functional Core, Imperative Shell.
4. **Style-dependent: event-sourced aggregates MUST hold state and `Apply` methods only; relational entities SHOULD keep logic out of EF entity classes.**
   - Event sourced (Marten): the aggregate rebuilds state from events; decisions are in the slices (vote-rewrite `Polls/Poll.cs`).
   - Relational (EF Core): entities are persistence shapes; non-trivial rules go into named pure functions in the feature folder (orginn's `SuggestedRequestSelector`, `InvoiceFamilies`), not into endpoint bodies or entity methods that need a `DbContext`.
5. **Events MUST be named in the past tense for what happened (`PollPublished`, `ConsentWithdrawn`, `MembershipFeeClaimWaived`), as sealed records with `required` or positional members.** An event is a fact; a command name (`PublishPoll`) on an event confuses intent and outcome.
6. **Concepts with more than one field or with their own rules SHOULD be value records, not loose primitives.** Examples: vote-rewrite `PollDefaults`, `LocalizedText`, `DbNow`; orginn `Address`. A value record keeps defaults and rules together and gives Wolverine a distinct type it will not bind from the query string.

### Time

7. **Domain code MUST get the current time from `TimeProvider`, or from a database clock where correctness depends on database time.** Inject `TimeProvider` (tests use `FakeTimeProvider`) and pass `now` into pure functions. Use a DB clock (vote-rewrite `IDbClock` reading `clock_timestamp()`) when ordering against locks or other transactions matters. Replace custom `IDateTimeProvider` interfaces with `TimeProvider` when touched.
8. **`DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now/UtcNow`, `DateTime.Today` and `DateOnly.Today` MUST be banned in domain code by an architecture test.** Direct clock reads make tests depend on the time of day; bcc-privacy has a consent-age test that fails between midnight and 02:00 CEST for this reason. vote-rewrite scans IL for these calls (`ClockRulesTests`); a `BannedSymbols.txt` analyzer works too (see [25 Code Quality Guards](25-code-quality-guards.md)).

### Expected failures

9. **Expected outcomes MUST be values, not exceptions.** A rule that rejects the request returns `ProblemDetails` from `Validate`; a decision with several outcomes returns an enum or a small record (`FamilyConsentAction.None/Throttled/Send`); a missing optional value is `null`. Exceptions are for bugs, broken configuration and unavailable dependencies, and are mapped centrally ([09 Endpoints and API Design](09-endpoints-and-api-design.md)). See Don't Use Exceptions as Flow Control.
10. **The Wolverine `LoadAsync` → `Validate` → `Handle` chain is the railway for endpoints and handlers; a general `Result<T>` library is not required.** Each stage short-circuits by returning a ProblemDetails or 404. Inside pure functions, return enums or records as in rule 9. Do not add a `Result<T>` type on top of Wolverine's stages. See Railroad Programming.

### Parse, don't validate

11. **Identifiers and values with rules SHOULD be parsed into strong types at the boundary, so that code inside the slice cannot receive an invalid value.** Start with:
    - **Ids**: `readonly record struct PollId(Guid Value)` or similar for the main aggregates, so a person id cannot be passed where a process id is expected.
    - **Dates and versions**: a version type instead of a `DateOnly` (bcc-privacy uses calendar dates as process and notice versions, which allows only one version per day); `DateOnly` vs `DateTimeOffset` chosen deliberately for each field.

    Hand-written record structs or a source generator are both fine; add JSON and Marten/EF converters in one place. Validation stays for rules that need loaded data. See Parse, Don't Validate.

## Example

`Features/Notes/` in each API variant: pure decision functions that take `now` as a parameter, value types for title and body parsed at the boundary, `TimeProvider` for time. The architecture tests ban direct clock reads.
