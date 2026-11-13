# Architecture and Domain

This page collects the architectural and engineering patterns that guide how we structure backend systems: how code is organised, how business logic and IO are separated, how failures are expressed, how data is owned, and how services and APIs communicate. The sections below cover domain-driven design, vertical slices, functional core and imperative shell, pipelines, railroad programming, parsing at the boundary, exceptions, repositories, database ownership, events, event sourcing, task-oriented APIs and external APIs. The standards that make these principles concrete are [09 Endpoints and API Design](../standards/09-endpoints-and-api-design.md), [10 Vertical Slices and CQRS](../standards/10-vertical-slices-and-cqrs.md), [11 Domain Modelling and Errors](../standards/11-domain-modelling-and-errors.md) and [12 Data Access and Migrations](../standards/12-data-access-and-migrations.md).

## Domain-Driven Design

DDD is about keeping your code aligned with the business domain. Use the same language as your stakeholders in your code. If the business calls it a `SubscriptionRenewal`, the code should too.

Guidance:
- Name things using the same terms stakeholders use. If you find yourself translating between "business speak" and "code speak", that is a smell.
- Keep the domain model aligned with how the business actually works, not how the database happens to be structured.
- Isolate infrastructure (databases, APIs, queues) from domain concepts. The domain should not know or care how it is persisted.

Why it matters: misalignment between code and business concepts creates a constant translation layer. Every time requirements change, someone has to figure out which code corresponds to which business concept. Shared language removes that overhead and reduces the chance of implementing the wrong thing.

What to avoid: data-centric models that mirror tables instead of business behavior. An `Order` entity with a `StatusInt` column is harder to work with than one with methods like `Approve()` and `Cancel()`.

Example: the domain uses `SubscriptionRenewal` and `RenewalWindow` rather than generic `Status` fields.

