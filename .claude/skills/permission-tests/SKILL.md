---
name: permission-tests
description: "Write or review authorization tests for this app: the endpoint policy sweep, the anonymous-route allowlist, PermissionConventionTests, admin-policy checks, ownership tests (403 for a foreign resource, 404 for an unknown or archived one) for write endpoints, and role/permission population tests. Use whenever the task adds or changes an endpoint, a policy, a role, a Permission method or an ownership rule, or reviews a permission test that checks only the allowed role, or when asked which test covers an access rule. Use it even when the request only says roles, permissions, access, default deny or 'can this user do X' without naming a test."
---

# Permission tests

Standard 15 (see the `bcc-standards` skill, reference 15) sets default deny: an endpoint is protected
unless someone deliberately opened it, and tests prove it. This skill is how to write those tests in
this template. Five kinds of test cover the rules:

| Test | Standard rule | Proves |
|---|---|---|
| `PermissionConventionTests` | 09.2, 15 | every endpoint class has a static `Permission` method, or is on a short allowlist with the reason |
| Endpoint policy sweep (`SecurityTests`) | 15.6 | every `/api` endpoint has the policy its prefix requires; only allowlisted routes allow anonymous access |
| Role/population test | 15.10 | for each rule, the allowed personas pass and every other persona is denied |
| Ownership test | 15.8, 15.9 | a foreign resource answers 403, an unknown or archived one 404, and a refused write changes nothing |
| Startup assertions | 15.4, 25.8 | an anonymous endpoint outside the allowlist, or an empty admin config, stops or denies at startup |

## Where things are

- `api/BccCode.Template.Api/Configuration/AnonymousRouteGuard.cs`: the anonymous allowlist and the startup assertion.
  `EndpointConventions.cs` there: the policy a route prefix requires.
- `api/tests/BccCode.Template.Tests/Configuration/PermissionConventionTests.cs`: unit test, no Docker.
- `api/tests/BccCode.Template.IntegrationTests/`: `SecurityTests.cs` (the sweep, admin and MFA checks), `NotesApiTests.cs`
  (ownership), `Infrastructure/TestSessions.cs` (`TestUser`, `scenario.As(user)`: a real session in the session store, so
  the policies run as in production), `Infrastructure/AppFixture.cs`.
- `api/tests/BccCode.Template.ArchitectureTests/`: slice shape.

Open the existing test for the rule first and extend it; do not add a second one. Pin what the code does, not what it should
do: when a rule looks wrong, write the test that matches the code and raise the question separately, because changing an access
rule is a product decision.

## Personas and test users

