# 15 Authorization and Permission Checks

How we decide who may call an endpoint and see or change a resource. The rule is default deny: an endpoint is protected unless someone has deliberately opened it, and tests prove it.

## Rules

### Endpoint level

1. **Every endpoint MUST get an authorization requirement by convention at startup, not by remembering an attribute.** In Wolverine.HTTP this is a `ConfigureEndpoints` hook that calls `RequireAuthorization(...)` on every chain without its own `[Authorize]`/`[AllowAnonymous]` (bcc-privacy `WolverineExtensions`, vote-rewrite `EndpointAuthorization.Apply`). orginn's `CurrentUserProvider` middleware does the same for authentication.
2. **The policy MUST be chosen from where the endpoint lives: its assembly, feature folder or path prefix.** bcc-privacy gives every endpoint in `BccCode.Privacy.Domain.Admin` the `Admin` policy; vote-rewrite maps `/api/ops/` → `GlobalAdmin`, `/api/admin/polls/{pollId}` → `PollAdmin`, `/api/admin/` → `AnyAdmin`, the rest → `Voter`. A new endpoint in the admin area is then admin-only without extra work.
3. **The ASP.NET Core fallback policy MUST require an authenticated user.** This covers minimal-API routes, the SPA fallback and anything else outside the Wolverine convention.
4. **Anonymous endpoints MUST be listed in one explicit allowlist, and startup MUST fail when an endpoint opts out without being on it.** orginn's `SelfAuthenticatedRoutes` turns copying an opt-out attribute into a startup error. An anonymous endpoint must authenticate itself in another way (signed token, capability token, shared secret) and say so in a comment.
5. **A policy whose configuration is empty MUST deny everyone.** bcc-privacy's `Admin` policy becomes `RequireAssertion(_ => false)` when the admin list is empty; vote-rewrite registers internal policies as deny-all when the host is not serving. A missing setting must never open access.
6. **An integration test MUST enumerate `EndpointDataSource` and assert that every endpoint has the policy its location requires, and that only allowlisted endpoints allow anonymous access.** This is the only check that catches a wrong or missing policy on a new endpoint. vote-rewrite: `EndpointPolicyConventionTests`.
7. **Host roles that serve different route sets MUST assert the separation at startup.** vote-rewrite's `RoleEndpointAssertion` throws if the Api maps `/internal/**` or the Processor maps `/api/**`. Applies only to apps with more than one host role.

### Resource level

8. **Resource checks MUST run in the endpoint's `LoadAsync`/`Validate` step, before any write, and MUST return 404 for a resource the caller may not see.** 404 avoids confirming that the resource exists. 403 is for resources the caller can see but not change.
9. **A test MUST call every write endpoint with an unknown or foreign resource id and assert 404 and that nothing was written.** Endpoint-level policies often let a caller through when the resource does not exist (vote-rewrite SEC-15); this test proves each endpoint closes that gap itself.
10. **Permission rules MUST be tested against the whole user population, so that each rule asserts who is allowed and that every other role is denied.** orginn's `UserPartition` and `Users.cs` define the roles; a test reads `Only(Administrator).And(BoardLeader).Should(HaveReadAccess)`. A test that only checks the allowed role misses a rule that lets everyone in. The `permission-tests` skill in orginn documents the vocabulary.
11. **Multi-tenant data MUST be isolated by a global query filter, with the unfiltered context documented as an escape hatch for named cases only.** orginn's `TenantedDataContext` filters every `IAppEntity` by `AppId` and its doc comment lists when the unfiltered `DataContext` is allowed. Event-sourced apps apply the same idea to projections and queries. Not needed in single-tenant apps.

### Frontend and stores

12. **The server SHOULD expose the caller's computed permissions to the SPA so it can hide actions, while every endpoint still checks them.** orginn's `PermissionsEndpoints` uses the same permission services the endpoints use. Hiding a button is a UX choice, not a control.
13. **Admin access SHOULD come from roles or groups in the identity provider or Core API, not from a list of person ids in config.** A config list needs a deploy to change and is invisible to the people who manage access. bcc-privacy records its allowlist as tech debt; vote-rewrite syncs grants from Core API roles.
14. **Data stores that clients read directly (for example Firestore) MUST deny client writes, and the rules MUST be tested per role in CI.** vote-rewrite: `allow write: if false` in `firebase/firestore.rules`, tested by `firebase/rules.test.ts` against the emulator.

## Example

`api/BccCode.Template.Web/` (default-deny conventions, fallback policy, admin policy, anonymous-route allowlist guard). Tests: endpoint policy sweep and ownership 404 tests in `api/tests/`. See the `permission-tests` skill.
