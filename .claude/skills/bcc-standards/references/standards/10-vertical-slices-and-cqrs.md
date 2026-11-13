# 10 Vertical Slices and CQRS

This standard covers how we organise backend code by business capability and how commands, queries, events and messages flow through Wolverine. A change to one feature should touch one folder, and a handler should be testable by calling it and inspecting what it returns. The rules below make that the default and turn the conventions into failing tests where possible.

## Rules

### Folder layout

1. **Backend code MUST be organised in one folder per business capability, with one slice (endpoint or message handler) per file.** Files are named `<Feature>/<Action><Thing>.cs` and contain `<Action><Thing>Endpoint` (see [09 Endpoints and API Design](09-endpoints-and-api-design.md)). Examples: `Projects/CreateProject.cs`, `Approvals/ApproveRequest.cs`, `Billing/Invoices/WaiveInvoice.cs`. Sub-folders are fine for large features. See [Vertical Slices](../principles/architecture-and-domain.md#vertical-slices).
2. **Tests and frontend modules MUST mirror the feature folders.** `Projects/` in the API has `Projects/` in the test project and a matching module under the frontend's `modules/` or `pages/`. Someone working on a feature then finds all of it by name.
3. **Everything a feature needs MUST live in its folder: entities or documents, EF configuration or Marten registration, events, permission rules, validators, e-mail templates and per-feature service registration.** Splitting a feature across `Entities`, `Data.Core` and `Domain` projects means every change spans three projects and nobody sees the whole feature in one place. Shared kernel types (ids, base events, the DbContext) are the exception.
4. **Where an event model or spec exists, each slice SHOULD carry the model's slice id in a short doc comment.** This traces code to the model. Keep it to the id and one sentence; long chains of spec section numbers make the comments hard to read for someone without the spec open.

### Slice code

5. **Wolverine endpoint and handler classes MUST be static, and slices MUST NOT introduce `*Service` or `*Repository` types.** Framework types that share the suffix (for example `AuthorizationHandler<T>`, `DelegatingHandler` and `BackgroundService`) are not slices. Dependencies arrive as method parameters, so there is no constructor wiring to mock and no layer that hides data access. Pure logic shared between slices goes into named static classes (`ApprovalBusinessLogic`, `InvoiceGrouping`). See [No Repositories](../principles/architecture-and-domain.md#no-repositories).
6. **Handlers MUST return side effects as values instead of calling buses or infrastructure.** Return events (`Events`, `IStartStream`), `IMartenOp`s, cascading messages and audit records; Wolverine applies them. A test calls the method and asserts the returned values, without mocks. See [Functional Core, Imperative Shell](../principles/architecture-and-domain.md#functional-core-imperative-shell).
7. **A request MUST be one transaction, and outgoing messages MUST be sent only after it commits.** Turn on `Policies.AutoApplyTransactions()` (Marten and EF) or mark endpoints `[Transactional]`. A message sent before the commit can be handled against data that is then rolled back.
8. **Work that is not part of the transaction and must not fail the request MUST run in an after-commit hook that catches its own errors.** Examples: creating Cloud Tasks, warm-up scheduling. The hook runs only when the write is stored; a repair loop or retry covers the case where it fails. With Marten, use a `session.AfterCommit(...)` extension.
9. **Every cascaded message MUST be routed explicitly, either by returning `RoutedToEndpointMessage<T>` or by a central Wolverine routing rule.** In serverless mode an unrouted message fails at send time; with a durable outbox it may go to a local queue nobody expected.

### Hosts and delivery

10. **When the same image runs in more than one role, each role MUST assert at startup that it only does its own job.** The API role must not listen to queues (an in-house startup check, for example called `NoListenersAssertion`); the API role must not map `/internal/**` and the Processor role must not map `/api/**` (another in-house check, for example `RoleEndpointAssertion`). A misconfigured role then fails on deploy instead of processing work twice.
11. **Event forwarding from Marten to Wolverine MUST be configured in one place, with a regression test that each event is delivered once.** Adding `PublishEventsToWolverine()` next to fast event forwarding delivers every event twice. Keep a comment at the configuration and a test that fails if a subscription runner is registered.
12. **Dead letters and projection rebuilds MUST be operable from an admin-only UI or endpoints, with a runbook.** Operators need to see, replay and discard dead letters and rebuild a projection without database access. Protect them with the global admin policy.

### Durable outbox or serverless with reconcile

Message delivery is a style choice. Pick one per service and write it down in an ADR:

- **Durable outbox**: Wolverine stores outgoing messages in Postgres in the same transaction and a worker or the host delivers them. Messages are delivered at least once, so handlers must be idempotent; the host needs CPU between requests (a worker pool or always-on instance).
- **Serverless with reconcile**: no CPU outside requests. Messages are sent after commit to Pub/Sub or Cloud Tasks; a scheduled Reconcile job finds and repairs anything a failed send left behind. State can be stale until the next Reconcile run; every handler must be idempotent.

Both variants follow rules 6–12. See [13 Messaging and Integration](13-messaging-and-integration.md).

## Examples

### Side effects as return values (rule 6)
A publish endpoint returns `(Events, IMartenOp, RoutedToEndpointMessage<RefreshAssignments>)`. A store endpoint returns `(StoreResponse, SendNotification[], IReadOnlyList<IMartenOp>)`, where a routing rule sends `SendNotification` to its own queue. The response, the events and the messages are all values that a test can assert on.

### After-commit hook (rule 8)
```csharp
/// Runs <paramref name="action"/> after the session's commit, when the events are stored. A postprocessor runs before the
/// commit, so scheduling there could leave a task behind for a rolled-back write; a failed or rejected request never
/// reaches the listener.
public static void AfterCommit(this IDocumentSession session, Func<Task> action) => session.Listeners.Add(new AfterCommitListener(action));
```

### Role assertions (rule 10)
```csharp
var forbidden = role == AppRole.Api ? "/internal" : "/api";
return routePatterns
    .Select(p => "/" + p.TrimStart('/'))
    .Where(p => p.Equals(forbidden, StringComparison.OrdinalIgnoreCase)
                || p.StartsWith(forbidden + "/", StringComparison.OrdinalIgnoreCase))
    .ToList();
```

### Single event forwarding (rule 11)
```csharp
.IntegrateWithWolverine(integration =>
{
    integration.AutoCreate = AutoCreate.None;   // the schema step applies the schema (see 12)
    // Events appended in Wolverine-managed sessions are forwarded to handlers through the outbox.
    // Do not also add PublishEventsToWolverine(): that delivers every event a second time.
    integration.UseFastEventForwarding = true;
```

A matching integration test appends an event and asserts the handler runs exactly once.

### Operational UI (rule 12)
Admin pages map the Wolverine dead-letter endpoints (`MapDeadLettersEndpoints`) under the admin policy, and a test covers them. A runbook for dead letters and one for projection rebuild describe the operator steps; a test such as `ProjectionRebuildTests` covers the rebuild.

### Feature folder with its own registration (rules 1 to 3)
A feature folder such as `Billing/` holds a `ServiceExtension.cs` that registers its services, an `Events.cs`, its permission rules and sub-slices (`Invoices/`, `Reminders/`, `Review/`).

## Related
- [Vertical Slices](../principles/architecture-and-domain.md#vertical-slices), [No Repositories](../principles/architecture-and-domain.md#no-repositories), [Functional Core, Imperative Shell](../principles/architecture-and-domain.md#functional-core-imperative-shell), [Model workflow as a pipeline](../principles/architecture-and-domain.md#model-workflow-as-a-pipeline) — the principles behind these rules
- [Backend (.NET)](../technology/backend-dotnet.md) — Wolverine, Marten and EF Core
- [09 Endpoints and API Design](09-endpoints-and-api-design.md) — endpoint shape
- [11 Domain Modelling and Errors](11-domain-modelling-and-errors.md) — events and errors as values
- [12 Data Access and Migrations](12-data-access-and-migrations.md) — persistence
- [13 Messaging and Integration](13-messaging-and-integration.md) — outbox and serverless delivery
- [22 Observability and Operations](22-observability-and-operations.md) — operating dead letters