The OIDC mock has `user` (plain member), `user2` (a second member, for ownership and leak tests), `admin` (admin role, signs in
with a second factor) and `admin-no-mfa` (admin role without one: every `/api` call answers 401 with the step-up problem). The
population for a test is these plus the **anonymous** caller. In integration tests `TestUser.New()` makes a fresh person (so
tests sharing a database do not see each other's notes), `TestUser.NewAdmin()` an administrator, `TestUser.Of(Persona.User)` a
persona whose uid the seeder writes notes for. When a role is added, add a persona for it; population tests written as
"allowed set, everyone else denied" then cover it.

## Endpoint order and what each step answers

An endpoint runs `LoadAsync` → `Permission` → `Before` → `Validate` → `Post` (standard 09.2). `Permission` is pure and decides who:

- no such resource for anyone (unknown id, archived) → **404**, no body;
- the resource exists and the caller may not use it → **403** (`Forbidden`), no body;
- otherwise `null`, and the request goes on to `Before` (a stale version, 409) and `Validate` (business rules, 400).

So a caller who may not act never learns that a version is stale or that a body is invalid. Test that order: send a stale version
and an invalid body as the foreign user and expect 403, not 409 or 400. Responses for 403 and 404 are **bodiless**; assert it.

## PermissionConventionTests

`NoPermission` in that test lists the endpoint classes that have no `Permission` method, each with the reason (own data only, the
route prefix decides). A new endpoint fails the test until it has `Permission` or is listed with a reason. Prefer writing
`Permission`: a listed endpoint is a decision someone has to defend in review. A removed endpoint must leave the list.

## The endpoint policy sweep (15.6)

`SecurityTests` enumerates `EndpointDataSource` from the real host and asserts, for every `/api` endpoint, that it does not allow
anonymous access and carries the policy its prefix requires (`/api/admin/...` requires the admin policy, the rest an authenticated
person with a `personUid`). `Only_the_allowlisted_routes_allow_anonymous_access` checks the rest against `AnonymousRouteGuard.Allowed`:

- `/auth/login`, `/auth/mfa-required` and `/auth/backchannel-logout` (each protects itself: a local return URL, fixed text, a signed token);
- `/health` and `/alive`.

Nothing else is anonymous. There are no antiforgery token routes: CSRF is the cross-origin check, so a write test as another origin
is 403 (`CrossOriginProtectionTests`), not a missing-token case. There is no rate limiting to test. A new anonymous route goes on the
allowlist with a comment saying how it authenticates itself; a framework or dev-only route goes through `alsoAllowed`.

## Role/population tests (15.10)

A rule is a statement about all callers at once: *these* may, *the rest* may not. A test that checks only the allowed role misses a
rule that lets everyone in. Write each rule as "the allowed set, every other persona denied", with one helper that takes the
allowed callers and asserts both sides. Check whether one exists before writing it:

```csharp
// Sketch: adapt to the helpers in SecurityTests.
[Fact]
public async Task A_member_is_refused_the_admin_area_and_an_admin_with_a_second_factor_is_let_in()
{
    await Host.Scenario(s => { s.As(TestUser.New()); s.Get.Url("/api/admin/stats"); s.StatusCodeShouldBe(403); });
    await Host.Scenario(s => { s.As(TestUser.NewAdmin()); s.Get.Url("/api/admin/stats"); s.StatusCodeShouldBeOk(); });
}
```

Rules for these tests:

- `/api` routes answer **401** to anonymous callers and **403** to signed-in callers without the role. Never a redirect (14.12).
- Name the test after the rule or permission, not after the roles. Put the reason a persona is on the list in a one-line comment.
- When a role or policy is added, add it to the population. The test should fail until someone decides.

## Ownership tests (15.8, 15.9)

Endpoint-level policy lets a caller through when the resource does not exist or belongs to someone else, so each endpoint that takes
a resource id closes that gap in `Permission` (here `GET /api/notes/{noteId}`, `POST .../edit`, `POST .../archive`).
For each:

1. Call it with an **unknown** id as a signed-in user. Expect **404**, no body.
2. Create a resource as one user, then call it as **another user**. Expect **403**, no body. The resource exists and the caller may
   not use it (15.8); 404 is only for a resource whose existence is itself personal data, which a note is not.
3. Call it with an **archived** resource, where the rules say archived counts as missing. Expect **404**.
4. After a rejected write, assert that **nothing was written**: the row is unchanged, the version did not move and no row was
   added (`Every_write_to_a_foreign_note_answers_403_and_to_an_unknown_one_404_and_stores_nothing`
   in `NotesApiTests` is the model).

A 409 for a stale version and a 400 for an invalid body are separate tests, not part of the ownership rule.

## Startup assertions (15.4, 15.5)

- A policy whose configuration is empty must **deny everyone** (an admin policy with no admin role): see
  `With_no_admin_role_configured_nobody_is_an_admin`, which starts the host with `Auth:AdminRole` empty.
- An endpoint that opts out of authorization without being on the allowlist must fail startup. Test the guard directly with a fake
  endpoint that is not on the list (`AnonymousRouteGuardTests`).
- A missing OIDC authority or client id must fail startup outside Development and Test (14.5).

## Anti-patterns

| Don't | Why |
|---|---|
| Assert only that the allowed persona gets 200 | A rule that lets everyone in still passes. |
| One test per persona with hand-written denial lists | The population helper asserts the complement; hand-written lists rot as roles are added. |
| Return 404 for a resource that exists and belongs to someone else | Standard 15.8 says 403. Use 404 only when existence is itself personal data, and say so in a comment. |
| Return a body with a 403 or 404 | They are bodiless (09.10); a body is a place to leak the reason. |
| Test the ownership rule only through the UI | Slow, and a UI that hides the button is not a control. |
| Skip the "nothing was written" assertion | A refused request that still wrote is a worse bug than a 200. |
| Add a policy exception to make a test pass | Fix the endpoint or add the route to the allowlist with a comment that says why it is public. |
| Put a business rule in `Permission` | `Permission` says who may; `Before` and `Validate` say what is allowed to change. |

## Checklist

- The new or changed endpoint has a `Permission` method (or a reasoned entry in `PermissionConventionTests`) and is covered by the
  policy sweep (no edit needed if it follows the convention).
- Each permission has a population test with the allowed set and everyone else denied.
- Each endpoint with a resource id has unknown-id (404), foreign-owner (403), archived (404) and nothing-written tests, and 403 and 404 are bodiless.
- Anonymous endpoints are on the allowlist with a comment saying how they authenticate themselves.
- Run `dotnet test --project api/tests/BccCode.Template.Tests` (convention tests), the integration tests (Docker) and the
  architecture tests.
