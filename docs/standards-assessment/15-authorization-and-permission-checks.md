---
standard: 15 Authorization and Permission Checks
assessed: 2026-10-19
commit: e4b2b86
updated: 2026-10-20 (ServerlessWithOutbox, ADR 0007)
---
# 15 Authorization and Permission Checks — assessment

Default deny is in place: a Wolverine convention assigns a policy by route prefix, the fallback policy requires a signed-in
user, anonymous routes fail startup unless allowlisted, and both variants have an endpoint policy sweep and ownership 404
tests. The new `/internal/**` routes (Pub/Sub push, durability pass) sit outside the Wolverine convention, but each must carry
the internal policy its path calls for, or startup fails, and a sweep test checks them. The permission tests check member and
admin but have no population vocabulary.

**Score: Strong** · ✅ 10 · ⚠️ 1 · ❌ 0 · N/A 3

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | Every endpoint gets authorization by convention | MUST | ✅ | `api/BccCode.Template.Web/Http/EndpointConventions.cs:13-42` — `ConfigureEndpoints` calls `RequireAuthorization` on every chain without its own `[Authorize]`/`[AllowAnonymous]`; both variants call `UseTemplateConventions()` (`Api.Marten/Program.cs:27`, `Api.EfCore/Program.cs:24`). The `/internal/**` routes are minimal APIs mapped by `MapTemplateInternal` (`Internal/InternalEndpoints.cs`) and name their policy themselves; `InternalRouteGuard.AssertInternalRoutes` (called from `AssertTemplateRoutes`, `TemplateWeb.cs:65`) fails startup when one lacks it, so they are not left to memory either |
| 2 | Policy follows from location (path prefix) | MUST | ✅ | `EndpointConventions.cs:27-30` — `/api/admin` and `/api/admin/**` get `Admin`, the rest the default signed-in policy. Test: `EndpointConventionTests.The_policy_follows_from_the_route` (incl. `/api/administrators` not matching). Under `/internal`, `/internal/pubsub/**` requires `InternalPubSub` and `/internal/durability/**` `InternalScheduler`; any other internal path has no allowed policy (`InternalEndpoints.ExpectedPolicy`; test `InternalRouteGuardTests.The_policy_follows_from_the_route`) |
| 3 | Fallback policy requires an authenticated user | MUST | ✅ | `Auth/AuthConfiguration.cs:55-59` — default and fallback policy require an authenticated user with `sub`. Static files are served before authorization (`TemplateWeb.cs:40`), so the fallback does not cover them; see standard 16 rule 8 |
| 4 | One anonymous allowlist; startup fails on unlisted opt-out | MUST | ✅ | `Http/AnonymousRouteGuard.cs:13-39`, called last in both `Program.cs` via `AssertTemplateRoutes` (`TemplateWeb.cs:57-63`); each anonymous route says how it is protected (`AuthEndpoints.cs:23,37,54`). `[AllowAnonymous]` on an internal route also fails startup (`InternalRouteGuard`). Tests: `AnonymousRouteGuardTests` (3), `InternalRouteGuardTests.An_anonymous_internal_route_and_an_unknown_one_are_violations`, `SecurityTests.Only_the_allowlisted_routes_allow_anonymous_access` (both variants) |
| 5 | Empty policy configuration denies everyone | MUST | ✅ | `AuthConfiguration.cs:60-65` — no `Auth:AdminRole` gives `RequireAssertion(_ => false)`. Test: `SecurityTests.With_no_admin_role_configured_nobody_is_an_admin`. The internal policies deny everything in command-line tools and OpenAPI generation, and a missing `Internal:*` setting stops the host instead of opening the route (`Internal/InternalAuthConfiguration.cs:56-104`; tests `InternalAuthConfigurationTests.A_missing_setting_stops_the_host_and_names_the_key`) |
| 6 | Integration test sweeps `EndpointDataSource` for policies | MUST | ✅ | `SecurityTests.Every_api_endpoint_has_the_policy_its_route_requires_and_none_allows_anonymous` in both variant test projects, with a non-vacuous count guard. It covers `/api/**`; the `/user/**` routes are covered by the allowlist test only. `InternalRoutesTests.Every_internal_route_has_its_internal_policy_and_none_allows_anonymous_or_gets_the_user_policy` (both variants) sweeps `/internal/**`, pins the two expected routes and checks each carries exactly its internal policy |
| 7 | Host roles assert route separation at startup | MUST | N/A | One host role (the API), which serves `/api/**` and `/internal/**`; no processor host |
| 8 | Resource checks in `LoadAsync`/`Validate`, 404 when not visible | MUST | ✅ | `Features/Notes/NoteRules.cs` (`Visibility` → 404) used from `Validate` in `GetNote`, `EditNote`, `ArchiveNote` in both variants (e.g. `Api.Marten/Features/Notes/EditNote.cs:21-28`, `Api.EfCore/Features/Notes/EditNote.cs:23-27`). Tests: `NotesApiTests.Another_users_note_and_a_missing_note_both_answer_404_so_existence_is_not_revealed`, `NoteSliceTests.An_edit_checks_visibility_first_then_freshness_then_the_input` |
| 9 | Every write endpoint tested with unknown/foreign id → 404, nothing written | MUST | ✅ | `NotesApiTests.Every_write_to_an_unknown_or_foreign_note_answers_404_and_stores_nothing` in both variants (edit and archive, checks stream version / row unchanged). `CreateNote` has no resource id |
| 10 | Permission rules tested against the whole user population | MUST | ⚠️ | `SecurityTests.A_member_is_refused_the_admin_area_and_an_admin_is_let_in` checks member and admin for `/api/admin/stats`; anonymous is checked on `/api/notes` only. No population helper (`Only(...).And(...)`), so a new role or endpoint is not covered automatically |
| 11 | Multi-tenant data isolated by global query filter | MUST | N/A | Single-tenant sample |
| 12 | Server exposes computed permissions to the SPA | SHOULD | ✅ | `GET /api/me` returns roles (`Features/CurrentUser/GetMe.cs`); the SPA hides the admin link and shows a no-access page (E2E `admin.spec.ts` "a plain user gets the no-access page and no link to the statistics"), and the endpoint still enforces `Admin` |
| 13 | Admin access from IdP roles, not a person-id list | SHOULD | ✅ | `Auth:AdminRole` names a role claim from the identity provider (`appsettings.json` `"AdminRole": "admin"`, `AuthConfiguration.cs:54-65`) |
| 14 | Client-readable stores deny writes, rules tested | MUST | N/A | No Firestore or other client-readable store |

## Gaps to close

- **10**: Add a small population vocabulary to the test fixtures (anonymous, member, admin) and a theory that, for each
  protected route, asserts which personas get 2xx and that every other persona gets 401/403. Files:
  `api/tests/BccCode.Template.Api.*.Tests/SecurityTests.cs` and `Infrastructure/AppFixture.cs`. The `permission-tests` skill
  describes the shape.
- **6** (coverage): extend the sweep, or add a second test, so `/user/**` minimal routes that are not on the allowlist are
  asserted to carry an authorization requirement or rely on the fallback deliberately.

## Not verifiable in the repo

- Which role claim name and value BCC Signon emits for administrators, and who manages membership of that role. Check the
  identity provider configuration for the client.
