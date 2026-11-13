# Backend (.NET)

The primary backend stack is .NET with Postgres and a message-driven architecture. The sections below cover the default database, the web framework, the two persistence libraries, the messaging and command bus, and the nullable reference types setting. The approved technology areas are .NET and Go on the backend ([Backend (Go)](backend-go.md)), the Vue.js stack on the frontend ([Frontend (Vue.js)](frontend-vue.md)), and supporting tooling ([Cloud and tools](cloud-and-tools.md)).

## Postgres

Postgres is our default database for all backend services.

Guidance:
- Use Postgres as the default persistence store.
- Prefer a managed Postgres instance (for example Cloud SQL on GCP) over self-hosted.
- Postgres works well with both EF Core for relational workloads and Marten for document and event store workloads.
- Only introduce a different database if there is a concrete, well-justified need for it. Do not add databases speculatively.

Example: a service uses Postgres for both relational data (EF Core) and event streams (Marten). Same database, two different access patterns.

Related: [Marten and EF Core](#marten-and-ef-core), [Event Sourcing](../principles/architecture-and-domain.md#event-sourcing), [No Repositories](../principles/architecture-and-domain.md#no-repositories), [12 Data Access and Migrations](../standards/12-data-access-and-migrations.md).

## ASP.NET Core

ASP.NET Core is the standard .NET web framework for APIs, BFFs and internal services.

Guidance:
- Use it for all new .NET HTTP services.
- Use the minimal hosting model. It is cleaner and has less ceremony than the older Host/Startup split.
- Keep middleware ordering explicit. Middleware order matters and implicit ordering leads to bugs.
- Do not use legacy .NET Framework web stacks for new services.

Related: [Backend-for-Frontend (BFF)](../principles/security.md#backend-for-frontend-bff), [Security Headers](../principles/security.md#security-headers), [Auth Sessions (Cookies)](../principles/security.md#auth-sessions-cookies), [09 Endpoints and API Design](../standards/09-endpoints-and-api-design.md), [16 HTTP Security Hardening](../standards/16-http-security-hardening.md).

## Marten and EF Core

We use both Marten and EF Core, depending on the workload. Marten treats Postgres as a document and event store; EF Core is for traditional relational CRUD.

Guidance:
- **Marten** for document store and event sourcing workloads. It is well suited to aggregates that benefit from event streams.
- **EF Core** for traditional relational data and CRUD where a document model does not fit.
- Do not mix both in the same aggregate without clear boundaries. It creates confusion about which is the source of truth.
- For performance-critical queries, Dapper or raw parameterized SQL are fine alternatives when the ORM-generated query is not good enough.

Example: an event-sourced aggregate uses Marten event streams. Reporting queries in the same service use EF Core projections. Each uses the right tool for the job.

Related: [Postgres](#postgres), [Event Sourcing](../principles/architecture-and-domain.md#event-sourcing), [No Repositories](../principles/architecture-and-domain.md#no-repositories), [12 Data Access and Migrations](../standards/12-data-access-and-migrations.md).

## Wolverine

Wolverine is our messaging and command bus for .NET. It handles the plumbing of message routing and HTTP endpoints, so you can focus on handlers.

Guidance:
- Use Wolverine.HTTP for building API endpoints. It reduces boilerplate significantly compared to minimal APIs or controllers.
- Keep handlers small and focused. They should be thin glue between the HTTP layer and your domain logic.
- Wolverine supports the functional core, imperative shell pattern well: the handler is the shell, calling into pure domain logic.

Example: a `CreateCustomer` command is routed via Wolverine.HTTP to a handler that calls domain logic and publishes a domain event.

Related: [Functional Core, Imperative Shell](../principles/architecture-and-domain.md#functional-core-imperative-shell), [Event-Driven Design](../principles/architecture-and-domain.md#event-driven-design), [Task-Oriented APIs](../principles/architecture-and-domain.md#task-oriented-apis), [08 Code Generation and Contracts](../standards/08-code-generation-and-contracts.md), [10 Vertical Slices and CQRS](../standards/10-vertical-slices-and-cqrs.md), [13 Messaging and Integration](../standards/13-messaging-and-integration.md).

## Nullable Reference Types

Enable nullable reference types on every .NET project. The compiler then tracks which references can be `null` and warns you before a `NullReferenceException` reaches runtime.

Guidance:
- Set `<Nullable>enable</Nullable>` in every project, ideally in `Directory.Build.props` so it applies solution-wide.
- Combine with warnings-as-errors so nullability violations fail the build, not just annotate it.
- Do not silence warnings with the null-forgiving operator (`!`) to make them go away. Use it only where you can prove non-null and the compiler cannot.
- Model "may be absent" explicitly with `T?` rather than relying on convention or comments.

Why it matters: null-reference bugs are one of the most common runtime failures in .NET. Nullable reference types move that class of bug to compile time, and they make intent explicit at API boundaries. A `string?` return says "this can be missing" in a way documentation never enforces.

Related: [Parse, Don't Validate](../principles/architecture-and-domain.md#parse-dont-validate), [Railroad Programming](../principles/architecture-and-domain.md#railroad-programming), [01 Repository Layout and Build](../standards/01-repository-layout-and-build.md), [25 Code Quality Guards](../standards/25-code-quality-guards.md).
