---
name: relational-slice
description: How to add an audited entity and its endpoints to this template, following api/BccCode.Template/Notes - the Audited base class, Record steps on the Change, the history twin and history_sync in the migration, Permission/Before/Validate, the tests to add and the contract regeneration. Use when adding a table, an entity, a business step, a column to an audited table or a slice that writes data, and when a test about history, versions or 409 fails.
---

# Relational slice

Domain data is ordinary EF Core tables with a typed `<table>_history` twin per audited table and a `change` table of business steps
([ADR 0028](../../../docs/adrs/0028-relational-domain-data-with-typed-history.md)). The sample is `api/BccCode.Template/Notes/`. Copy it rather
than writing from memory, and keep one slice per file. The Wolverine rules for the endpoint itself are in the `wolverine` skill; the
test rules for access are in `permission-tests`.

## What goes where

| Piece | Where | Note |
|---|---|---|
| Entity | `<Feature>/<Entity>.cs` | `class X : Audited`, private setters, a protected parameterless constructor for EF |
| History twin | same file | `sealed class XVersion : X, IReplacedVersion`, private constructor |
| Mapping | `<Feature>/<Entity>Configuration.cs` | two `IEntityTypeConfiguration` classes: the row calls `HistoryMapping.ConfigureAudited(b, "table")`, the twin calls `HistoryMapping.ConfigureHistory(b, "table")` |
| DbSets | `api/BccCode.Template/Persistence/TemplateDb.cs` | `DbSet<X>` and `DbSet<XVersion>`; the configurations are found by assembly scanning |
| Migration | `api/BccCode.Template/Persistence/Migrations/` | ends with `migrationBuilder.Sql(HistorySql.Sync("table"))` and a `Down` with `HistorySql.DropHistory("table")` |
| Endpoints | `<Feature>/<Verb><Thing>.cs` | one static class per endpoint with its sealed request and response records |
| Rules | `<Feature>/<Entity>Rules.cs` | pure functions shared by the slices (`Permission`, `Freshness`) |
| Value types | `<Feature>/<Entity>Values.cs` | `TryParse` for `Validate`, `Parse` for `Post` |

## The entity

- Derive from `Audited`. It gives `Id`, `Version` (the concurrency token, 1 on insert), `ChangeId` and the `Change` navigation, and
  asks for `SubjectId`: the id of the case the row belongs to, its own id for a root row and the parent's for a child row.
- State changes only through public methods that take the request's `Change` and end with `Record(change, "<Step>")`. The step name
  is a past-tense constant (`Note.Created`, `Note.Edited`, `Note.Archived`). One change has one step: a request that is two steps
  is two requests. `Record` throws if the same change touches the row twice.
- Times come from `change.At` (built from `TimeProvider`), never from `DateTime.UtcNow` (banned).
- A removal sets `RemovedAt` and the mapping adds `HasQueryFilter(x => x.RemovedAt == null)`. The app never deletes rows.
- Enums are stored as text: `HasConversion<string>()` on the row's configuration and on the twin's, because the twin does not
  inherit it.
- A step that writes no row is a change of its own: name it with `change.Name(type, subjectId, reason)` and add the change to the `TemplateDb`.

## The migration

1. Add the entity, the twin, the configurations and the `DbSet`s.
2. `dotnet ef migrations add <Name> --project api/BccCode.Template --startup-project api/BccCode.Template.Api`. The twin is
   `ExcludeFromMigrations`, so EF writes only the row's table.
3. Append `migrationBuilder.Sql(HistorySql.Sync("<table>"))` to `Up` (also for a migration that only adds a column to an audited
   table) and `migrationBuilder.Sql(HistorySql.DropHistory("<table>"))` to the start of `Down` for a new table. `history_sync` creates or extends the
   twin and attaches the `history_track` trigger, and is safe to run again.
4. A rename or a type change is written by hand for both tables, in one migration.
5. Changes are additive (new tables, new nullable or defaulted columns), so the previous revision still runs against the new schema.
6. `dotnet ef migrations has-pending-model-changes ...` must report none; the unit test `MigrationTests` runs the same check.

## The endpoint

Order: `LoadAsync` → `Permission` → `Before` → `Validate` → `Post`.

- `LoadAsync` loads the entity **tracked** for a write (EF puts the version it read in the `WHERE` of the update); `AsNoTracking` for a read, and a
  read takes `[NonTransactional]`.
- `Permission(entity?, CallerAccess)` is pure: `NotFound` when there is none for anyone (unknown, archived), `Forbidden` when it exists and the
  caller may not use it, else `null`. Both are bodiless. Business rules do not belong here.
- `Before` returns `StaleVersion?` for a version the client did not see (409). `Validate` returns `DomainError?` with an i18n key
  (`errors.*`), using the value type's `TryParse`.
- `Post` takes the request when `Before` or `Validate` need it (Wolverine binds the body only then), the entity as `[NotBody]`, a
  `CallerAccess` for the person uid and a `Change`, calls the entity method and returns the response. A create returns
  `(Created<TResponse>, IStorageAction<TEntity>)`: `Storage.Insert(entity)`. Ids for new rows are `Guid.CreateVersion7(change.At)`.
- A second writer racing past `Before` fails on the concurrency token at commit and answers 409 through `ConflictPolicy`; nothing to write.
- Messages for other systems: return an `IInternalMessage` from `Post` (the outbox stores it in the same transaction). `NoteArchivedMessage` is the sample.
- Add the endpoint's class to nothing by hand: the `[WolverinePost]` attribute registers the route, `PermissionConventionTests` requires
  `Permission`, and the policy sweep requires the policy for its prefix.

## Tests to add

| Test | Project | What |
|---|---|---|
| Entity and rules | `BccCode.Template.Tests` (`Notes/NoteSliceTests.cs`) | `Permission`, `Before`, `Validate` and the entity methods called directly: the 404/403/null answers, stale and fresh versions, the step name recorded, a second `Record` with the same change throws |
| Contract and access | `BccCode.Template.IntegrationTests` (`NotesApiTests.cs`) | success answers and the response, foreign 403 and unknown 404 with no body, stale 409, invalid 400 with the key, **nothing written** after a refusal (row, version, `change`, history) |
| History | `Changes/HistoryTests.cs`, `NoteHistoryTests.cs` | the parity test (every audited table has a twin with the same columns and the trigger) covers a new table by itself and fails if `history_sync` is missing; add a test for a step whose history reads differently |
| Convention | `PermissionConventionTests`, `SliceConventionTests` | pass without edits when the slice follows the pattern; do not add an allowlist entry to make them pass |

## After the change

```powershell
dotnet build BccCode.Template.slnx
dotnet test --project api/tests/BccCode.Template.Tests
dotnet test --project api/tests/BccCode.Template.IntegrationTests   # Docker
pnpm --dir frontend generate                                         # the contract and the client changed
dotnet format BccCode.Template.slnx --verify-no-changes
```

A new field that holds personal data goes into [the data inventory](../../../docs/data-protection/data-inventory.md) in the same PR, and
a column added to an audited table must also be in the twin (the parity test checks).
