---
standard: 10 Vertical Slices and CQRS
assessed: 2026-10-19
commit: e4b2b86
updated: 2026-10-20 (ServerlessWithOutbox, ADR 0007)
---
# 10 Vertical Slices and CQRS — assessment

The slice layout, static endpoints, side effects as return values and one transaction per request are in place in both
variants and enforced by architecture tests. Gaps: the API test projects do not mirror the feature folders, the Marten
projection is registered outside its feature, and neither the Marten projection nor dead letters can be operated from an
admin endpoint. The archive slice now cascades an integration message through the outbox (ADR 0007), and the architecture
test checks every endpoint and handler return type.

**Score: Adequate** · ✅ 5 · ⚠️ 2 · ❌ 1 · N/A 4

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | One folder per capability, one slice per file | MUST | ✅ | `Features/Notes/`, `Features/Admin/`, `Features/CurrentUser/` in both variants, one endpoint per file. Test `SliceConventionTests.Every_endpoint_class_lives_in_a_feature_folder`. |
| 2 | Tests and frontend mirror feature folders | MUST | ⚠️ | Frontend mirrors: `frontend/src/features/notes/`, `features/admin/`, `features/session/`. The API test projects are flat: `api/tests/BccCode.Template.Api.Marten.Tests/NoteSliceTests.cs`, `NotesApiTests.cs` (same in EF Core), with no `Notes/` or `Admin/` folder. |
| 3 | Everything a feature needs lives in its folder | MUST | ⚠️ | EF Core: entity, `NoteConfiguration.cs`, rules and values are in `Features/Notes/`; `TemplateDbContext.cs:18-19` picks the configuration up by assembly scan. Marten: the snapshot projection is registered in `Configuration/TemplateStore.cs:21`, not in the feature. |
| 4 | Slice id in a doc comment where a spec exists | SHOULD | N/A | No event model or spec in the repo (`docs/specs/README.md` only). |
| 5 | Static endpoints, no `*Service`/`*Repository`, test enforces | MUST | ✅ | `SliceConventionTests.Endpoints_are_static_classes`, `No_services_or_repositories` (ran the architecture tests with `--no-build`: 14 passed). |
| 6 | Handlers return side effects as values | MUST | ✅ | Marten returns `IStartStream`/`IMartenOp` (`CreateNote.cs:20`, `EditNote.cs:34`, `ArchiveNote.cs:26`); EF Core returns `IStorageAction<Note>` (`CreateNote.cs:20`, `EditNote.cs:36`, `ArchiveNote.cs:27`). The archive slice returns the message as a value next to the storage action (`(IMartenOp, NoteArchivedMessage)`, `(IStorageAction<Note>, NoteArchivedMessage)`). Asserted without mocks in `NoteSliceTests` (both; Marten `Archiving_appends_an_event_and_hands_the_message_to_the_outbox`). |
| 7 | One transaction per request; messages after commit | MUST | ✅ | `options.Policies.AutoApplyTransactions()` in Marten `WolverineConfiguration.cs:35` and EF Core `:42`. Under `ServerlessWithOutbox` a cascaded message is written to the outbox in the same transaction and sent after the commit (`api/BccCode.Template.Web/Messaging/TemplateMessaging.cs`). Tests: `MessagingTests.An_archived_note_is_published_and_the_pushed_message_is_handled`, `A_refused_archive_publishes_nothing` (both variants). |
| 8 | Non-transactional work in after-commit hook | MUST | N/A | No work outside the transaction: the message goes through the outbox, and there are no Cloud Tasks or scheduling (scheduled and delayed messages are banned by `SliceConventionTests.Nothing_schedules_delays_or_uses_local_queues`). |
| 9 | Cascaded messages routed explicitly; architecture test on returns | MUST | ✅ | Only `IIntegrationMessage` types are routed, to one Pub/Sub topic, and conventional local routing is off (`Messaging/TemplateMessaging.cs:38,55`). `SliceConventionTests.Every_cascaded_message_is_an_integration_message` (`SliceConventionTests.cs:74-91`) walks every HTTP endpoint and every `*Handler` method and fails on any returned type that is not a side effect or an integration message, and refuses `OutgoingMessages` because its content cannot be checked; `Handlers_exist_so_the_rule_below_is_not_vacuous` guards it. Handlers are found by the `*Handler` class name, so a handler class named otherwise is not checked. |
| 10 | Each role asserts it only does its own job | MUST | N/A | One serving role (the API), which serves both `/api/**` and `/internal/**`. The migrate, seed and codegen steps are CLI commands that exit (`MigrateCommand.cs`, `SeedCommand.cs`), not listeners, and run with the Pub/Sub transport disabled (`CodeGeneration.IsCommandLineTool()`, `WolverineConfiguration.cs`). |
| 11 | Marten→Wolverine event forwarding in one place, tested once | MUST | N/A | No event forwarding configured (`MartenConfiguration.cs:28-38` has `IntegrateWithWolverine()` only, no `PublishEventsToWolverine`). |
| 12 | Dead letters and projection rebuilds operable by admins, with runbook | MUST | ❌ | Dead letters now exist (Pub/Sub dead-letter topic and Wolverine's `wolverine_dead_letters` table). They have a runbook (`docs/runbooks/pubsub-dead-letters.md`) but no admin-only UI or endpoint: replay is done with `gcloud`, and Wolverine's own dead letters have no replay path at all. The Marten variant has a snapshot projection (`TemplateStore.cs:21`) with no rebuild endpoint, command or runbook. `docs/runbooks/README.md:18-19` says a rebuild runbook is not needed because the projection is inline; an inline snapshot still has to be rebuilt when an `Apply` method changes. |

The delivery style is a durable outbox without CPU between requests (`DurabilityMode.ServerlessWithOutbox`), decided in
[ADR 0007](../adrs/0007-wolverine-serverless-with-outbox.md): the outbox row is written in the request's transaction, sent
inline after the commit, and a failed send is picked up by the durability pass that Cloud Scheduler calls every minute. This
sits between the two styles the standard names: it keeps the outbox guarantee of the first and the request-only CPU of the
second, without a reconcile job. `LayerRulesTests.Nothing_runs_in_the_background` still keeps hosted services out.

## Gaps to close

- **12**: Add admin-only endpoints under `/api/admin/` to list, replay and discard dead letters (Wolverine's table at least;
  the Pub/Sub dead-letter topic can stay with the runbook if an ADR says so). Pair with standard 13 rule 11.
- **12**: For the Marten variant, add an admin-only rebuild path for the `Note` snapshot (an endpoint under `/api/admin/` or a
  JasperFx command run as a Cloud Run job), a runbook `docs/runbooks/projection-rebuild.md`, and correct
  `docs/runbooks/README.md:18-19`. Pair with standard 12 rule 14.
- **2**: Move the API tests into feature folders: `api/tests/BccCode.Template.Api.*.Tests/Notes/` (slice and API tests),
  `Admin/`, `CurrentUser/`; keep `CsrfTests`, `SecurityTests`, `PersistenceTests` under a cross-cutting folder.
- **3**: Register the `Note` snapshot from the feature (for example a `NotesStore.Configure(StoreOptions)` in
  `Features/Notes/` called by `TemplateStore.Configure`), so `Configuration/TemplateStore.cs` holds only shared registrations.

## Not verifiable in the repo

- None for this standard.
