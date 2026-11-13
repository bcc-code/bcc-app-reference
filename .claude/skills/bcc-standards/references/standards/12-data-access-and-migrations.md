# 12 Data Access and Migrations

This standard covers how slices read and write the database, how schema and data changes reach production, and how we handle tenancy, connection pooling and concurrency. Schema changes that run at app startup race between instances and can half-apply on a cold start; a missing tenant filter or a wrong lock order leaks data or deadlocks. EF Core (relational) and Marten (documents and events) are both allowed; where a rule differs by store, both variants are given.

## Rules

### Access from slices

1. **Slices MUST query the `IDocumentSession`/`IQuerySession` or `DbContext` directly; no repository classes.** The session already is a unit of work and a query API; a repository adds a layer that hides what is loaded and makes tests mock the wrong thing. See [No Repositories](../principles/architecture-and-domain.md#no-repositories).
2. **Queries reused by several slices SHOULD be named extension methods on `IQueryable<T>` or the session, kept next to the feature that owns the data.** C# extension blocks work well for this (for example `OrganizationQueries.cs`). A named query states intent and keeps the LINQ in one place without becoming a repository.
3. **The database MUST be private to the service.** Other systems integrate through the API or messages, never through tables or views. Tables in a shared Cloud SQL instance are still owned by one service. See [Database as a Private Implementation Detail](../principles/architecture-and-domain.md#database-as-a-private-implementation-detail).
4. **Code MUST NOT read or write framework-internal tables (`mt_events`, `mt_streams`, `wolverine_*`) with raw SQL.** Their layout changes between versions without notice. Use the framework API (Marten's stream archiving/deletion, Wolverine's dead-letter API); if no API exists, wrap the SQL in one class with a test that fails when the framework changes.
5. **Each repo SHOULD have a short persistence guide for developers that says which pattern to use when.** For example a `docs/internal/persistence.md` that answers "document or event stream?" and lists the building blocks. Keep it in the Diátaxis how-to or reference section (see [23 Documentation and Decisions](23-documentation-and-decisions.md)).

### Schema and migrations

6. **Schema migrations MUST NOT run at application startup in any environment; they run as a separate step before the new revision rolls out.** In deployed environments use a Cloud Run job executed with `--wait` (a `DatabaseManager` job) or a container command in the deploy workflow (a `db-apply` step); locally the same schema job runs before the app starts (see [02 Local Development](02-local-development.md)). In every environment set `AutoCreate.None` (Marten) and do not call `Database.Migrate()` or `ApplyAllDatabaseChangesOnStartup()`. A failed migration then stops the deploy instead of crash-looping every instance.
7. **One registration point MUST define the store and schema configuration, shared by every host role and by the schema step.** For example a `AppStore.Configure` method called by the Api, the Processor and `db-apply`, so all agree on the schema even for documents only one role uses. For EF this is the single `DbContext` model.
8. **Style-dependent: schema changes are versioned migrations (EF) or code-defined schema applied by the schema step (Marten/Weasel); data migrations MUST be versioned scripts that run once.** EF: `dotnet ef migrations add`, applied by the job. Marten: the schema step runs `resources setup`/`db-apply`; data changes go in numbered DbUp SQL scripts (`Migrations/01-...sql`) or a one-off migration command, not in startup code.
9. **Seeding test data MUST be gated by an allowlist of disposable environments, never by a "not prod" check.** The full rule and an example are in [02 Local Development](02-local-development.md) (rule 10).

### Tenancy, pooling and concurrency

10. **Multi-tenant data MUST be protected by a mechanism that applies to every query (an EF Core global query filter, Marten tenancy or Postgres row-level security), with unfiltered access documented as an escape hatch for named cases.** For example, with EF Core a `TenantedDataContext` filters every `IAppEntity` by `AppId`; its doc comment lists when the plain `DataContext` is allowed (background jobs, pre-session code, deliberate cross-tenant endpoints). Not needed in single-tenant apps.
11. **When connections go through a transaction-mode pooler (PgBouncer, Cloud SQL managed pooling), Npgsql MUST use pooler-safe settings, and an integration test SHOULD run the write paths through a real pooler.** Set `NoResetOnClose = true` and `MaxAutoPrepare = 0`, avoid session-level `SET` and session advisory locks, and send DDL directly, not through the pooler. Newer PgBouncer versions (1.21 and later) can support prepared statements in transaction mode; if you rely on that, check the pooler's documentation and keep the test. A `PgBouncerTests` fixture can run a PgBouncer container in front of Testcontainers Postgres.
12. **Concurrent updates MUST use optimistic concurrency (Marten stream versions, EF `xmin` concurrency tokens) mapped to `409 Conflict`.** An explicit integer `Version` column as concurrency token is also acceptable with EF. Use advisory locks (rule 13) only where optimistic concurrency is not enough. See [09 Endpoints and API Design](09-endpoints-and-api-design.md).
13. **Where advisory locks are used, the lock order MUST be documented in one place and every code path MUST take locks in that order.** Use transaction-scoped locks (`pg_advisory_xact_lock`) so they release on commit or rollback. Example, recorded in an ADR: project lock first, then task locks in ascending id order.
14. **Style-dependent (Marten): projections MUST be rebuildable, with a runbook and a test that a rebuild gives the same result.** Keep a `projection-rebuild.md` runbook and a `ProjectionRebuildTests` test; an admin rebuild UI is an option (see [10 Vertical Slices and CQRS](10-vertical-slices-and-cqrs.md)).
15. **Tests of query behaviour MUST run against real Postgres (Testcontainers), not EF InMemory.** InMemory ignores SQL translation, case sensitivity, JSON columns, constraints and transactions. See [19 Testing Strategy](19-testing-strategy.md).

## Examples

### Migrations as a gated deploy job (rule 6)
A `schema` job runs before `deploy`. A variant uses a `db-apply` step in the deploy workflow, followed by `REASSIGN OWNED` (a Postgres command that moves ownership of the created objects to the project role).

```yaml
gcloud run jobs execute myapp-database-manager-${{matrix.env}} \
  --region ${{ vars.GCP_REGION }} \
  --wait
...
deploy:
  needs: [schema]
```

### Single store registration (rules 7, 6, 11)
```csharp
/// Every Marten document and projection registration. Both roles and the schema step (`resources setup`, `db-apply`)
/// call this, so they all agree on the schema, even for documents only one role uses.
public static class AppStore
{
    public static void Configure(StoreOptions opts)
    {
        opts.Schema.For<DataProtectionKeyDocument>();
        ...
        opts.Projections.Add<ProjectViewProjection>(ProjectionLifecycle.Inline);   // inline only
```

The host configuration sets `AutoCreateSchemaObjects = AutoCreate.None` in every environment, because the schema job applies the schema, and the pooler-safe `NpgsqlConnectionStringBuilder(connectionString) { NoResetOnClose = true, MaxAutoPrepare = 0 }`.

### Tenant filter with documented escape hatch (rule 10)
```csharp
/// The default context. It adds a global query filter pinning every <see cref="IAppEntity"/> to the
/// AppId of the current session, so a request cannot read another tenant's rows even if a query
/// forgets to filter on AppId.
///
/// ApplyQueryFilter is an in-house extension that calls HasQueryFilter for every entity type implementing the interface.
...
public class TenantedDataContext(DbContextOptions<DataContext> options, ISessionProvider sessionProvider) : DataContext(options, sessionProvider)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyQueryFilter<IAppEntity>(x => x.AppId == SessionProvider.AppId);
    }
}
```

### Seeding allowlist (rule 9)
The allowlist and its example live in [02 Local Development](02-local-development.md) (rule 10).

### Pooler test and lock order (rules 11, 13)
- The pooler test runs PgBouncer in transaction mode in front of the test Postgres. DDL is applied directly first, as the `db-apply` step does.
- Lock order lives in an ADR (for example `0006-advisory-locks.md`) and in the code that takes the locks (`ProjectWriteLock`, `TaskLock`).

### Named queries, persistence guide and data migrations (rules 2, 5, 8)
- Named queries: an `OrganizationQueries.cs` file next to the feature, with extension methods on `IQueryable<Organization>`.
- Persistence guide: `docs/internal/persistence.md`.
- Data migrations: DbUp scripts embedded as resources in an API `Migrations/` folder.
- Marten variant: event stream, inline projection and stream-version concurrency. EF Core variant: `Migrations/` and an explicit integer `Version` concurrency token. Both run schema changes as a separate resource before the API starts, never at API startup.

## Related
- [No Repositories](../principles/architecture-and-domain.md#no-repositories), [Database as a Private Implementation Detail](../principles/architecture-and-domain.md#database-as-a-private-implementation-detail), [Event Sourcing](../principles/architecture-and-domain.md#event-sourcing) — the principles behind these rules
- [Backend (.NET)](../technology/backend-dotnet.md) — Marten, EF Core and Postgres
- [02 Local Development](02-local-development.md) — seeding and local databases
- [06 Deployment and Release](06-deployment-and-release.md) — the schema step in the deploy workflow
- [10 Vertical Slices and CQRS](10-vertical-slices-and-cqrs.md) — slice conventions and projection operations
- [15 Authorization and Permission Checks](15-authorization-and-permission-checks.md) — tenant and permission filtering
- [19 Testing Strategy](19-testing-strategy.md) — real Postgres in tests
