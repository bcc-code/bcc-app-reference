---
name: wolverine
description: Wolverine best practices (handlers, Wolverine.HTTP endpoints, messages, validation, error handling, idempotency, codegen, IoC) with their sources, and how they apply in this repo. Use when writing or reviewing a Wolverine handler, HTTP endpoint, message type, middleware, error policy or messaging configuration, and when deciding between cascading, publishing or invoking.
---

# Wolverine

Every rule below comes from one of these sources, cited by section:

- **[BP]** Wolverine docs, [Best Practices](https://wolverinefx.io/introduction/best-practices)
- **[REV]** Jeremy Miller, [Wolverine Best Practices, Revisited](https://jasperfx.net/news/wolverine-best-practices-2026.html) (2026-10-28)

**In this repo** notes say how a rule applies under our architecture: Cloud Run with no background work, `ServerlessWithOutbox`, EF Core on Postgres and Pub/Sub push (ADRs 0013, 0023, 0024). They add no rules of their own.

## Shape of a slice

- One file per command or request, holding the message, the handler or endpoint, `LoadAsync`, `Validate` and the validator. Related slices share a namespace, and only shared contracts go in a separate library. [BP, Vertical Slice Architecture]; [REV, Vertical Slices, One File at a Time](https://jasperfx.net/news/wolverine-best-practices-2026.html#vertical-slices-one-file-at-a-time)
- A-Frame: `LoadAsync` (or `[Entity]`) fetches the data. The main method is a pure function of that data, and it returns side effects (cascading messages, `Storage.*`, `ISideEffect`) rather than performing them. [BP, The A-Frame Architecture](https://wolverinefx.io/introduction/best-practices#the-a-frame-architecture)
- Unit test the pure method. Don't unit test `LoadAsync`; cover it with tracked-session or Alba integration tests. [BP, The A-Frame Architecture](https://wolverinefx.io/introduction/best-practices#the-a-frame-architecture)
- Don't wrap `IDocumentSession` or `DbContext` in repositories. Query logic shared between slices becomes a query plan or specification. Batch round trips with `Include()`, `LoadManyAsync()` and batched queries. [BP, Keep Your Call Stacks Short](https://wolverinefx.io/introduction/best-practices#keep-your-call-stacks-short)
- Don't grow a shared "services" layer. Use middleware and compound handlers for shared concerns. In Wolverine.HTTP, don't put Wolverine as a mediator under GET endpoints. [BP, Vertical Slice Architecture]
- Don't wrap Wolverine in your own abstractions. [BP, Avoid Abstracting Wolverine]

## Side effects visible from the root

- Publish only from the root handler method: return cascading messages (`null` for none), or take `IMessageBus` as a method argument. No service → service → `PublishAsync` chains. [BP, Make All Side Effects Apparent from the Root Handler]
- Don't call `IMessageBus.InvokeAsync()` from inside a handler to reuse logic. It hides a nested transaction. [BP, same section]
- Prefer asynchronous cascading workflows over request/reply. [BP, Prefer Purely Asynchronous Processing]
- **In this repo:** a cascaded message is stored in the outbox with the EF Core transaction and sent after the commit, inside the request or by the next durability pass. `UseEntityFrameworkCoreTransactions(TransactionMiddlewareMode.Eager)` opens the transaction before the endpoint runs, so the rows and the outbox row commit together; a read that takes the `DbContext` is marked `[NonTransactional]`. There is no event stream to forward: the endpoint that archives a note returns the message (`NoteArchivedMessage` from the archive slice).

## Validation and HTTP

- Use `Validate()`/`ValidateAsync()` for rules that need state or belong to one handler. Use FluentValidation for reusable validators, registered as singletons. Don't wrap return values in `Result<T>`. [BP, Prefer Validate() Methods for Business Rule Validation]
- For new code, write a Wolverine.HTTP endpoint where the endpoint method is the handler. `Validate` returning `ProblemDetails` stops the request with a 400. `[Entity]` answers 404 for a missing entity. No `Result<T>`/`OneOf<>` returns. [BP, Prefer Wolverine.HTTP Endpoints over "Wolverine as MediatR"]
- **In this repo:** the order is `LoadAsync` → `Permission` → `Before` → `Validate` → `Post` (standard 09.2; `PermissionPolicy` puts `Permission` after the load). `Permission` is pure and returns `Results<NotFound, Forbidden>?` (404 for a resource that does not exist, 403 for one the caller may not use, both bodiless), `Before` returns `StaleVersion?` (409), `Validate` returns `DomainError?` (400 with an i18n key). All are `IResult` early returns that Wolverine.HTTP treats like `ProblemDetails`; `null` continues. Wolverine binds the request body for `Before` and `Validate` only when the endpoint method (`Post`) takes the request too: a `Post` with no use for it keeps the parameter and suppresses IDE0060 with a reason. `PermissionConventionTests` requires `Permission` on every endpoint not on its allowlist.

## Messages

- Name commands in the imperative and events in the past tense. Use records or init properties. Prefer init properties or named arguments over positional records with several parameters of the same type, because transposed `Guid`s are easy to miss. [BP, The A-Frame Architecture](https://wolverinefx.io/introduction/best-practices#the-a-frame-architecture); [BP, Design Messages as Small, Immutable Contracts]
- Don't reuse EF entities, documents or view models as messages. Events are thin: ids and the facts that changed. [BP, Design Messages as Small, Immutable Contracts]
- Version additively: nullable properties, receivers deployed before senders, and a new versioned type only for a breaking change. [BP, same section]
- One handler method per class. When two handlers take the same message, give them their own endpoints (`[StickyHandler]`). [BP, Dividing Handlers]
- **In this repo:** internal messages implement `IInternalMessage` (our Pub/Sub topic, or the local durable inbox when `Messaging:UsePubSub` is off, which is the case locally and in tests) and integration events implement `IIntegrationEvent` (BCC PubSub, off until `BccPubSub:Enabled`). Only those two are routed; the architecture tests fail on any other cascaded type and `BannedSymbols.txt` bans local queues. `Wolverine.Runtime` has an `IInternalMessage` of its own, so a file that uses both needs an alias or a qualified name.

## One transaction, one kind of failure

- Each handler covers one transaction and one kind of failure. Every call to an external system gets its own handler with its own retries. Steps are connected by cascading through the outbox. [REV, Divide work along transaction and retry boundaries](https://jasperfx.net/news/wolverine-best-practices-2026.html#divide-work-along-transaction-and-retry-boundaries)
- Put a timeout on every external call. Retry at one layer, because nested retries multiply. Don't retry failures that will always fail (validation, deserialization, business rejection). [REV, Sort your errors into categories](https://jasperfx.net/news/wolverine-best-practices-2026.html#sort-your-errors-into-categories)
- Use error policies by category, specific rules before general ones. Don't catch and swallow exceptions. Don't silently `return` from a guard clause: throw a specific exception and give it a `Discard()` rule. [BP, Lean on Wolverine Error Handling]
- The dead letter queue needs an owner, monitoring and alerts. [BP, Lean on Wolverine Error Handling]
- **In this repo:** the host sets one chain for every handler: retry once, retry after a 5 s cooldown, schedule retries after 1 and 15 minutes, then the dead-letter table plus a `Fault<T>` message, with a 60 s execution timeout (`TemplateMessaging`). Dead letters have a runbook ([pubsub-dead-letters](../../../docs/runbooks/pubsub-dead-letters.md)). `AddStandardResilienceHandler` (ServiceDefaults) retries every HTTP method, POST included ([Microsoft, HTTP resilience](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience), "Disable retries for a given list of HTTP methods"). Inside a Wolverine handler that is a second retry layer. Use `options.Retry.DisableForUnsafeHttpMethods()` for calls Wolverine already retries.

## At-least-once delivery

- Prefer naturally idempotent handlers. The inbox deduplicates envelopes, not the same intent sent twice. For that, declare a business identity (logical message deduplication). Test idempotency by handling the same message twice. [REV, At-least-once delivery is the contract](https://jasperfx.net/news/wolverine-best-practices-2026.html#at-least-once-delivery-is-the-contract); [BP, Design for At-Least-Once Delivery]
- Don't rely on message order. Sagas only orchestrate, and every saga has an end. [BP, Design for At-Least-Once Delivery]
- Use durable endpoints where message loss matters. Use `DeliverWithin()` or expiration for messages that go stale. Use optimistic concurrency when loading aggregates for writes. [BP, Decide on the Transactional Outbox Early]

## Runtime and IoC

- Pre-generate code (`codegen write`), run `TypeLoadMode.Static` in production, and register runtime compilation only in development. [BP, Pre-Generate Types to Optimize Production Usage]
- Prefer method injection. No `Scoped`/`Transient` lambda registrations. Don't resolve `IMessageBus`, `IMessageContext` or `Envelope` from `IServiceProvider`. Check `codegen preview` for `_serviceScopeFactory.CreateScope()`. [BP, IoC Container Usage]
- Use the diagnostics: `describe`, `describe-routing --explain`, `codegen preview`, and OpenTelemetry. [BP, Use the Diagnostics]
- **In this repo:** broker listener tuning, circuit breakers and pause-on-error from [BP, Dividing Handlers] and [BP, Lean on Wolverine Error Handling] need running listeners. Cloud Run push delivery has none (ADR 0024). Scheduled messages (`ScheduleAsync`, `DelayedFor`) are allowed: the durability pass releases them, up to about a minute late. Release builds run pre-generated code: generate it with `dotnet run --project api/BccCode.Template.Api -c Release -- jasper codegen write` first (ADR 0012), and the `codegen` CI job fails on a handler that only works with runtime compilation.
