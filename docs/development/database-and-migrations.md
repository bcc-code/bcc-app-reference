# Database and migrations

Domain data is relational: ordinary tables in Postgres through EF Core. The model is `TemplateDb` in
`api/BccCode.Template/Persistence/`; migrations are in `api/BccCode.Template/Persistence/Migrations/`.

## The schema step

The schema never changes at API startup ([ADR 0016](../adrs/0016-schema-applied-by-a-deploy-step.md)). `jasper db-apply` applies the
EF Core migrations and Wolverine's envelope tables; `jasper db-assert` checks that the database matches.

- **Locally:** the AppHost runs it as the `schema` resource. The seeder and the API wait for it.
- **Deploy:** a step in `deploy.yml` runs the pushed image with `db-apply` through the Cloud SQL Auth Proxy, then `REASSIGN OWNED`.
- **Tests:** the integration fixture runs `db-apply` and `db-assert`.
- **Previews:** `Persistence:Schema=pr<N>` puts a preview in its own schema ([pr-previews](../pr-previews.md)).

Migrations are expand-only: the previous release must still run against the new schema, so a traffic rollback stays safe.
Contract (drop or rename) in a later release.

## Add a migration

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project api/BccCode.Template --startup-project api/BccCode.Template.Api
dotnet ef migrations has-pending-model-changes --project api/BccCode.Template --startup-project api/BccCode.Template.Api
```

The design-time factory builds the model without connecting to a database. The unit test `MigrationTests.Every_model_change_has_a_migration` runs the same check, so the
backend job fails when the model has changes no migration covers.

## The database snapshot

A new database does not start empty. `api/BccCode.Template.TestData/Snapshots/` holds a plain-SQL snapshot of the migrated and seeded
database, and the AppHost gives it to Postgres as init files (`WithInitFiles` in `DatabaseSnapshot.cs`). Postgres runs them only
when its data directory is empty, so every E2E run, a new isolated worktree and a new sole instance start from the snapshot, while an
existing volume or persistent container is left alone. The `schema` step and the seeder still run after the restore: `db-apply`
applies only the migrations that are newer than the snapshot, and the seeder finds its rows. With the template's three notes the
snapshot makes little difference; it is there so that a seed that grows to many thousands of rows does not have to be inserted again
on every fresh start.

- **Files.** `000-schema.sql` (`CREATE DATABASE` and the tables), one `100-<schema>.<table>.sql` per table with rows (`COPY`),
  `800-sequences.sql` and `900-post-data.sql` (indexes, constraints). Many small files, because DCP copies no file over 50 MB into a
  container; the writer fails when one gets that large. The files are generated and marked `linguist-generated` and `-diff` in
  `.gitattributes`.
- **Never in it:** the rows of `auth_sessions` and `data_protection_keys` (sessions and keys belong to one running instance). Their
  table definitions are kept.
- **Deterministic.** The seeder uses fixed ids and a fixed date (`SeedNotes`), never the clock or a random id, so the same inputs give
  byte-identical files. Keep it that way when you add seed data; a second run of the writer must give no `git diff`.

### Regenerate it

After a migration, and after a change to the seeder:

```bash
node tools/db-snapshot/update.mjs     # needs Docker and the .NET SDK; about 30 seconds
```

It starts a throwaway `postgres:18`, runs `jasper db-apply` and the seeder once (`Seeder:Once=true`, environment Development, which
`SeedingGate` allows) against it, dumps the result with `pg_dump` and `COPY`, and removes the container, also when a step fails.
Commit the changed files with the migration.

The unit test `DatabaseSnapshotTests` fails when the newest migration is not in the snapshot's `__ef_migrations_history` file, and
says to run the command. A snapshot older than the migrations would still work (the schema step applies the newer ones), but each
fresh start would be slower, so the test keeps it current. A seeder change that adds no migration is not detected by the test:
regenerate by hand.

### Reset a database

- **From the snapshot:** the "Reset database from snapshot" command on the `postgres` resource (dashboard, not in E2E runs, with a
  confirmation) drops the database, restores the init files with `psql -v ON_ERROR_STOP=1` inside the container, then restarts
  `schema` and `testdata` and waits until the seeder is healthy. A running API keeps its Data Protection keys in memory and
  loses its sessions, so sign in again. From a terminal: `aspire resource postgres reset-from-snapshot`.
- **Seeded notes only:** the "Reset test data" command on `testdata` deletes every table's rows except sessions and keys and seeds again.
- **Everything:** stop the AppHost and remove the volume (`docker volume rm bcc-template-<folder>-postgres`, or
  `bcc-template-postgres` for the sole instance); the next start restores the snapshot.

### After `scripts/init-app.ps1`

The script renames `template` in every text file, so the database name in the snapshot, in `update.mjs` and in the AppHost follows
the new slug, and the folder `BccCode.Template.TestData` is renamed too. The script skips files over 5 MB, though: when a data file
has grown that large it keeps the old name in its `connect` line. Run `node tools/db-snapshot/update.mjs` once after `init-app`
and the snapshot is right in any case.

## Rows, versions and removal

- Each entity changes only through its own methods (`Note.Create`, `Edit`, `Archive`), which take the time from the endpoint and raise
  `version`. `version` is mapped with `IsConcurrencyToken()`: a stale save fails and answers 409 (`DbConcurrencyMiddleware`).
- Endpoints take a `TimeProvider` and call `PostgresTime.Now`, which cuts the time to the microseconds Postgres keeps.
- The app never deletes a domain row: a business removal sets `archived_at`, and a query filter hides the row. Revoking the delete
  right and the erasure procedure are not built yet; an application adds them with its retention rule
  ([data inventory](../data-protection/data-inventory.md#anonymization-and-erasure)).
- The template keeps no audit trail or history of business data yet ([tech debt](../tech-debt/audit-history-not-in-the-template.md)).

Sessions and Data Protection keys are tables of the host, mapped in `Persistence/HostTables.cs`.

## Connect to the local database

The dashboard shows the Postgres resource and its connection string. In isolated mode the port is random and the password is the fixed one in `DatabaseResources.cs`, because the volume outlives the run.
