---
standard: 12 Data Access and Migrations
assessed: 2026-10-19
commit: e4b2b86
updated: 2026-10-20 (ServerlessWithOutbox, ADR 0007)
---
# 12 Data Access and Migrations — assessment

Data access and schema handling are done well in both variants: slices use the session or `DbContext` directly, the schema is
applied by a separate step (AppHost resource locally, Cloud Run job with `--wait` in deploy), seeding is allowlist-gated and
all database tests run on Testcontainers Postgres. Wolverine's envelope tables (outbox, incoming, dead letters) are created
by the same schema step in both variants and never at API startup outside Development and Test, with a test that runs the
schema step on an empty database. Gaps: no rebuild path for the Marten projection, no persistence guide for
developers, and no test of a commit-time concurrency race in the Marten variant.

**Score: Adequate** · ✅ 8 · ⚠️ 2 · ❌ 1 · N/A 4

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | Slices query session/`DbContext` directly; no repositories | MUST | ✅ | Marten slices take `IQuerySession` (`GetNote.cs:12`, `ListNotes.cs:11`); EF Core slices take `TemplateDbContext` (`GetNote.cs:14`, `ListNotes.cs:13`). Test `SliceConventionTests.No_services_or_repositories`. |
| 2 | Reused queries as named extension methods near the owner | SHOULD | N/A | No query is shared by more than one slice; the shared visibility rule is a pure function (`NoteRules.IsVisibleTo`). |
| 3 | Database private to the service | MUST | ✅ | `infra/database.tf:44` creates the app's own database; no views, grants or connection strings for other systems in `infra/` or `api/`. See "Not verifiable". |
| 4 | No raw SQL on framework-internal tables | MUST | ✅ | Only raw SQL is `select 1` in `Infrastructure/DatabaseHealthCheck.cs` (Marten `:12`, EF Core `:13`). No `mt_`/`wolverine_` SQL in application code (grep over `api/`, tests excluded). `MessagingTests` reads and updates `wolverine_outgoing_envelopes` to age a row for the durability-pass test, and `InternalRoutesTests` reads the catalog to check the envelope tables; both are tests, which fail when the framework layout changes. `docs/runbooks/durability-pass.md` gives an operator a read-only `select` on the outbox. |
| 5 | Short persistence guide for developers | SHOULD | ⚠️ | `docs/adrs/0003-marten-or-ef-core.md` explains how to choose a variant, not how to work in it (when to add a document vs. an event, how to add a migration, concurrency, seeding). No how-to in `docs/`. |
| 6 | No migrations at startup in shared envs; separate deploy step | MUST | ✅ | Marten `AutoCreate.None` outside Development/Test (`MartenConfiguration.cs:34`); EF Core migrates only in `Infrastructure/MigrateCommand.cs:29-36`, and Wolverine's message storage is `AutoBuildMessageStorageOnStartup = AutoCreate.None` outside Development and Test in both variants (`api/BccCode.Template.Web/Messaging/TemplateMessaging.cs:31`), and EF Core also sets `ResourceAutoCreate` to `None` (`EfCore/Configuration/WolverineConfiguration.cs:59`). The schema step creates the envelope tables with the outgoing timestamp column and index the durability pass needs: `InternalRoutesTests.The_schema_step_creates_the_envelope_tables_with_the_timestamp_column_and_index` runs `resources setup` (Marten) or `migrate` (EF Core) on an empty database without Pub/Sub settings, as the Cloud Run job does. AppHost runs `schema` before `seed` and `api` (`api/AppHost/Program.cs`); `deploy.yml:185-188,258-261` runs `template-migrate-<env>` with `--wait`; `infra/cloudrun_migrate.tf` defines the job with `max_retries = 0`. |
| 7 | One registration point for store/schema, shared by all hosts | MUST | ✅ | Marten: `Configuration/TemplateStore.cs` via `MartenConfiguration.cs:35`, used by the API and by `resources setup` (same host). EF Core: one `TemplateDbContext` and `EfCoreConfiguration.ConfigureProvider` (`:42-45`) shared by the API, `migrate` and `DesignTimeDbContextFactory.cs:15`. |
| 8 | Versioned EF migrations / Marten schema step; data migrations run once | MUST | ✅ | EF Core `Migrations/20261019183137_Initial.cs` plus `PersistenceTests.The_migrations_match_the_model_and_leave_no_pending_change`. Marten: `resources setup` (`AppHost/Program.cs:54`, `infra/variables.tf:72-74`). No data migrations exist yet, and no mechanism (DbUp scripts or a one-off command) is scaffolded for them. |
| 9 | Seeding gated by allowlist of disposable envs | MUST | ✅ | `Seeding/SeedingGate.cs:10` (Development, Test, Sandbox, Preview) in both; `SeedCommand.cs` exits non-zero on refusal. Tests `PersistenceTests.Disposable_environments_may_be_seeded` and `Anything_else_is_refused_including_a_misspelled_name` (both). |
| 10 | Multi-tenant data behind a global query filter | MUST | N/A | Single-tenant sample. Per-user ownership is enforced by `NoteRules.IsVisibleTo` and the list query's `OwnerId` filter. |
| 11 | Pooler-safe Npgsql settings and a pooler integration test | MUST | N/A | No transaction-mode pooler in `infra/` (Cloud SQL Auth Proxy, `infra/cloudrun_api.tf:5`). The settings are already applied (`NoResetOnClose`, `MaxAutoPrepare = 0` in `MartenConfiguration.cs:26`, `EfCoreConfiguration.cs:38`), but there is no pooler test, which becomes required if managed pooling is turned on. Wolverine's durability pass takes a session-level advisory lock, which breaks behind a transaction-mode pooler; ADR 0007 and `infra/README.md` say to give the message store a direct connection before turning pooling on. |
| 12 | Optimistic concurrency mapped to 409 | MUST | ⚠️ | Marten: stream version on append (`EditNote.cs:40`, `ArchiveNote.cs:27`) and `MapMartenConcurrencyFailuresToConflict` (`Program.cs:30-31`). EF Core: `Version` as concurrency token (`NoteConfiguration.cs:20`) and `ConcurrencyConflictHandler`. EF Core tests the race at commit (`NotesApiTests.An_edit_that_loses_a_race_at_commit_answers_409_and_keeps_the_other_writers_change`); the Marten variant only tests the pre-check (`An_edit_of_a_stale_version_answers_409_and_changes_nothing`), so its commit-time mapping is untested. |
| 13 | Advisory lock order documented | MUST | N/A | The application takes no advisory locks (grep `advisory` over `api/`). Wolverine's durability pass takes one session-level lock per message store inside the library; it is the only lock, so there is no order to document, and ADR 0007 describes it. |
| 14 | Marten projections rebuildable, with runbook and test | MUST | ❌ | Marten `Note` snapshot projection (`TemplateStore.cs:21`) has no rebuild command or endpoint, no runbook and no rebuild test. `docs/runbooks/README.md:18-19` treats this as not needed because the projection is inline. EF Core: N/A. |
| 15 | Query tests on real Postgres, not InMemory | MUST | ✅ | `Infrastructure/AppFixture.cs:32` (Marten) and `:35` (EF Core) use `PostgreSqlBuilder("postgres:18")`; no `UseInMemoryDatabase` in the API or test projects (only the local OIDC mock, `api/OidcMock/Program.cs:21`, uses it for its own client store). |

