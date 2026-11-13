# 0019. BFF sign-in with BCC Signon, with PAR

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADR 0021. Differs on PAR, see below.

## Context

Every page and API call needs a signed-in BCC person. Standard 14 asks for a Backend-for-Frontend: the API runs the OIDC flow and keeps the session in a cookie the browser cannot read, so no token reaches the SPA. BCC's identity provider is BCC Signon (Auth0). Standard 14.1 requires pushed authorization requests (PAR) where the provider supports it.

bcc-ateam runs without PAR, because its check of the sandbox discovery document found no `pushed_authorization_request_endpoint`, and its ADR 0021 records that. The template keeps PAR on. If Signon has no PAR endpoint, `UseIfAvailable` falls back to a plain authorization request without any user action; if it has one, requests are pushed. We keep it until it is shown not to work in a real environment.

## Decision

We will sign people in through the API with the OIDC authorization code flow, PKCE and PAR against BCC Signon.

- One confidential client per environment. The secret comes from Secret Manager (`<app>-oidc-client-secret`, created by hand, see the [first-deploy runbook](../runbooks/first-deploy.md)); the authority and client id are plain configuration. Startup fails outside Development and Test when any of them is missing (14.5).
- Scopes `openid profile` only. Tokens are not saved (`SaveTokens = false`), no `offline_access`, no userinfo call, `MapInboundClaims = false`.
- The session keeps only `sub`, `name`, `role`, `sid`, `iss`, `https://login.bcc.no/claims/personUid` and `template:mfa` (set by the app from `amr`, never taken from the token). **personUid is the key of all data**: notes belong to a personUid, and the actor of every change is recorded by it. A sign-in without it fails.
- PAR is `UseIfAvailable`. If BCC Signon turns out not to support it in sandbox and prod, an application made from the template may switch it off and record that in an ADR that supersedes this one (as bcc-ateam did). If it supports it, switch to `Require`.
- Logout is a POST (a real form POST, because the response redirects through the identity provider), so the cross-origin check ([0022](0022-default-deny-authorization-and-request-hardening.md)) covers it. If the provider has no `end_session_endpoint`, it redirects to the provider's logout URL with the client id and the return URL.
- `/api` requests get 401/403, never a redirect. A navigation without a session is sent to the provider before `index.html` is served, so the SPA never loads signed out. `GET /auth/login?returnUrl=` accepts only local return URLs. The auth routes are under `/auth/*`.
- Administrators must have signed in with MFA (14.4); the admin policy refuses a session without it. The step-up asks for the second factor with the `mfa` scope, as bcc-ateam does; whether BCC Signon honours `acr_values` instead is not verified ([tech debt](../tech-debt/mfa-step-up-uses-a-scope-not-acr-values.md)).
- Locally, in E2E and in integration tests, an OpenIddict mock (`api/OidcMock`) stands in for BCC Signon with named personas (standard 02.4). It has a public PKCE client, so no secret is needed locally.

## Consequences

| | |
|---|---|
| Positive | No token in the browser. One origin, so no CORS. The same pattern as the other BCC apps. Local runs and tests need no BCC Signon account or secret. |
| Negative | The mock has to be kept close enough to BCC Signon that a flow that works locally also works in sandbox. PAR adds a request to the sign-in and one more thing that can fail against a provider whose endpoint behaves differently from the mock. |
| Follow-up | Check in sandbox that PAR works, then decide: `Require`, or off with a superseding ADR. |

## Alternatives considered

- **Tokens in the SPA (public client in the browser)** — standard 14 rules it out: tokens in JavaScript can be stolen by any XSS bug.
- **Duende BFF** — the other BCC apps rejected it for its licence cost. The hand-written setup is small.
- **No PAR (bcc-ateam)** — the same result if Signon has no endpoint, but it drops a protection we may get for free, and it would have to be turned on again later.
- **PAR `Require`** — would fail every sign-in if BCC Signon has no PAR endpoint.
