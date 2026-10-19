# 10 Vertical Slices and CQRS

How we organise backend code by business capability and how commands, queries, events and messages flow through Wolverine. A change to one feature should touch one folder, and a handler should be testable by calling it and inspecting what it returns. The rules below make that the default and turn the conventions into failing tests where possible.

## Rules

### Folder layout

1. **Backend code MUST be organised in one folder per business capability, with one slice (endpoint or message handler) per file.** Examples: `Polls/CreatePoll.cs`, `FamilyApproval/ApproveFamilyConsentEndpoint.cs`, `MembershipFees/PaymentRequests/WaivePaymentRequestEndpoint.cs`. Sub-folders are fine for large features. See Vertical Slices.
2. **Tests and frontend modules MUST mirror the feature folders.** `Polls/` in the API has `Polls/` in the test project and a matching module under the frontend's `modules/` or `pages/`. Someone working on a feature then finds all of it by name.
3. **Everything a feature needs MUST live in its folder: entities or documents, EF configuration or Marten registration, events, permission rules, validators, e-mail templates and per-feature service registration.** Splitting a feature across `Entities`, `Data.Core` and `Domain` projects means every change spans three projects and nobody sees the whole feature in one place. Shared kernel types (ids, base events, the DbContext) are the exception.
4. **Where an event model or spec exists, each slice SHOULD carry its slice id in a short doc comment (`S10`, `S39`).** This traces code to the model. Keep it to the id and one sentence; long chains of spec section numbers and "ruling N" references make the comments hard to read for someone without the spec open.

### Slice code

5. **Endpoint and handler classes MUST be static, and slices MUST NOT introduce `*Service` or `*Repository` types; an architecture test MUST enforce both.** Dependencies arrive as method parameters, so there is no constructor wiring to mock and no layer that hides data access. Pure logic shared between slices goes into named static classes (`FamilyConsentBusinessLogic`, `InvoiceFamilies`). See No Repositories.
6. **Handlers MUST return side effects as values instead of calling buses or infrastructure.** Return events (`Events`, `IStartStream`), `IMartenOp`s, cascading messages and audit records; Wolverine applies them. A test calls the method and asserts the returned values, without mocks. See Functional Core, Imperative Shell.
7. **A request MUST be one transaction, and outgoing messages MUST be sent only after it commits.** Turn on `Policies.AutoApplyTransactions()` (Marten and EF) or mark endpoints `[Transactional]`. A message sent before the commit can be handled against data that is then rolled back.
8. **Work that is not part of the transaction and must not fail the request MUST run in an after-commit hook that catches its own errors.** Examples: creating Cloud Tasks, warm-up scheduling. The hook runs only when the write is stored; a repair loop or retry covers the case where it fails. vote-rewrite: `session.AfterCommit(...)`.
9. **Every cascaded message MUST be routed to an explicit endpoint, and an architecture test MUST check handler return types.** In serverless mode an unrouted message fails at send time; with a durable outbox it may go to a local queue nobody expected. The test walks every endpoint and handler and fails on a bare message type.

### Hosts and delivery

10. **When the same image runs in more than one role, each role MUST assert at startup that it only does its own job.** The API must not listen to queues (orginn `NoListenersAssertion`); the Api must not map `/internal/**` and the Processor must not map `/api/**` (vote-rewrite `RoleEndpointAssertion`). A misconfigured role then fails on deploy instead of processing work twice.
11. **Event forwarding from Marten to Wolverine MUST be configured in one place, with a regression test that each event is delivered once.** Adding `PublishEventsToWolverine()` next to fast event forwarding delivers every event twice. bcc-privacy keeps a comment at the configuration and a test that fails if a subscription runner is registered.
12. **Dead letters and projection rebuilds MUST be operable from an admin-only UI or endpoints, with a runbook.** Operators need to see, replay and discard dead letters and rebuild a projection without database access. Protect them with the global admin policy.

### Durable outbox or serverless with reconcile

Message delivery is a style choice. Pick one per service and write it down in an ADR:

- **Durable outbox** (orginn, bcc-privacy): Wolverine stores outgoing messages in Postgres in the same transaction and a worker or the host delivers them. Delivery is guaranteed; the host needs CPU between requests (a worker pool or always-on instance).
- **Serverless with reconcile** (vote-rewrite, ADR 0002): no CPU outside requests. Messages are sent after commit to Pub/Sub or Cloud Tasks; a scheduled Reconcile job finds and repairs anything a failed send left behind. State can be stale until the next Reconcile run; every handler must be idempotent.

Both variants follow rules 6–12. See [13 Messaging and Integration](13-messaging-and-integration.md).

## Example

`Features/Notes/` in each API variant: one folder per capability, one slice per file. Architecture tests in `api/tests/BccCode.Template.ArchitectureTests/` enforce the conventions.
