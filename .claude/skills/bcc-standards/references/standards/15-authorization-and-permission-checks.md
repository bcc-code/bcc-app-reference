# 15 Authorization and Permission Checks

How we decide who may call an endpoint and see or change a resource. The rule is default deny: an endpoint is protected unless someone has deliberately opened it, and tests prove it.

The pieces are a fallback policy that protects every endpoint, a convention once areas need different policies, a test of every route's policy, permission rules tested against every role, and an admin policy that fails closed when it is not configured.

## Rules

### Endpoint level

1. **Every endpoint MUST be protected without anyone remembering an attribute.** While every endpoint needs the same policy, the fallback policy (rule 3) does this: it applies to every endpoint without its own `[Authorize]`/`[AllowAnonymous]`, Wolverine endpoints included. Once areas need different policies (rule 2), add a Wolverine `ConfigureEndpoints` hook that calls `RequireAuthorization(...)` on every chain without its own attribute.
2. **Where areas need different policies, the policy MUST be chosen from where the endpoint lives: its assembly, feature folder or path prefix.** For example, every endpoint in the admin feature assembly gets the `Admin` policy, or paths map as `/api/ops/` to `GlobalAdmin`, `/api/admin/projects/{projectId}` to `ProjectAdmin`, `/api/admin/` to `AnyAdmin`, and the rest to `User`. A new endpoint in the admin area is then admin-only without extra work.
3. **The ASP.NET Core fallback policy MUST require an authenticated user.** This covers minimal-API routes, the SPA fallback and anything else outside the Wolverine convention.
4. **Anonymous endpoints MUST be listed in one explicit allowlist, and the endpoint sweep test (rule 6) MUST fail when an endpoint opts out without being on it.** Copying an opt-out attribute then fails CI. A startup check that does the same MAY be added. An anonymous endpoint must authenticate itself in another way (signed token, capability token, shared secret) and say so in a comment.
5. **A policy whose configuration is empty MUST deny everyone.** An `Admin` policy becomes `RequireAssertion(_ => false)` when the setting it depends on (for example the identity-provider group or role name that grants admin) is empty, and internal policies are registered as deny-all when the host is not serving. A missing setting must never open access.
6. **An integration test MUST enumerate `EndpointDataSource` and assert that every endpoint has the policy its location requires, and that only allowlisted endpoints allow anonymous access.** This is the only check that catches a wrong or missing policy on a new endpoint. For example `EndpointPolicyConventionTests`.
7. **Host roles that serve different route sets MUST assert the separation at startup.** For example, an in-house startup check (here called `RoleEndpointAssertion`) throws if the Api maps `/internal/**` or the Processor maps `/api/**`. Applies only to apps with more than one host role.

### Resource level

8. **Resource checks MUST run in the endpoint's `Permission` step, before any write. A caller who may not do this to an existing resource gets 403; 404 is for a resource that does not exist.** Learning that an id exists tells the caller nothing: ids carry no personal data, and our apps have no business figures to hide. One answer for "not allowed" keeps `Permission` to one check and makes the cause clear in the client and the logs. The exception is a resource whose existence is itself personal data, typically one found by a person's id or email (`/api/persons/{personUid}/application`): a 403 there confirms that the person applied, so `Permission` answers 404. Neither has a body. Business rules stay in `Validate` (standard 09.2).
9. **A test MUST call every write endpoint with an unknown resource id and assert 404, and with a resource the caller may not change and assert 403, and in both cases that nothing was written.** Endpoint-level policies often let a caller through when the resource does not exist or is someone else's; this test proves each endpoint closes that gap itself.
10. **Permission rules MUST be tested against the whole user population, so that each rule asserts who is allowed and that every other role is denied.** A test partition defines the roles and users; a test reads `Only(Administrator).And(ProjectManager).Should(HaveReadAccess)`. A test that only checks the allowed role misses a rule that lets everyone in. Document the test vocabulary for the team (for example in an agent skill or a testing guide).
11. **Multi-tenant data MUST be isolated by a mechanism that applies to every query (an EF Core global query filter, Marten tenancy or Postgres row-level security), with any unfiltered access documented as an escape hatch for named cases only.** For example, a tenant-scoped context filters every tenant-owned entity by tenant id, and its doc comment lists when the unfiltered context is allowed. Event-sourced apps apply the same idea to projections and queries. Not needed in single-tenant apps.

