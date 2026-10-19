---
name: permission-tests
description: "Write or review authorization tests for this app: the endpoint policy sweep, the anonymous-route allowlist, admin-policy checks, ownership 404 tests for write endpoints, and role/permission population tests. Use whenever the task adds or changes an endpoint, a policy, a role or an ownership rule, or reviews a permission test that checks only the allowed role, or when asked which test covers an access rule. Use it even when the request only says roles, permissions, access, default deny or 'can this user do X' without naming a test."
---

# Permission tests

Standard 15 (see the `bcc-standards` skill, reference 15) sets default deny: an endpoint is protected
unless someone deliberately opened it, and tests prove it. This skill is how to write those tests in
this template. Four kinds of test cover the rules:

| Test | Standard rule | Proves |
|---|---|---|
| Endpoint policy sweep | 15.6 | every endpoint has the policy its location requires; only allowlisted endpoints allow anonymous access |
| Role/population test | 15.10 | for each rule, the allowed personas pass and every other persona is denied |
| Ownership test | 15.9 | writing to an unknown or foreign resource id returns 404 and writes nothing |
| Startup assertions | 15.4, 25.8 | an anonymous endpoint outside the allowlist, or an empty admin config, stops or denies at startup |

## Status of the helpers

The shared plumbing (default-deny conventions, admin policy, anonymous-route allowlist guard) is in
`api/BccCode.Template.Web/`. The test helpers (host fixture, persona sign-in, endpoint enumeration)
are in `api/tests/`. **This skill was written before those helper names were final, so the code
below is a sketch.** Before writing a test, open the existing tests in `api/tests/BccCode.Template.Api.Marten.Tests/`,
`api/tests/BccCode.Template.Api.EfCore.Tests/` and `api/tests/BccCode.Template.ArchitectureTests/`, and
copy their fixture and helper names. If a test for the rule already exists, extend it rather than adding a second one.

Both persistence variants expose the same HTTP contract, so a permission test should pass against both.
Put shared test logic where both test projects can use it, or write the same test in both.

## Personas

The OIDC mock has `user` (plain member), `user2` (a second member, for ownership and leak tests) and
`admin` (has the admin role). The population for a test is these three plus the **anonymous** caller.
When a role is added, add a persona for it and every population test then covers it automatically if
the test is written as "allowed set, everyone else denied".

## The endpoint policy sweep (15.6)

One integration test enumerates `EndpointDataSource` from the real host and asserts, for every endpoint:

- an endpoint that is not on the anonymous allowlist carries an authorization requirement, and the
  policy matches where it lives (`/api/admin/...` requires the admin policy, the rest require an authenticated user);
- an endpoint that allows anonymous access is on the explicit allowlist (login, logout, back-channel
  logout, antiforgery token), and nothing else is.

```csharp
// Sketch. Replace the host fixture and policy lookup with the ones in the existing tests.
[Fact]
public void Every_endpoint_has_the_policy_its_location_requires()
{
    var endpoints = Host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();

    foreach (var endpoint in endpoints)
    {
        var route = endpoint.RoutePattern.RawText!;
        var policy = PolicyOf(endpoint);                  // from IAuthorizeData / IAllowAnonymous metadata

        if (AnonymousAllowlist.Contains(route)) { policy.Should().Be(Policy.Anonymous, route); continue; }

        var required = route.StartsWith("/api/admin/") ? Policy.Admin : Policy.Authenticated;
        policy.Should().Be(required, route);
    }
}
```

A new endpoint in the admin area is admin-only by convention; the test fails when someone adds an
endpoint outside the convention, or opts out of it without being on the allowlist. A framework
route that is not ours (health, OpenAPI) belongs on a short, commented exclusion list in the same test.

## Role/population tests (15.10)

A rule is a statement about all callers at once: *these* may, *the rest* may not. A test that checks
only the allowed role misses a rule that lets everyone in. Write each rule as "the allowed set, every
other persona denied". Use one helper that takes the allowed personas and asserts both sides:

