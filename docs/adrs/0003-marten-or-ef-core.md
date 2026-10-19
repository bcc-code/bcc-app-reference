# ADR 0003: Marten or EF Core: how to choose

- Status: Accepted
- Date: 2026-10-19

## Context

BCC applications on Postgres use either Marten (documents and event sourcing) or EF Core (relational). The template carries
both, with the same Notes sample and the same HTTP contract, so that a new application can start from the one that fits and
delete the other. An application uses one of them. Mixing both in one application is not a goal.

## Decision

Choose when the application is created, with `scripts/init-app.ps1 -Persistence marten|efcore`.

Choose **Marten** when:

- the history of changes matters (audit, "what did this look like then", undo, replay), so storing events is a feature;
- the domain is a set of aggregates with behaviour, and reads are projections built for the screens that need them;
- you want optimistic concurrency per aggregate through the stream version, and messaging integration through Wolverine's
  transactional outbox.

Choose **EF Core** when:

- the data is relational and is queried across entities in many ways (reporting, exports, ad hoc filters, joins);
- the team or the tooling around the application (BI, SQL access, other services reading the tables) expects ordinary tables;
- the domain is mostly create, read, update and delete, and a history of events would only be extra work.

If both answers fit, pick EF Core for the simpler operational story, and Marten when audit or replay is a stated requirement.
Do not use Marten as a document store only to avoid writing migrations: it still has a schema and still needs the migration step.

Both variants run schema changes as a separate step before the API starts (standard 12), never at startup.

## Choice made for this application

<!-- template:persistence-choice -->
Not made yet: this is the template, which contains both variants. `scripts/init-app.ps1` replaces this line with the choice.

## Consequences

| | Marten | EF Core |
|---|---|---|
| Write model | Events in streams, inline projection for reads | Tables and entities |
| Concurrency | Stream version | Integer `Version` column (EF concurrency token) |
| Audit trail | Events are the history | Needs an audit table or triggers |
| Reporting | Query projections or export to a read store | Plain SQL |
| Migrations | Marten schema management, run as a job | EF migrations, run as a job |
| Learning curve | Event modelling, projections | Familiar to most .NET developers |

The init script removes the project and the test project of the variant that is not chosen. The chosen variant keeps its code
unchanged except for the rename to `BccCode.<Name>.Api`.

## Alternatives considered

- Marten only: forces event sourcing on domains that do not benefit from it.
- EF Core only: loses the audit and replay story that some BCC systems need.
- A runtime switch inside one application: doubles the code paths to test and keeps both stacks as dependencies. The
  `Template:Persistence` setting in the AppHost exists only so that both samples can be run from the same template.
