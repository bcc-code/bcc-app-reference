---
standard: 11 Domain Modelling and Errors
assessed: 2026-10-19
commit: e4b2b86
---
# 11 Domain Modelling and Errors — assessment

The note sample models its rules as pure functions over loaded data, uses `TimeProvider` everywhere with a test that bans
the instance clock, returns expected failures as ProblemDetails values and parses title and body into value types. The one
open point is that ids are plain `Guid`s rather than strong types.

**Score: Strong** · ✅ 9 · ⚠️ 1 · ❌ 0 · N/A 1

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | Domain terms for types, events, endpoints, routes | MUST | ✅ | `Note`, `NoteCreated`, `NoteEdited`, `NoteArchived`, `ArchiveNoteEndpoint`, `/api/notes/{noteId}/archive` in both variants. |
| 2 | Norwegian terms allowed, explained once | MAY/MUST | N/A | No Norwegian domain terms in the sample. |
| 3 | Decisions in pure static functions with explicit inputs | MUST | ✅ | `Features/Notes/NoteRules.cs:15-24` (`IsVisibleTo`, `Visibility`, `Freshness`) and `NoteValues.cs:52-60` in both variants; time comes in as a `TimeProvider` parameter of `Post`. Unit-tested by value in `NoteSliceTests` (both). |
| 4 | Event-sourced aggregate: state + Apply; EF entity: no non-trivial logic | MUST/SHOULD | ✅ | Marten `Features/Notes/Note.cs:16-39`: properties, `Create`, `Apply` only. EF Core `Features/Notes/Note.cs:35-45`: `Edit`/`Archive` set fields and bump `Version`; no `DbContext`, the rules are in `NoteRules`. |
| 5 | Events past tense, sealed records with positional members | MUST | ✅ | Marten `Note.cs:4-9`. Test `NoteSliceTests.Every_note_event_is_handled_by_the_note_aggregate` (Marten). |
| 6 | Multi-field or rule-bearing concepts as value records | SHOULD | ✅ | `NoteTitle`, `NoteBody`, `NoteContent` (`NoteValues.cs:8-61`), `NoteCounts` (`Admin/GetStats.cs:9`). |
| 7 | Current time from `TimeProvider` | MUST | ✅ | `TimeProvider.System` registered in `api/BccCode.Template.Web/TemplateWeb.cs:18`; injected in `CreateNote.cs`, `EditNote.cs`, `ArchiveNote.cs` and the seed commands. Tests use `FakeTimeProvider` (`NoteSliceTests.cs:12` Marten, and the EF Core equivalents). |
| 8 | Instance clock banned by an architecture test | MUST | ✅ | `api/tests/BccCode.Template.ArchitectureTests/ClockRulesTests.cs:6-10` scans IL of web and API assemblies for `System.*.UtcNow/Now/Today`, with a self-test (`The_scanner_detects_a_forbidden_call`). Ran with `--no-build`: passed. |
| 9 | Expected outcomes are values, not exceptions | MUST | ✅ | `Validate` returns `ProblemDetails` (`NoteRules.Visibility`, `Freshness`, `NoteContent.Parse`); `TryParse` returns the error key or null. The only exception paths are framework concurrency failures, mapped centrally (Marten `Program.cs:30-31`, EF Core `Infrastructure/ConcurrencyConflictHandler.cs`). |
| 10 | Wolverine chain is the railway; no `Result<T>` library | MUST | ✅ | No `Result<T>` type in the solution (grep); every slice short-circuits from `Validate`. |
| 11 | Ids and rule-bearing values parsed into strong types at the boundary | SHOULD | ⚠️ | Title and body are parsed into `NoteTitle`/`NoteBody` before `Post` (`CreateNote.cs:16,20`). Note ids are plain `Guid` in routes, records, events and entities; there is no `NoteId` type. |

## Gaps to close

- **11**: Introduce `readonly record struct NoteId(Guid Value)` in `Features/Notes/` (both variants), with the JSON converter
  and the EF Core value converter / Marten identity registration in one place, and use it in the slices, events and
  `NoteResponse`. As a template, this shows the pattern teams are expected to copy.

## Not verifiable in the repo

- None for this standard.