```csharp
// Sketch. The helper below is what to write once and reuse; check whether one already exists.
static readonly Persona[] Population = [Anonymous, User, User2, Admin];

static async Task AssertOnlyTheseMay(Func<Persona, Task<int>> call, params Persona[] allowed)
{
    foreach (var persona in Population)
    {
        var status = await call(persona);
        if (allowed.Contains(persona)) status.Should().BeInRange(200, 299, $"{persona.Name} should be allowed");
        else status.Should().BeOneOf([401, 403], $"{persona.Name} should be denied");
    }
}

[Fact]
public Task Admin_stats_is_admin_only() =>
    AssertOnlyTheseMay(p => Get(p, "/api/admin/stats"), Admin);

[Fact]
public Task Listing_notes_needs_a_signed_in_user() =>
    AssertOnlyTheseMay(p => Get(p, "/api/notes"), User, User2, Admin);
```

Rules for these tests:

- `/api` routes answer **401** to anonymous callers and **403** to signed-in callers without the role. Never a redirect (14.12).
- Name the test after the rule or permission, not after the roles. Put the reason a persona is on
  the list in a one-line comment; the code already says who.
- When a role or policy is added, add it to the population. The test should fail until someone decides.
- Pin what the code does, not what it should do. When a rule looks wrong, write the test that matches
  the code and raise the question separately; changing an access rule is a product decision.

## Ownership tests (15.8, 15.9)

Endpoint-level policy often lets a caller through when the resource does not exist, or exists but
belongs to someone else. Each write endpoint must close that gap itself. For every endpoint that takes
a resource id (here: `GET /api/notes/{noteId}`, `POST .../edit`, `POST .../archive`):

1. Call it with an **unknown** id, as `user`. Expect **404**.
2. Create a resource as `user`, then call it as **`user2`**. Expect **404**, not 403: do not reveal that the resource exists.
3. Call it with an **archived** resource where the rules say archived counts as missing. Expect **404**.
4. After a rejected write, assert that **nothing was written**: the resource is unchanged, no event was appended (Marten), no row changed (EF Core), and the version did not move.

```csharp
// Sketch.
[Fact]
public async Task Editing_another_users_note_returns_404_and_changes_nothing()
{
    var note = await CreateNote(User, "Mine", "body");

    var response = await Post(User2, $"/api/notes/{note.Id}/edit", new { title = "Taken", body = "x", version = note.Version });

    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await GetNote(User, note.Id)).Should().BeEquivalentTo(note);   // unchanged, same version
}
```

Keep the list of write endpoints next to the test, or enumerate them from the endpoint data source, so a
new write endpoint without an ownership test is noticed. A 409 for a stale version is a separate test
(concurrency), not part of the ownership rule.

## Startup assertions (15.4, 15.5)

- A policy whose configuration is empty must **deny everyone** (for example an admin policy with no
  admin role configured). Test it by building the host with the empty setting and asserting 403 for `admin`.
- An endpoint that opts out of authorization without being on the allowlist must fail startup. Test the
  guard directly with a fake endpoint that is not on the list.
- A missing OIDC authority, client id, secret or audience must fail startup outside Development/Test (14.5).

## Anti-patterns

| Don't | Why |
|---|---|
| Assert only that the allowed persona gets 200 | A rule that lets everyone in still passes. |
| One test per persona with hand-written denial lists | The population helper asserts the complement; hand-written lists rot as roles are added. |
| Return 403 for a resource owned by someone else | It confirms the resource exists. Use 404. |
| Test the ownership rule only through the UI | Slow, and a UI that hides the button is not a control. |
| Skip the "nothing was written" assertion | A 404 that still wrote is a worse bug than a 200. |
| Add a policy exception to make a test pass | Fix the endpoint or add the route to the allowlist with a comment that says why it is public. |

## Checklist

- The new or changed endpoint is covered by the policy sweep (it needs no edit if it follows the convention).
- Each permission has a population test with the allowed set and everyone else denied.
- Each write endpoint with a resource id has unknown-id, foreign-owner and nothing-written tests.
- Anonymous endpoints are on the allowlist with a comment saying how they authenticate themselves.
- Run the tests for both variants: `dotnet test --project api/tests/<project>` for the Marten and EF Core test projects, and the architecture tests.
