# 16 HTTP Security Hardening

The HTTP-level controls every BFF app sets: response headers, CSP, CSRF protection, CORS, rate limiting, forwarded headers, redirects and HTML sanitization. They limit the damage of an XSS bug, a malicious site or a misbehaving client.

## Rules

### Response headers

1. **Security headers MUST be set on every response, including errors, by one middleware that writes them in `Response.OnStarting`.** Setting them in endpoints misses error pages and static files. See Security Headers.
2. **The SPA shell MUST carry a CSP with a per-request nonce: `default-src 'self'; script-src 'self' 'nonce-…'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'`, plus only the extra hosts the app needs.** `style-src 'unsafe-inline'` is allowed only while PrimeVue injects `<style>` at runtime, and the reason MUST be in a code comment. See Content Security Policy and CSP Example.
3. **Existing apps SHOULD roll out CSP as `Content-Security-Policy-Report-Only` first, then enforce it.** This finds inline scripts and third-party hosts without breaking production.
4. **HSTS MUST be sent with `max-age=31536000` outside Development.** Not in Development, because it would pin HTTPS for `*.dev.localhost` for a year. Add `includeSubDomains`/`preload` only when the domain owner agrees. See HSTS.
5. **Every response MUST carry `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `X-Frame-Options: DENY`, a `Permissions-Policy` that disables unused features (camera, microphone, geolocation and so on), `Cross-Origin-Opener-Policy: same-origin` and `Cross-Origin-Resource-Policy: same-origin`.** `X-Frame-Options` covers browsers that ignore `frame-ancestors`; COOP/CORP stop other origins from holding a window reference or embedding our responses. Widget bundles meant for other origins are the exception to CORP and must be listed.
6. **`/api` responses MUST default to `Cache-Control: no-store` when the endpoint sets nothing.** API answers are per person and must not be stored by shared caches.
7. **The `Server` header MUST be removed (`builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false)`).** It tells an attacker the stack and version for free.
8. **`index.html` MUST be served by the API, not by a bucket or CDN, so the headers and the CSP nonce apply to it.** Hashed assets can come from a bucket. See Frontend Hosting Strategy.

### CSRF

9. **Every unsafe request (POST, PUT, PATCH, DELETE) that carries the session cookie MUST pass a double-submit antiforgery check, whether or not it also has an `Authorization` header.** In Wolverine this is a `Before` middleware applied to all endpoints, because ASP.NET's `UseAntiforgery` only checks form-bound endpoints. See CSRF.
10. **The check MUST NOT exempt a request just because it has a `Bearer` header.** bcc-privacy exempts on the presence of the header; vote-rewrite exempts only when there is no session cookie. With bcc-privacy's rule, a request with any `Bearer` value plus the ambient cookie skips the check and is then authenticated by the cookie on policies that accept both schemes. Whether that can be exploited depends on CORS settings; vote-rewrite's rule does not depend on them.
11. **A failed check MUST return a ProblemDetails 400 with `X-Csrf-Refresh: 1`, and the frontend fetch wrapper MUST fetch a new token and retry once on that header only.** A plain validation 400 must not trigger a retry. bcc-privacy `frontend/src/lib/xsrfInterceptor.ts`, vote-rewrite `frontend/src/lib/voteFetch.ts`.
12. **The antiforgery cookie MUST be `SameSite=Strict`.** The session cookie stays `Lax` so links into the app work (see [14 Authentication and Sessions](14-authentication-and-sessions.md)).

### CORS

13. **A same-origin BFF MUST NOT enable CORS.** The SPA is served from the API, so no cross-origin access is needed. See CORS.
14. **Where cross-origin access is needed (embeddable widgets), CORS MUST apply only to `/api`, use an explicit origin allowlist validated at startup, fail closed outside Development when the list is empty or malformed, and not allow credentials unless required.** vote-rewrite parses each entry as `scheme://host[:port]` and rejects wildcards; bcc-privacy throws when the list is empty outside Development.

### Rate limiting, proxies, redirects

15. **Anonymous endpoints and endpoints that call expensive dependencies MUST be rate limited per person (when authenticated) or per IP, returning 429 with `Retry-After` and a ProblemDetails body.** The known limits (per-instance counters, IP taken from forwarded headers) MUST be written next to the configuration. See Rate Limiting.
16. **Forwarded headers MUST only be trusted from known proxy networks, and invalid configuration MUST fail startup.** bcc-privacy validates a CIDR list and throws on a bad entry. On Cloud Run, where the front proxy has no fixed address, the accepted alternative is ingress restricted to the load balancer plus `ForwardLimit = 1`, documented in the code.
17. **Every redirect target that comes from the request (return URLs, post-logout URLs) MUST be checked as local before use.** Use a tested `LocalRedirectGuard`.
18. **User-provided HTML MUST be sanitized with an allowlist sanitizer before it is stored and again before it is rendered, and `v-html` on user content MUST be blocked by a lint rule unless it goes through the sanitizer.** See HTML Sanitization.

## Example

`api/BccCode.Template.Web/` (security headers middleware, CSP with nonce, CSRF check, rate limiting, ProblemDetails for 400 and 429).
