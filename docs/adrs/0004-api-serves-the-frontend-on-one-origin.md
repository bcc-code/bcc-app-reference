# 0004. The API serves the frontend on one origin

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADR 0004.

## Context

The SPA calls our own API. If the browser talked to the Vite dev server and the API on different origins, we would need CORS locally, and cookies, cross-origin checks and CSP would behave differently from production, where the API serves the built SPA.

## Decision

The browser talks only to the API. In development the API forwards every request that is not an API route to the Vite dev server with YARP, found through Aspire service discovery. In other environments the API serves the built SPA from `wwwroot` and falls back to `index.html`. The frontend client therefore uses relative URLs (`baseUrl: ''`).

## Consequences

| | |
|---|---|
| Positive | No CORS; cookies, the cross-origin check and CSP behave the same locally and in production; one HTTPS certificate. Works with BFF authentication ([0019](0019-bff-sign-in-with-bcc-signon.md)). |
| Negative | The frontend cannot run against the API without the API running; YARP is an extra moving part in development. |
| Follow-up | The gateway setup follows standard 26 (assets from a bucket, everything else to the API), see [0013](0013-cloud-run-behind-the-platform-gateway.md). |

## Alternatives considered

- **Vite dev-server proxy to the API** — gives one origin in development only; production would still differ.
- **Separate origins with CORS** — more configuration and a weaker security model for cookie-based sessions.
