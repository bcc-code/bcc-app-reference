---
title: "A revoke of the admin role can miss a session that is being signed in at that moment"
status: needs-decision
decision-by: "the team, when an app made from the template has administrators it must be able to lock out at once"
effort: M
priority: P3
scope: api
area: permissions
---

# A revoke of the admin role can miss a session that is being signed in at that moment

`RevokeAdminEndpoint` (`api/BccCode.Template/Admin/RevokeAdmin.cs`) revokes the grant and deletes the person's sessions in one transaction ([ADR 0031](../adrs/0031-entra-id-sign-in-next-to-bcc-signon.md)). A sign-in that is in flight can fall between the two steps:

1. `SignInPeople.RecordAsync` reads the person's active grants (the admin role goes into the principal).
2. The revoke commits: the grant is revoked and the sessions that exist now are deleted.
3. The sign-in finishes and stores its session, with the admin role in the ticket.

That session was not there when the revoke deleted sessions, and nothing looks at the grant again, so the person keeps the admin role until the session ends (8 hours, sliding). The window is the time between the grant read and the session write in one callback, a few milliseconds to a second, and it needs the person to sign in at the moment they are revoked.

No grant check was added to every admin request, because that is a database read on each `/api/admin` call (and on every call, if the check covered all roles) for a window this small. The role in the session is what the admin policy and `AdminMfaMiddleware` read, with no database.

Options, cheapest first:

- Re-check the active grants in the cookie's `OnValidatePrincipal` (`AuthConfiguration.ConfigureCookie`), only for sessions that carry a role that came from a stored grant. It costs a read for administrators only, and makes a revoke effective on the next request whatever the timing.
- Stamp the grant's `version` into the session at sign-in and compare it in `OnValidatePrincipal`; the same read, but a cached value could replace it.
- Leave it, and say in the runbook that a revoke of someone who may be signing in is repeated after a minute.

To close it: pick one, add an integration test that signs a session in with a grant read before a revoke and expects the next request to be 401 or 403, and delete this file.