## Gaps to close

- **14**: Add a projection rebuild for the Marten variant (for example a JasperFx `projections --rebuild` run as a Cloud Run
  job, or an admin endpoint), a test that rebuilding the `Note` snapshot gives the same documents, and
  `docs/runbooks/projection-rebuild.md`; correct `docs/runbooks/README.md:18-19`.
- **12**: Add a Marten test equivalent to the EF Core race test: two appends at the same expected version, the second answers
  409 and the first writer's change is kept (`api/tests/BccCode.Template.Api.Marten.Tests/NotesApiTests.cs`).
- **8**: Decide how data migrations run (numbered DbUp SQL scripts or a one-off command run by the migrate job) and add the
  empty mechanism with a short note, so the first data change does not end up in startup code.
- **5**: Add a persistence how-to under `docs/` for each variant: adding an entity/document or event, adding a migration,
  concurrency, seeding, and the schema step.

## Not verifiable in the repo

- Rule 3: that no other service or BI tool has credentials to the database. Check Cloud SQL users and IAM bindings in each
  GCP project.
- Rule 6: that the API starts in a deployed environment with a database user that has no DDL rights, now that Wolverine only
  checks that its tables exist (`AutoCreate.None`). Check with the first sandbox deploy.
- Rule 11: whether Cloud SQL managed connection pooling is enabled on the instance (GCP console or `gcloud sql instances describe`).
  If it is, the durability pass lock (ADR 0007) is affected as well.
