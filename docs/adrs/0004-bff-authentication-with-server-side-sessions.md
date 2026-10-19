# ADR 0004: BFF authentication with server-side sessions

- Status: Accepted
- Date: 2026-10-19

## Context

The frontend is a single-page application. A browser app that holds OIDC tokens in JavaScript exposes them to any script that
runs in the page, and every API call then needs token refresh logic in the browser. Standard 14 describes the pattern used in
BCC applications: the backend does the OIDC login and the browser only holds a session cookie.

## Decision

The API acts as a backend for frontend (BFF):

- The API runs the OIDC code flow with PKCE against the identity provider. Tokens stay on the server.
- The browser receives an HTTP-only, `Secure`, `SameSite=Lax` session cookie. The SPA never sees an access token.
- The session is held on the server side. Session claims are kept slim: the subject, a display name and roles, nothing else.
- State-changing requests need a CSRF token (antiforgery), including logout, which is a POST. The SPA reads the token from an
  endpoint that is on the anonymous allowlist.
- Back-channel logout from the identity provider ends the session on the server.
- The Data Protection keys that protect cookies and antiforgery tokens are stored in Postgres and encrypted with a Cloud KMS
  key, so every Cloud Run instance accepts the same cookies (standard 04, rule 11).
- Authorization is default-deny. Only the auth routes are anonymous, listed in one allowlist that a test guards.

The shared implementation is in `BccCode.Template.Web`.

## Consequences

| Gain | Cost |
|---|---|
| No tokens in the browser | The API holds session state and must be reachable for every request |
| Session end (logout, back-channel logout) takes effect on the server at once | Anonymous API clients are not supported by this scheme; machine clients need their own scheme |
| One place for cookie, CSRF and security header settings | The SPA and the API must share a site, which the gateway setup gives |

## Alternatives considered

- Tokens in the browser (OIDC client in the SPA): simpler backend, but token theft through XSS gives the attacker a bearer
  token, and logout cannot be enforced centrally.
- Stateless cookies that carry the tokens, encrypted: no server state, but the cookie gets large, revocation is hard, and
  back-channel logout cannot be honoured without a store.
