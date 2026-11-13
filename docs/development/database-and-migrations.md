# Database and migrations

Domain data is relational: ordinary tables, one typed `<table>_history` twin per audited table, and a `change` table of business
steps ([ADR 0028](../adrs/0028-relational-domain-data-with-typed-history.md)). The model is `TemplateDb` in
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

## History twins

Each audited entity has `change_id` and `version`, and a twin table `<table>_history` with the same columns plus `replaced_at` and
`replaced_by`. One trigger function, `history_track()`, copies the old row on update and refuses an update that does not point to
a new change or skips a version, so raw SQL and `ExecuteUpdate` are covered too.

- A migration that creates an audited table, or adds a column to one, ends with `migrationBuilder.Sql(HistorySql.Sync("<table>"))`,
  which creates or extends the twin and attaches the trigger. It is safe to run again.
- A rename or a type change is written by hand for **both** tables, so history moves with the data.
- Entities change only through methods that call `Record(change, "<Step>")` with the request's `Change`. One change has one step
  name; a request that is two steps is split.
- A stale `version` answers 409.
- The app never deletes a domain row: a business removal sets `removed_at`. Revoking the delete right and the erasure procedure are
  not built yet; an application adds them with its retention rule ([data inventory](../data-protection/data-inventory.md#anonymization-and-erasure)).

Sessions and Data Protection keys are tables without history. Drafts, if an application adds them, live in tables without history
and are copied into the audited tables on submit.

## Connect to the local database

The dashboard shows the Postgres resource and its connection string. In isolated mode the port and password are random.
