---
standard: 10 Vertical Slices and CQRS
assessed: 2026-10-19
commit: e4b2b86
---
# 10 Vertical Slices and CQRS — assessment

The slice layout, static endpoints, side effects as return values and one transaction per request are in place in both
variants and enforced by architecture tests. Gaps: the API test projects do not mirror the feature folders, the Marten
projection is registered outside its feature, and the Marten projection cannot be rebuilt by an operator. The messaging rules
mostly do not apply because the sample sends no messages.

**Score: Adequate** · ✅ 5 · ⚠️ 2 · ❌ 1 · N/A 4

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | One folder per capability, one slice per file | MUST | ✅ | `Features/Notes/`, `Features/Admin/`, `Features/CurrentUser/` in both variants, one endpoint per file. Test `SliceConventionTests.Every_endpoint_class_lives_in_a_feature_folder`. |
| 2 | Tests and frontend mirror feature folders | MUST | ⚠️ | Frontend mirrors: `frontend/src/features/notes/`, `features/admin/`, `features/session/`. The API test projects are flat: `api/tests/BccCode.Template.Api.Marten.Tests/NoteSliceTests.cs`, `NotesApiTests.cs` (same in EF Core), with no `Notes/` or `Admin/` folder. |
| 3 | Everything a feature needs lives in its folder | MUST | ⚠️ | EF Core: entity, `NoteConfiguration.cs`, rules and values are in `Features/Notes/`; `TemplateDbContext.cs:18-19` picks the configuration up by assembly scan. Marten: the snapshot projection is registered in `Configuration/TemplateStore.cs:21`, not in the feature. |
| 4 | Slice id in a doc comment where a spec exists | SHOULD | N/A | No event model or spec in the repo (`docs/specs/README.md` only). |
| 5 | Static endpoints, no `*Service`/`*Repository`, test enforces | MUST | ✅ | `SliceConventionTests.Endpoints_are_static_classes`, `No_services_or_repositories` (ran the architecture tests with `--no-build`: 14 passed). |
| 6 | Handlers return side effects as values | MUST | ✅ | Marten returns `IStartStream`/`IMartenOp` (`CreateNote.cs:20`, `EditNote.cs:34`, `ArchiveNote.cs:26`); EF Core returns `IStorageAction<Note>` (`CreateNote.cs:20`, `EditNote.cs:36`, `ArchiveNote.cs:27`). Asserted without mocks in `NoteSliceTests` (both). |
| 7 | One transaction per request; messages after commit | MUST | ✅ | `options.Policies.AutoApplyTransactions()` in Marten `WolverineConfiguration.cs:29` and EF Core `:36`. No outgoing messages exist. |
| 8 | Non-transactional work in after-commit hook | MUST | N/A | No work outside the transaction (no Cloud Tasks, no scheduling). |
| 9 | Cascaded messages routed explicitly; architecture test on returns | MUST | ✅ | `SliceConventionTests.Every_cascaded_value_is_a_side_effect_and_never_a_bare_message` walks every HTTP endpoint. There are no message handlers yet; the test covers only HTTP endpoint methods, so it needs extending when handlers appear. |
| 10 | Each role asserts it only does its own job | MUST | N/A | One serving role (the API). The migrate and seed steps are CLI commands that exit (`MigrateCommand.cs`, `SeedCommand.cs`), not listeners. |
| 11 | Marten→Wolverine event forwarding in one place, tested once | MUST | N/A | No event forwarding configured (`MartenConfiguration.cs:28-38` has `IntegrateWithWolverine()` only, no `PublishEventsToWolverine`). |
| 12 | Dead letters and projection rebuilds operable by admins, with runbook | MUST | ❌ | No dead letters (no messaging). The Marten variant has a snapshot projection (`TemplateStore.cs:21`) with no rebuild endpoint, command or runbook. `docs/runbooks/README.md:16-17` says a rebuild runbook is not needed because the projection is inline; an inline snapshot still has to be rebuilt when an `Apply` method changes. |

The delivery-style decision (durable outbox or serverless with reconcile) is postponed until the app sends messages; both
`WolverineConfiguration.cs` files say so in a comment (Marten `:26-28`, EF Core `:29-31`). `LayerRulesTests.Nothing_runs_in_the_background`
keeps hosted services out until then.

## Gaps to close

- **12**: For the Marten variant, add an admin-only rebuild path for the `Note` snapshot (an endpoint under `/api/admin/` or a
  JasperFx command run as a Cloud Run job), a runbook `docs/runbooks/projection-rebuild.md`, and correct
  `docs/runbooks/README.md:16-17`. Pair with standard 12 rule 14.
- **2**: Move the API tests into feature folders: `api/tests/BccCode.Template.Api.*.Tests/Notes/` (slice and API tests),
  `Admin/`, `CurrentUser/`; keep `CsrfTests`, `SecurityTests`, `PersistenceTests` under a cross-cutting folder.
- **3**: Register the `Note` snapshot from the feature (for example a `NotesStore.Configure(StoreOptions)` in
  `Features/Notes/` called by `TemplateStore.Configure`), so `Configuration/TemplateStore.cs` holds only shared registrations.

## Not verifiable in the repo

- None for this standard.
