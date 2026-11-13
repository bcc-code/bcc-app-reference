# 0028. Relational domain data with typed history

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Ported from bcc-ateam ADR 0039 (its status there is Proposed; the template adopts it as Accepted). The design details are in bcc-ateam's specs `relational-domain-data.md` and `relational-pilot.md`; they are not copied here.

## Context

An earlier version of the template had a Marten variant (events and projections) and an EF Core variant, and `init-app` kept one. The BCC A-team decided not to use event sourcing or other less common technology for its applications: the app is maintained by a team, and increasingly by AI agents, over many years, and the people involved should not first have to learn event sourcing. EF Core and SQL are what most developers, and the models, know. The template is not deployed anywhere, so the switch has no data to migrate.

The requirements that made event sourcing attractive still hold:

- every change is a named business step, with who, when and an optional reason; some steps only record a fact;
- a timeline per business object, and roles and assignments over time;
- erasure of personal data when its retention ends;
- rules the database can enforce are enforced there.

Postgres has no system-versioned tables, and EF Core supports temporal tables only on SQL Server. The `temporal_tables` extension stamps database time, so it cannot take the app's clock or the seeder's historical times, and it does not record who made a change.

## Decision

We will store domain data in ordinary relational tables through EF Core and Npgsql, with one typed history table per audited table and one table of business steps. The template has no Marten.

- **Business steps.** `change` has one row per step: type (`NoteArchived`), actor, time from `TimeProvider` ([0029](0029-timeprovider-as-the-clock.md)), reason, and the object it was about. The actor is an alias for the person's personUid ([0019](0019-bff-sign-in-with-bcc-signon.md)), so erasing a person means clearing one mapping. A step that changes no row is a change on its own.
- **Domain tables.** Ordinary tables with constraints for rules no process may break (foreign keys, partial unique indexes, exclusion constraints on validity periods). Each audited row carries `change_id` and `version`. Enums are stored as text.
- **History tables.** `<table>_history`, in the same schema as its table (a preview has a schema of its own, [0027](0027-pr-previews-in-a-preview-project.md)), has the same columns plus `replaced_at` and `replaced_by`. One trigger function, `history_track()`, copies the old row on update and refuses an update that does not point to a new change or skips a version. It also catches raw SQL and `ExecuteUpdate`. Migrations call `history_sync('<table>')` to create or extend the history table and attach the trigger. A rename or type change is written for both tables, so history moves with the data. In the sample: `note` and `note_history`.
- **Passing the change.** A `Change` object is built once per HTTP request (a parameter strategy) or, in an application that needs one, per message handler run (a middleware; the template has none yet, and its only handler writes no row). Endpoints pass it to entity methods, which call `Record(change, "<Step>")`. There are no interceptors and no session variables. One change has one step name.
- **Stale edits.** `version` is an EF concurrency token. A mismatch answers 409.
- **Removal and erasure.** The app role cannot delete domain rows. A business removal sets `removed_at`, hidden by an EF query filter. Erasure is a scheduled job that calls a stored procedure which deletes the object's rows, their history and their changes in one transaction.
- **Messages.** Wolverine stays, with its EF Core transactions and outbox ([0024](0024-wolverine-serverless-with-outbox-and-pubsub-push.md)). Messages commit with the data and the change.
- **Other stores.** Sessions ([0020](0020-server-side-sessions-with-back-channel-logout.md)) and Data Protection keys ([0021](0021-data-protection-keys-in-postgres-with-kms.md)) are EF Core tables without history, so the app has one kind of storage.
- **Schema.** `jasper db-apply` runs the EF migrations ([0016](0016-schema-applied-by-a-deploy-step.md)), and the unit test `MigrationTests` fails when the model has changes no migration covers.

## Consequences

| | |
|---|---|
| Positive | The current data is in tables anyone can query, and one diagram of tables and foreign keys shows the model. Every change, including raw SQL, is recorded in typed history with its step and actor. Renames and type changes are ordinary migrations. Erasure is a delete. The custom code is small: about 85 lines of SQL and 80 lines of C#, plus tests. |
| Negative | Two tables per audited entity; a rename or type change is written for both. Past events cannot be replayed to build new views. Who deleted a row is only recorded for soft deletes, so hard deletes are limited to erasure. One change has one step name, so a request that is two steps is split. |
| Follow-up | The template ships the parity and trigger tests, not yet the rights (no delete for the app role) or the erasure procedure and its tests; an application adds those with its first retention rule. Decide per application: the read log, history visibility for readers, and retention for changes with no object. |

## Alternatives considered

- **Keep event sourcing (Marten).** It meets the requirements, and a fixed projection can be rebuilt from events. Rejected because the team does not want to depend on event sourcing for long-term maintenance.
- **One revision table with the row as JSON, an action table set through a session variable, and an EF Core interceptor.** Rejected: old revisions keep old column names, and the interceptor and session variable hide how a change is recorded.
- **The `temporal_tables` extension.** Rejected: it stamps database time and does not check that a change was recorded. Its idea of storing when a version ended is used here.
- **SQL Server temporal tables with EF Core's `IsTemporal()`.** No custom history code, but it would mean leaving the platform's shared Cloud SQL Postgres instance ([0015](0015-shared-platform-postgres-with-iam-auth.md)).