### Frontend and stores

12. **The server SHOULD expose the caller's computed permissions to the SPA so it can hide actions, while every endpoint still checks them.** A `PermissionsEndpoints` group should use the same permission services the endpoints use. Hiding a button is a UX choice, not a control.
13. **Admin access SHOULD come from roles or groups in the identity provider or Core API, not from a list of person ids in config.** A config list needs a deploy to change and is invisible to the people who manage access. Sync grants from Core API roles instead.
14. **Where clients read a data store directly (for example Firestore), the store MUST deny client writes, and its rules MUST be tested per role in CI.** This applies only to apps that do this. For Firestore, use `allow write: if false` in the rules and test them against the emulator.

## Examples

### Convention-based policy per area (rules 1, 2)
```csharp
public static void Apply(HttpChain chain)
{
    var declared = chain.Method.Method.GetCustomAttributes(true)
        .Concat(chain.Method.HandlerType.GetCustomAttributes(true))
        .Any(a => a is IAuthorizeData or IAllowAnonymous);
    if (!declared) chain.RequireAuthorization(AppPolicies.User);
}
```

Register it with `ConfigureEndpoints` once areas need different policies; until then the fallback policy is enough. The same approach can be applied per assembly in the Wolverine setup.

### Test that every endpoint has the expected policy (rules 2, 6)
```csharp
endpoint.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull($"{path} must not allow anonymous access");
var expected = ExpectedPolicy(path);
policies.Should().Contain(expected, $"{path} needs the {expected} policy");
...
private static string ExpectedPolicy(string path) => path switch
{
    _ when path.StartsWith("/api/ops/", StringComparison.OrdinalIgnoreCase) => AppPolicies.GlobalAdmin,
    _ when path.StartsWith("/api/admin/projects/{projectId}", StringComparison.OrdinalIgnoreCase) => AppPolicies.ProjectAdmin,
    _ when path.StartsWith("/api/admin/", StringComparison.OrdinalIgnoreCase) => AppPolicies.AnyAdmin,
    _ => AppPolicies.User,
};
```

### Unknown and foreign resource write tests (rule 9)
A test such as `Every_project_write_endpoint_answers_404_and_writes_nothing_for_an_unknown_project` enumerates the write endpoints and calls each with an unknown id as a caller with the broadest role. Its twin, `..._answers_403_and_writes_nothing_for_someone_elses_project`, calls each with an existing project as a caller without access. It asserts that the expected number of endpoints is found (a lower bound), so a broken filter cannot pass silently, and checks that no stream, event or row was created.

### Anonymous allowlist (rule 4)
```csharp
// In the endpoint sweep test. An endpoint marked [AllowAnonymous] is reachable by anyone on the
// internet unless it authenticates itself (signed token, capability token, shared secret). Adding a
// route here is the deliberate act of accepting that; the failing test exists so nobody can make an
// endpoint anonymous just by copying an attribute.
anonymous.Should().BeEquivalentTo("/health", "/alive", "/auth/login", "/auth/backchannel-logout");
```

### Whole-population permission tests (rule 10)
A `UserPartition` class defines every role the app knows, and a `Users` class provides one user per role. Each permission rule is then tested over the whole set:

```csharp
Only(Administrator).And(ProjectManager).Should(HaveReadAccess);
```

### Fail-closed policy (rule 5)
The `Admin` policy is built with `RequireAssertion(_ => false)` when the setting that grants admin (for example the identity-provider group name) is empty.

### Tenant filter (rule 11)
With EF Core, a tenant-scoped context applies `HasQueryFilter(e => e.TenantId == currentTenantId)` to every tenant-owned entity, typically through a small in-house helper that loops over the model. The unfiltered context is used only for the cases named in its doc comment.

## Related
- [14 Authentication and Sessions](14-authentication-and-sessions.md) — who the caller is
- [09 Endpoints and API Design](09-endpoints-and-api-design.md) — endpoint conventions and error responses
- [19 Testing Strategy](19-testing-strategy.md) — integration and permission tests
- [25 Code Quality Guards](25-code-quality-guards.md) — authorization sweep and startup guards
- [Security principles](../principles/security.md#security-principle) — security principle, zero trust and OWASP Top 10
