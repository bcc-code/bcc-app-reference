# 12 Data Access and Migrations

How slices read and write the database, how schema and data changes reach production, and how we handle tenancy, connection pooling and concurrency. Schema changes that run at app startup race between instances and can half-apply on a cold start; a missing tenant filter or a wrong lock order leaks data or deadlocks. EF Core (relational) and Marten (documents and events) are both allowed; where a rule differs by store, both variants are given.

## Rules

### Access from slices

1. **Slices MUST query the `IDocumentSession`/`IQuerySession` or `DbContext` directly; no repository classes.** The session already is a unit of work and a query API; a repository adds a layer that hides what is loaded and makes tests mock the wrong thing. See No Repositories.
2. **Queries reused by several slices SHOULD be named extension methods on `IQueryable<T>` or the session, kept next to the feature that owns the data.** orginn's `Data.Core/Queries/*Queries.cs` uses C# extension blocks for this. A named query states intent and keeps the LINQ in one place without becoming a repository.
3. **The database MUST be private to the service.** Other systems integrate through the API or messages, never through tables or views. Tables in a shared Cloud SQL instance are still owned by one service. See Database as a Private Implementation Detail.
4. **Code MUST NOT read or write framework-internal tables (`mt_events`, `mt_streams`, `wolverine_*`) with raw SQL.** Their layout changes between versions without notice. Use the framework API (Marten's stream archiving/deletion, Wolverine's dead-letter API); if no API exists, wrap the SQL in one class with a test that fails when the framework changes.
5. **Each repo SHOULD have a short persistence guide for developers that says which pattern to use when.** bcc-privacy `docs/internal/persistence.md` answers "document or event stream?" and lists the building blocks. Keep it in the Diátaxis how-to or reference section.

### Schema and migrations

6. **Schema migrations MUST NOT run at application startup in shared environments; they run as a separate deploy step before the new revision rolls out.** Use a Cloud Run job executed with `--wait` (orginn `DatabaseManager`) or a container command in the deploy workflow (vote-rewrite `db-apply`). Outside Development and Test, set `AutoCreate.None` (Marten) and do not call `Database.Migrate()` or `ApplyAllDatabaseChangesOnStartup()`. A failed migration then stops the deploy instead of crash-looping every instance.
7. **One registration point MUST define the store and schema configuration, shared by every host role and by the schema step.** vote-rewrite's `VoteStore.Configure` is called by the Api, the Processor and `db-apply`, so all agree on the schema even for documents only one role uses. For EF this is the single `DbContext` model.
8. **Style-dependent: schema changes are versioned migrations (EF) or code-defined schema applied by the schema step (Marten/Weasel); data migrations MUST be versioned scripts that run once.** EF: `dotnet ef migrations add`, applied by the job. Marten: the schema step runs `resources setup`/`db-apply`; data changes go in numbered DbUp SQL scripts (bcc-privacy `Migrations/01-...sql`) or a one-off migration command, not in startup code.
9. **Seeding test data MUST be gated by an allowlist of disposable environments, never by a "not prod" check.** A misspelled environment name must refuse to seed. orginn `SeedingGate`: `dev`, `sandbox`, `preview`. See [02 Local Development](02-local-development.md).

### Tenancy, pooling and concurrency

10. **Multi-tenant data MUST be protected by a global query filter, with the unfiltered context documented as an escape hatch for named cases.** orginn's `TenantedDataContext` filters every `IAppEntity` by `AppId`; its doc comment lists when the plain `DataContext` is allowed (background jobs, pre-session code, deliberate cross-tenant endpoints). Not needed in single-tenant apps.
11. **When connections go through a transaction-mode pooler (PgBouncer, Cloud SQL managed pooling), Npgsql MUST use pooler-safe settings, and an integration test MUST run the write paths through a real pooler.** Set `NoResetOnClose = true` and `MaxAutoPrepare = 0`, avoid session-level `SET` and session advisory locks, and send DDL directly, not through the pooler. vote-rewrite `PgBouncerTests` runs a PgBouncer container in front of Testcontainers Postgres.
12. **Concurrent updates MUST use optimistic concurrency (Marten stream versions, EF `xmin` concurrency tokens) mapped to `409 Conflict`.** See [09 Endpoints and API Design](09-endpoints-and-api-design.md).
13. **Where advisory locks are used, the lock order MUST be documented in one place and every code path MUST take locks in that order.** Use transaction-scoped locks (`pg_advisory_xact_lock`) so they release on commit or rollback. vote-rewrite ADR 0006: poll lock first, then question locks in ascending id order.
14. **Style-dependent (Marten): projections MUST be rebuildable, with a runbook and a test that a rebuild gives the same result.** vote-rewrite `docs/runbooks/projection-rebuild.md` and `ProjectionRebuildTests`; bcc-privacy has a rebuild UI (see [10 Vertical Slices and CQRS](10-vertical-slices-and-cqrs.md)).
15. **Tests of query behaviour MUST run against real Postgres (Testcontainers), not EF InMemory.** InMemory ignores SQL translation, case sensitivity, JSON columns, constraints and transactions. See [19 Testing Strategy](19-testing-strategy.md).

## Example

Marten variant: `api/BccCode.Template.Api.Marten/` (event stream, inline projection, stream-version concurrency). EF Core variant: `api/BccCode.Template.Api.EfCore/` with `Migrations/` (explicit integer `Version` concurrency token). Both run schema changes as an AppHost resource before the API starts, never at API startup.
