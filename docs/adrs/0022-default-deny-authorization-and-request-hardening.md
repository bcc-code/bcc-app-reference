# 0022. Default-deny authorization, cross-origin checks and no CORS

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADR 0024.

## Context

With sign-in in place ([0019](0019-bff-sign-in-with-bcc-signon.md)), every endpoint has to decide who may call it. Standard 15 asks for default deny: an endpoint is protected unless someone deliberately opened it, and a test proves it. A cookie session also needs CSRF protection (standard 16). The SPA and the API share one origin ([0004](0004-api-serves-the-frontend-on-one-origin.md)). Behind the platform gateway the app sees the load balancer's address unless forwarded headers are read. An earlier version of the template used antiforgery tokens and a custom CSRF middleware; bcc-ateam checked that ASP.NET's built-in antiforgery marking does not stop a JSON request without a token.

## Decision

- The ASP.NET fallback policy is `User` (signed in, with personUid). It applies to every endpoint without its own `[Authorize]`/`[AllowAnonymous]`: the Wolverine endpoints, the SPA shell and the Vite proxy.
- Anonymous endpoints are on one allowlist (`AnonymousRouteGuard`): `/health`, `/alive`, `/auth/login`, `/auth/backchannel-logout` (it checks a signed logout token itself) and, in Development only, the OpenAPI document. Startup fails when any other route allows anonymous access.
- Every endpoint has a `Permission` step in its compound handler, a pure function that returns the problem (or nothing) before `Validate` and `Handle` run. `/api/admin/**` gets the admin policy by convention, and an empty `Auth:AdminRole` denies everyone (fail closed, 15.5).
- A resource that exists but the caller may not use answers 403; 404 is for one that does not exist or whose existence is itself personal data (15.8), checked in the endpoint before any write. In the Notes sample, another person's note answers 404, because the existence of a note is personal data, and the ownership tests check that nothing was written.
- CSRF: every unsafe request that does not come from our own origin answers 403, with or without a cookie. The browser's `Sec-Fetch-Site` decides (`same-origin` or `none` pass), and `Origin` when the browser sends no fetch metadata. `same-site` is refused, because every app under `bcc.no` is the same site and the Lax session cookie goes along with their requests. There are no antiforgery tokens, so the SPA needs no CSRF code (standard 16.9 to 16.12). With .NET 11 the check is the built-in cross-origin middleware.
- No CORS. Same origin, so nothing needs it (16.13).
- `/internal/**` is outside the user policies. Each route has its own policy on a Google OIDC bearer scheme ([0023](0023-pubsub-for-internal-messages-and-bcc-pubsub-for-events.md)), and `InternalRouteGuard` fails startup when a route lacks one.
- Forwarded headers are read from the gateway's load balancer: ingress only allows the load balancer, and two hops match the header shape it documents, so the app sees the client's address and `https`.
- No rate limiting in the app. The anonymous endpoints are cheap, and per-instance counters keyed by a forwarded IP would only stop a stuck client; floods are the gateway's job. The first endpoint that starts expensive work gets its own limit (16.15).

## Consequences

| | |
|---|---|
| Positive | A new endpoint is protected without anyone remembering to protect it, and a test fails if one slips through. CSRF is checked in one place, with no token endpoint or client code. |
| Negative | Opening an endpoint to anonymous callers takes a change to the allowlist, by design. A test checks every route against its prefix, so adding a prefix means updating the sweep. |
| Follow-up | Permission tests cover whole populations of roles, not one persona (15.10): see the `permission-tests` skill. |

## Alternatives considered

- **`[Authorize]` on each endpoint** — relies on people remembering it.
- **Double-submit antiforgery tokens** — a token endpoint, a cookie, a refresh-and-retry path in the SPA and our own middleware, for no protection the origin check lacks.
- **SameSite cookies and no CORS alone** — other apps under `bcc.no` are the same site, and a POST without a body is not checked for its content type.
- **404 for every forbidden resource** — hides that the id exists, but it also hides real permission errors from the person who may know about the resource. Used only where existence is personal data.
