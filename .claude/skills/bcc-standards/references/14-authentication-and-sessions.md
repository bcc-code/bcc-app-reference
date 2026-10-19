# 14 Authentication and Sessions

How our web apps sign users in, keep sessions, and authenticate machine callers. Our apps use the Backend-for-Frontend (BFF) pattern: the .NET API serves the SPA, runs the OIDC flow and holds the session in a cookie the browser cannot read.

## Rules

### Sign-in flow

1. **The SPA MUST authenticate through the API as a BFF, using the OIDC authorization code flow with PKCE and PAR (`PushedAuthorizationBehavior.UseIfAvailable`).** Tokens never reach the browser, and PAR keeps authorization parameters out of the front-channel URL. See OIDC (OpenID Connect) and OAuth PAR (Pushed Authorization Requests).
2. **`SaveTokens` MUST be `false` unless the backend calls APIs on the user's behalf; then tokens MUST be kept in a server-side store, never in the cookie.** A token in the cookie lives as long as the cookie and can be replayed if the cookie leaks.
3. **Set `MapInboundClaims = false`, request only the scopes the app uses, and slim the principal to the claims the app reads before the session is written.** Fewer claims mean a smaller cookie or ticket, less personal data at rest, and no surprise claim-type remapping. vote's `SessionClaims.Slim` keeps four claims. Do not request `offline_access` when no refresh token is used.
4. **Administrators SHOULD be stepped up to MFA before any session cookie is written.** orginn's `OnTokenValidatedStepUp` suppresses the first sign-in for admins and re-challenges with the `mfa` scope, with the decision in a pure, unit-tested function (`ShouldStepUpAdmin`).
5. **Startup MUST fail when OIDC authority, client id, client secret or the bearer audience is missing outside Development/Test.** A missing audience silently disables audience validation. bcc-privacy added this after a penetration-test finding and has a test for it; vote-rewrite throws in `ConfigureOidc`.

### Session cookie and store

6. **The session cookie MUST be an opaque `__Host-` cookie with `HttpOnly`, `Secure=Always` and `SameSite=Lax`.** `__Host-` forbids `Domain` and requires `Path=/` and HTTPS. See Auth Sessions (Cookies).
7. **Sessions MUST be stored server side (an `ITicketStore`), so the cookie only carries a key and a session can be revoked.** With a stateless ticket, a stolen cookie is valid until it expires; vote-rewrite documents that revoking one session means rotating all Data Protection keys. bcc-privacy's `MartenSessionStore` protects the serialised ticket with Data Protection and stores it in Postgres. For EF Core apps the same store can be a table.
8. **Session lifetime MUST be short with sliding expiry, and the chosen value documented with its reason.** bcc-privacy uses 10 minutes because a consent flow is short; vote uses 12 hours and orginn 8 hours for back-office work. Shorter limits the window for a stolen session; longer means fewer re-logins. With a server-side store, a longer sliding window is acceptable because sessions can be revoked.
9. **The app MUST expose an OIDC back-channel logout endpoint that validates the signed logout token and deletes the sessions by `iss` + `sid`.** Capture `sid` and `iss` as claims in `OnTokenValidated` so the store can find them. See Backchannel Logout.
10. **Logout MUST be a POST protected by the antiforgery token.** A GET logout can be triggered from any site with an image tag.
11. **The browser-facing "who am I" endpoint MUST return only the fields the SPA needs, not the full claim list.**

### API behaviour

12. **Requests under `/api` MUST get 401/403 instead of redirects, both from the cookie handler and from `OnRedirectToIdentityProvider`.** A redirect to the IdP is useless to `fetch` and hides the real status.
13. **The SPA MUST handle 401 by navigating to the login endpoint with a return URL, and the server MUST validate that URL as local (`LocalRedirectGuard`).** This prevents open redirects through the login flow. vote's `voteFetch` does this.
14. **Data Protection keys MUST be shared across instances in Postgres and encrypted with Cloud KMS.** Cookies, the ticket store and antiforgery tokens depend on them. See ASP.NET Core Data Protection and [16 HTTP Security Hardening](16-http-security-hardening.md) for the antiforgery side.

### Machine callers

15. **Each machine caller (M2M client, widget, webhook) MUST have its own JWT bearer scheme with a mandatory audience and an allowlist of clients or scopes, and only the policies that need it MAY accept it.** vote's widget scheme accepts RS256 only, checks `azp`/`client_id` against `Widget:AllowedClients`, and is accepted by the `Voter` policy only; admin policies are cookie-only.
16. **Internal service-to-service calls (Pub/Sub push, Cloud Tasks, Cloud Scheduler) MUST be authenticated with Google-signed OIDC tokens, one policy per caller requiring its own audience and service-account email.** Network position is not trust. See Zero Trust.
17. **When a request carries both a session cookie and a bearer token for a different person, the API MUST reject it with 401.** Otherwise a policy that accepts both schemes merges two identities. vote: `UseVoteWidgetSessionGuard`.
18. **Test-only authentication (impersonation headers, load-test tokens) MUST be off by default, enabled only by explicit config, limited to an environment allowlist, and MUST refuse to start in production.** vote checks an allowlist in C# and in a Terraform precondition; bcc-privacy throws when enabled in Prod.

## Example

`api/BccCode.Template.Web/` (OIDC code flow with PKCE and PAR, `__Host-` cookie, server-side session, back-channel logout, POST logout with CSRF, anonymous auth-route allowlist) and `api/OidcMock/` for local sign-in.
