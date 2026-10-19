---
standard: 09 Endpoints and API Design
assessed: 2026-10-19
commit: e4b2b86
---
# 09 Endpoints and API Design — assessment

Both variants follow the endpoint shape closely: one static Wolverine.HTTP class per operation, `LoadAsync` → `Validate` →
`Post`, task routes, per-endpoint sealed records and a shared ProblemDetails helper with a stable `type` and a `key`. The
remaining points are small: no 503 mapping for a database outage, validation without FluentValidation, and one non-ProblemDetails
error body in the back-channel logout route.

**Score: Strong** · ✅ 9 · ⚠️ 3 · ❌ 0 · N/A 1

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | Wolverine.HTTP static class per operation, one per file | MUST | ✅ | `Features/Notes/{CreateNote,EditNote,ArchiveNote,GetNote,ListNotes}.cs`, `Features/Admin/GetStats.cs`, `Features/CurrentUser/GetMe.cs` in both variants. No controllers. Test `SliceConventionTests.Endpoints_are_static_classes` (ran `dotnet test --project api/tests/BccCode.Template.ArchitectureTests --no-build`: 14 passed). |
| 2 | Compound handler: `LoadAsync` → `Validate` → `Post` → `After` | MUST | ✅ | e.g. Marten `Features/Notes/EditNote.cs:21-41`, EF Core `Features/Notes/EditNote.cs:23-40`. `Post` returns the response plus `IMartenOp`/`IStorageAction`; nothing calls `SaveChanges`. The EF Core `Post` mutates the loaded entity (`note.Edit(...)`) before returning `Storage.Update`, which is still testable without mocks (`NoteSliceTests.An_edit_changes_the_note_and_moves_the_version_on`). |
| 3 | `[NotBody]` on loaded params, `[EmptyResponse]` when no body | MUST | ✅ | `ArchiveNote.cs:23,26` (both variants); `[NotBody]` on every loaded parameter in the note slices and `GetStats.cs`. Committed document: archive answers `204` only. |
| 4 | Task routes, not generic resource PUT | MUST | ✅ | `/api/notes/{noteId}/archive` (`ArchiveNote.cs:23`), `/api/notes/{noteId}/edit` (`EditNote.cs:30`/`:32`). |
| 5 | One route style: `/api/`, kebab-case, plural, ids in route | MUST | ✅ | All routes in `api/openapi/openapi.json`: `/api/notes`, `/api/notes/{noteId}`, `/api/admin/stats`, `/api/me`. The auth routes (`/user/login`, `/user/antiforgery-token`, `AuthEndpoints.cs:21-67`) are outside `/api` and excluded from the document. |
| 6 | Public ids are GUIDs (UUIDv7) | MUST | ✅ | `Guid.CreateVersion7(now)` in `CreateNote.cs:23` (both); seed ids are v7-shaped (`SeedNotes.cs`). |
| 7 | Own sealed-record request/response; never entities or documents | MUST | ✅ | `CreateNote`, `EditNote`, `ArchiveNote`, `NoteResponse`, `StatsResponse`, `MeResponse` are sealed records. Test `SliceConventionTests.Endpoints_return_sealed_records_and_not_stored_types`; `NoteSliceTests.The_response_does_not_expose_the_owner` (both). |
| 8 | Body-less `PUT`/`DELETE` answer 204 | MUST | N/A | No `PUT` or `DELETE` endpoint exists. The body-less task route answers 204 (`NotesApiTests.Archiving_answers_204_and_hides_the_note_from_its_owner`, both). |
| 9 | FluentValidation nested in endpoint; messages are i18n keys | SHOULD/MUST | ⚠️ | No FluentValidation; input is parsed into value types in `Validate` (`NoteValues.cs:52-60`), which also meets standard 11 rule 11. Messages are i18n keys (`errors.note.titleRequired` etc.), but string literals, not generated `LanguageKeys` (see 08 rule 11). |
| 10 | Every error is RFC 9457 ProblemDetails | MUST | ⚠️ | `/api` errors are ProblemDetails: `Problems.cs`, `AddProblemDetails` + `UseStatusCodePages` (`TemplateWeb.cs:19,38`), CSRF (`AntiforgeryMiddleware.cs:33`), 429 (`RateLimiting.cs:54-58`). `POST /user/backchannel-logout` returns `{ "error": "invalid_request" }` (`AuthEndpoints.cs:58,62`). OIDC Back-Channel Logout requires that body, but the exception is not noted at the code. |
| 11 | Stable URI `type`, i18n key in `key`, no personal data in `detail` | MUST | ✅ | `Problems.cs:13,55-60` derives `type` (`urn:bcc:template:<key>`) from the key; `detail` never set. Tests `HttpConventionTests.A_problem_has_a_stable_urn_type_and_the_key_as_an_extension`, `A_problem_never_carries_detail`, `Problems_written_by_the_framework_get_a_type_and_key_too`. |
| 12 | Status codes: 400/422, 401/403, 404, 409, 429, 503 | MUST | ⚠️ | 401 not redirect (`SecurityTests.Without_a_session_an_api_call_answers_401_and_not_a_redirect`), 404 for foreign notes (`NotesApiTests.Another_users_note_and_a_missing_note_both_answer_404...`), 409 mapped centrally (Marten `Program.cs:30-31`, EF Core `ConcurrencyConflictHandler.cs`), 429 with `Retry-After` (`SecurityTests.The_anonymous_routes_are_rate_limited_with_a_retry_after_and_a_problem`). No 503: a database outage during a request surfaces as an exception and answers 500. |
| 13 | Exceptions only for unexpected; one central mapping, no message text | MUST | ✅ | `UseExceptionHandler()` (`TemplateWeb.cs:37`) with `Problems.Customize` (no `detail`); expected outcomes come from `Validate`. No `ArgumentException`-to-400 mapping anywhere (grep). |

## Gaps to close

- **12**: Map database-unavailable exceptions (`NpgsqlException` with a connection failure, `TimeoutException` on open) to a
  `503` ProblemDetails in one `IExceptionHandler` in `api/BccCode.Template.Web/Http/`, with a test.
- **10**: Add a comment at `api/BccCode.Template.Web/Auth/AuthEndpoints.cs:58,62` citing OIDC Back-Channel Logout 1.0 section
  2.8 as the reason for the non-ProblemDetails body.
- **9**: Either adopt FluentValidation with the Wolverine ProblemDetails middleware for request-shape rules, or record that
  boundary parsing into value types replaces it (an ADR or a note in `docs/adrs/0005-wolverine-http-and-vertical-slices.md`).
  Move the keys to `LanguageKeys` (08 rule 11).
- Contract accuracy (related to 08 rule 6): the committed document declares `404` on `GET /api/me`, `GET /api/notes` and
  `GET /api/admin/stats`, which cannot return it, and no endpoint declares `401`, `403` or `429`. The generated client's
  error types follow the document.

## Not verifiable in the repo

- What a client sees when Cloud SQL is down in a deployed environment (rule 12): check the Cloud Run logs and the response
  of `/api/notes` during a planned database restart.