Related: [11 Domain Modelling and Errors](../standards/11-domain-modelling-and-errors.md), [Event Sourcing](#event-sourcing), [Task-Oriented APIs](#task-oriented-apis).

## Vertical Slices

Organize code by feature, not by layer. All the code for a given feature (the API endpoint, handler, data access and domain logic) lives in the same folder. This is sometimes called "screaming architecture": the folder structure should show what the system does, not which technical pattern it uses.

Guidance:
- Group by feature, not by technical role. `Payments/` beats `Controllers/`, `Services/`, `Repositories/`.
- Keep related behavior and data together. If you are changing a feature, you should not need to navigate across multiple top-level folders.
- Shared utilities and cross-cutting concerns (auth middleware, logging and so on) can still live in shared folders. Do not put feature logic there.

Why it matters: horizontal layers (all controllers in one folder, all services in another) make it hard to find everything related to a feature, and you end up jumping between folders constantly. Vertical slices make each feature self-contained and easy to find.

Example: the `Payments` slice contains the API endpoint, handler, domain logic and data access in one folder.

Related: [10 Vertical Slices and CQRS](../standards/10-vertical-slices-and-cqrs.md), [Domain-Driven Design](#domain-driven-design).

## Functional Core, Imperative Shell

Keep business logic pure: functions that take inputs and return outputs, with no side effects. Push all IO (database calls, API requests, message publishing) to the outer shell.

Guidance:
- Pure functions in the core: no database calls, no external calls, no global state.
- IO, DB access and API calls go in the shell, which orchestrates the pure core.
- Keep adapters thin. They translate between the outside world and the domain, nothing more.

Why it matters: pure functions are easy to test. You call them with inputs and check the output, with no mocking. When side effects are isolated to the shell, you can test the business logic without standing up infrastructure, and swap out infrastructure without touching business rules.

What to avoid: mixing IO with business logic throughout the codebase. A function that validates a discount and also queries a database to look up the customer is hard to test and hard to reason about.

Example: the core computes a discount based on inputs alone. The shell loads the customer data, calls the pure discount function and writes the result.

Related: [10 Vertical Slices and CQRS](../standards/10-vertical-slices-and-cqrs.md) (handlers return side effects as values), [11 Domain Modelling and Errors](../standards/11-domain-modelling-and-errors.md), [Railroad Programming](#railroad-programming), [Don't Use Exceptions as Flow Control](#dont-use-exceptions-as-flow-control), [19 Testing Strategy](../standards/19-testing-strategy.md).

## Model workflow as a pipeline

Model a workflow as a sequence of small, pure step functions, and let the imperative shell call them in order. The steps live in the functional core; the handler orchestrates them with plain, readable control flow. You get an explicit workflow without hidden control flow from base classes and overrides, and without reaching for a hand-rolled monad.

Benefits:
- **Explicit flow:** the sequence is visible in the handler where the workflow runs.
- **Low coupling:** steps are static functions, with no base classes or virtual overrides.
- **Easy reuse:** the same step can be used in multiple workflows.
- **Better testing:** each step is small and pure, so tests are fast and isolated.
- **Safer changes:** reordering or swapping steps does not ripple through a class chain.

Example (C#). The request is parsed into typed values at the boundary (see [Parse, Don't Validate](#parse-dont-validate)). The pure steps transform data. Each returns a new value; none reach out to the world.

```csharp
public sealed record PlaceOrderRequest(Guid CustomerId, decimal Total);
public sealed record Order(Guid CustomerId, decimal Total, decimal Vat);
public sealed record OrderPlaced(Guid CustomerId, decimal Total, decimal Vat);
public sealed record PlaceOrderResponse(Guid CustomerId, decimal Total);

// Functional core: pure step functions.
public static class OrderWorkflow
{
    public static Order Price(PlaceOrderRequest request)
    {
        const decimal vatRate = 0.25m;
        return new Order(request.CustomerId, request.Total, request.Total * vatRate);
    }

    public static OrderPlaced ToEvent(Order order) =>
        new(order.CustomerId, order.Total, order.Vat);
}
```

The imperative shell is a Wolverine endpoint in the shape described in [09 Endpoints and API Design](../standards/09-endpoints-and-api-design.md). `Validate` rejects bad input by returning a `DomainError`, or `null` to continue. `Post` runs the pure steps and returns the response and the event as values; Wolverine publishes the event after the transaction commits.

```csharp
public static class PlaceOrderEndpoint
{
    public static DomainError? Validate(PlaceOrderRequest request)
    {
        return request.Total > 0
            ? null
            : new DomainError(LanguageKeys.errors.invalidOrderTotal);
    }

    [WolverinePost("/api/orders")]
    public static (PlaceOrderResponse, OrderPlaced) Post(PlaceOrderRequest request)
    {
        var order = OrderWorkflow.Price(request);
        return (new PlaceOrderResponse(order.CustomerId, order.Total), OrderWorkflow.ToEvent(order));
    }
}
```

When to prefer it: use pipeline-style composition when the workflow is a sequence of independent steps. Keep the steps pure and let the handler orchestrate them. When a workflow has many fallible steps and the guard clauses start to stack up, split it into smaller functions that return an enum or a small record (see [Railroad Programming](#railroad-programming)). In Wolverine endpoints, the `LoadAsync`, `Validate`, `Handle` chain already plays this role (see [09 Endpoints and API Design](../standards/09-endpoints-and-api-design.md)).

Summary: compose behavior as a pipeline of pure functions, orchestrated by the imperative shell. The workflow stays explicit and easy to test, you avoid brittle inheritance chains, and the code reads as ordinary C#.

Related: [Functional Core, Imperative Shell](#functional-core-imperative-shell), [Parse, Don't Validate](#parse-dont-validate), [Railroad Programming](#railroad-programming), [Don't Use Exceptions as Flow Control](#dont-use-exceptions-as-flow-control).

## Railroad Programming

Railroad programming is a functional approach to error handling: operations chain together, but the moment one fails, the rest are skipped and the failure flows through to the end. Think of a train that either stays on the success track or switches to the failure track and goes straight to the end.

Guidance:
- Make each step return either a success or a failure as a value: an enum, a small record or a typed result such as `DomainError`. A general `Result<T>` library is not required.
- Avoid nested error handling. If you are writing nested `if (!result.IsSuccess)` blocks, there is a better way.
- Chain steps so that later steps only execute on success. In Wolverine this is the `LoadAsync`, `Validate`, `Handle` chain.

Why it matters: nested try/catch blocks for normal business flows are hard to follow. Railroad-style code makes the happy path and error handling both visible and flat, so you can see the whole workflow in one place.

Example: `Validate` returns a `DomainError`, or `null` to continue. If it fails, all downstream steps are skipped and the error is returned directly. No nested error handling is needed.

In Wolverine endpoints the `LoadAsync`, `Validate`, `Handle` chain is the railway, and each stage short-circuits by returning a typed result (`DomainError`, a bare `NotFound`). Do not add a `Result<T>` type on top of it (see [11 Domain Modelling and Errors](../standards/11-domain-modelling-and-errors.md)).

Related: [Don't Use Exceptions as Flow Control](#dont-use-exceptions-as-flow-control), [Functional Core, Imperative Shell](#functional-core-imperative-shell), [Model workflow as a pipeline](#model-workflow-as-a-pipeline).

## Parse, Don't Validate

Convert inputs into strong domain types at the boundary, so that invalid states become impossible to represent further into the system.

Guidance:
- Parse inputs into domain types early, at the boundary where data enters the system.
- Do not spread validation checks across the codebase. If you are validating the same thing in multiple places, something is wrong.
- Make invalid values unconstructable. An `Email` type that can only be created by parsing a valid email string is safer than a plain string that might or might not be a valid email.

Why it matters: when you pass raw strings and integers around, every function that uses them needs to handle the "invalid" case. Parse once, and downstream code gets a guarantee: if it received an `Email`, it is valid. This removes entire categories of bugs.

Example: an `Email` value object is created once at the boundary. If it is invalid, you get an error immediately. Everything downstream works with a valid `Email` and does not need to re-validate.

Related: [11 Domain Modelling and Errors](../standards/11-domain-modelling-and-errors.md) (strong ids and value records), [09 Endpoints and API Design](../standards/09-endpoints-and-api-design.md) (request validation).

## Don't Use Exceptions as Flow Control

Exceptions should be for truly exceptional situations: things that should not happen in normal operation. Do not use them to represent expected outcomes like "the email already exists" or "validation failed".

Guidance:
- Use explicit values for expected outcomes: a typed result such as `DomainError` from `Validate`, an enum or a small record. A general `Result<T>` library is not required.
- Reserve exceptions for genuinely unexpected failures.
- Make error paths visible in function signatures. Callers should know a function can fail without reading its implementation.

Why it matters: when exceptions carry business logic, the control flow becomes hidden and surprising. Every call site that does not catch exceptions is silently relying on something upstream handling them. Explicit values make failures visible and force callers to deal with them.

Example: `Validate` for `CreateUser` returns a `DomainError` with a clear error case when the email is already taken, instead of throwing.

Related: [09 Endpoints and API Design](../standards/09-endpoints-and-api-design.md) (central exception mapping), [11 Domain Modelling and Errors](../standards/11-domain-modelling-and-errors.md), [Railroad Programming](#railroad-programming), [Parse, Don't Validate](#parse-dont-validate).

## No Repositories

Avoid generic repository abstractions that hide what queries are actually being made. Express data access intent directly through well-named functions or extension methods.

Guidance:
- Name query methods after what they return, not the mechanism: `GetActiveCustomersByRegion()` beats `repository.Query()`.
- Keep data access close to the feature that needs it. Do not dump everything in a shared `IRepository<T>`.
- Generic repositories tend to grow into leaky abstractions that are harder to test and harder to optimize.

Why it matters: a generic repository does not communicate intent. When you see `_customerRepository.GetAll()`, you have no idea if it loads 10 or 10,000 records. When you see `GetActiveCustomersByRegion(region)`, you know exactly what it does. Explicit query methods also make it easier to add caching, projections or query optimization without polluting a shared abstraction.

Example: `GetActiveCustomersByRegion(region)` expresses intent clearly and is easy to optimize or test independently.

Related: [12 Data Access and Migrations](../standards/12-data-access-and-migrations.md), [10 Vertical Slices and CQRS](../standards/10-vertical-slices-and-cqrs.md), [Backend (.NET)](../technology/backend-dotnet.md).

## Database as a Private Implementation Detail

A service's database belongs to that service alone. Other services, jobs or teams must never read from or write to it directly. The schema is an implementation detail, not a public contract.

Guidance:
- Integrate through explicit contracts, such as an API or published events, never through shared tables.
- If another boundary genuinely needs access to your data, expose it deliberately (a read-only view, a replicated read model or an endpoint) so ownership stays clear.
- Own your schema. If you cannot change a column without coordinating with three other teams, the schema has become an accidental integration point.

Why it matters: when multiple boundaries share tables, no one owns the data. A migration in one service silently breaks another, coupling ships back in through the back door, and the "boundaries" become fiction. Explicit contracts make ownership and blast radius visible.

Example: a reporting job needs order data. Instead of querying the Orders service's tables directly, it consumes the order events the service already publishes and builds its own read model, so the Orders team stays free to change its schema.

Related: [12 Data Access and Migrations](../standards/12-data-access-and-migrations.md), [DTOs over Schema Exposure](security.md#dtos-over-schema-exposure), [No Repositories](#no-repositories), [Event-Driven Design](#event-driven-design), [Vertical Slices](#vertical-slices).

## Event-Driven Design

Services communicate by publishing events when something meaningful happens, rather than calling each other directly. Consumers subscribe to the events they care about.

Guidance:
- Publish events for meaningful domain changes: things that actually happened, in past tense (`UserRegistered`, `OrderApproved`).
- Keep event contracts stable and version them explicitly.
- Do not leak internal implementation details into event payloads.
- Use direct API calls for synchronous, tightly bounded workflows where events would add unnecessary indirection.

Why it matters: event-driven integrations let you add new consumers without touching the producer. Each service can evolve and deploy independently. It also makes the system easier to extend, since adding analytics, notifications or audit logging is just adding a new subscriber. Do not overuse it, though. Simple request-response workflows do not need to be events.

Example: when a user registers, a `UserRegistered` event triggers onboarding emails and analytics tracking, without the registration service knowing anything about either.

Related: [13 Messaging and Integration](../standards/13-messaging-and-integration.md), [Backend (.NET)](../technology/backend-dotnet.md) (Wolverine), [Event Sourcing](#event-sourcing).

## Event Sourcing

Instead of storing just the latest values of a record, you store every change as an immutable event. The present state is derived by replaying those events.

Guidance:
- Persist each state change as a domain event.
- Rebuild state from event streams when needed. The stream is your source of truth.
- Model events with clear domain meaning: `OrderPlaced`, `ItemAdded`, `OrderPaid`, not `OrderUpdated`.
- Do not use event sourcing just because it sounds attractive. It fits best where auditability, replay or temporal queries matter.

When to use it: event sourcing is a good fit when you need a full audit trail, want to debug by replaying history, or need to support projections and read models. For simple CRUD with no auditability requirements, it adds complexity without payoff.

Example: an order is rebuilt by replaying `OrderPlaced`, `ItemAdded` and `OrderPaid` events from the event stream.

Related: [11 Domain Modelling and Errors](../standards/11-domain-modelling-and-errors.md) (event naming), [12 Data Access and Migrations](../standards/12-data-access-and-migrations.md) (projection rebuilds), [Backend (.NET)](../technology/backend-dotnet.md) (Marten and EF Core), [Event-Driven Design](#event-driven-design).

## Task-Oriented APIs

Do not write generic CRUD APIs. Design endpoints around the specific tasks your users and systems actually perform.

Guidance:
- Model endpoints as explicit business operations: `POST /orders/{id}/approve` instead of `PATCH /orders/{id}` with a status field.
- Tailor request and response shapes to the task. Do not expose the full data model just because it is easy.
- Task-oriented APIs handle change much better. When the business process changes, you update one endpoint rather than untangling shared CRUD logic.
- Use resource-oriented REST endpoints only when the workflow is genuinely simple CRUD.

Why it matters: generic CRUD endpoints look like they keep things simple, but they shift complexity to the client. The client has to know which fields to set, in what combination, to trigger the right business logic. A task-oriented endpoint makes the workflow explicit and keeps business rules on the server.

Example: `POST /orders/{id}/approve` expresses a business action clearly, and the server knows exactly what to do. Compare `PATCH /orders/{id}` with `{"status": "approved"}`: is setting that field directly safe? Does it trigger side effects? The request does not say.

Related: [09 Endpoints and API Design](../standards/09-endpoints-and-api-design.md), [08 Code Generation and Contracts](../standards/08-code-generation-and-contracts.md), [DTOs over Schema Exposure](security.md#dtos-over-schema-exposure).

## External APIs

Do not expose internal APIs to outside consumers. A purpose-built API is required for external integrations.

When you need to expose an external API, it deserves the same level of care as an internal one, and often more. Design it with security, versioning and clear contracts in mind from the start.

- Protect against unauthorized access and handle sensitive data appropriately.
- Design for the expected load, so the external API does not become a bottleneck.
- Version APIs explicitly and communicate deprecation policies clearly so clients can migrate without surprises.
- Document using OpenAPI specifications.
- Provide SDKs for .NET and JavaScript to make integration easier for clients.
- External API consumers authenticate with JWT bearer tokens. Browsers reach internal APIs through the BFF with session cookies, and internal service-to-service calls use Google-signed OIDC tokens (see [14 Authentication and Sessions](../standards/14-authentication-and-sessions.md)).

Related: [08 Code Generation and Contracts](../standards/08-code-generation-and-contracts.md), [14 Authentication and Sessions](../standards/14-authentication-and-sessions.md).
