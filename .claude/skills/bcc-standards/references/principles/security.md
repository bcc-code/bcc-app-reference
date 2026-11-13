# Security

> **Security is not a feature; it is the natural byproduct of a system that is simple enough to understand.**

Security is a non-negotiable requirement and must be designed in from the start. This page collects the security principles we apply. The sections cover design principles, authentication and sessions, browser hardening, data handling, and scanning and supply chain. The matching rules are in [14 Authentication and Sessions](../standards/14-authentication-and-sessions.md), [15 Authorization and Permission Checks](../standards/15-authorization-and-permission-checks.md), [16 HTTP Security Hardening](../standards/16-http-security-hardening.md), [17 Supply Chain and Security Scanning](../standards/17-supply-chain-and-security-scanning.md) and [18 Personal Data and GDPR](../standards/18-personal-data-and-gdpr.md).

## Security Principle

The most secure systems are the ones you can understand. When the code is clear, data flows are traceable, and trust boundaries are explicit, security problems are easier to spot and harder to introduce by accident.

- Favor clarity and explicitness over cleverness. Code that is hard to understand is also hard to audit.
- Make data flows and access paths visible. If you cannot easily trace who can access what, something is wrong.
- Reduce hidden behavior. Every piece of magic is a piece of the system that is harder to reason about securely.
- Apply defense in depth: multiple layers of controls, each simple on its own.

Avoid security by obscurity (hiding things does not make them secure) and avoid bolting security on as a final hardening step. By then it is too late.

Example: a service replaces dynamic reflection-based endpoint configuration with an explicit allowlist of routes and roles. The access paths are now obvious in code review.

Related: [Zero Trust](#zero-trust), [15 Authorization and Permission Checks](../standards/15-authorization-and-permission-checks.md), [Code design principles](code-design.md).

## Zero Trust

Do not trust any request by default, not even ones from inside the internal network. Every request must be authenticated and authorized explicitly.

- Authenticate and authorize every request, including service-to-service calls on the same internal network.
- Treat internal networks as untrusted. A compromised service on the same network should not get free access to other services.
- Apply least privilege to service-to-service access. Each service should only be able to call what it needs.
- Use signed service tokens (on GCP, Google-signed OIDC tokens checked for audience and calling service account) or mutual TLS (mTLS) for service-to-service authentication.

Why it matters: traditional perimeter security assumes everything inside the network is safe. Once an attacker gets inside, through a compromised service or a phishing attack, they can move freely. Zero Trust limits the blast radius by treating every hop as a security boundary.

Example: an internal API requires a service token for every call, even when both services run on the same subnet. There is no implicit trust based on network location.

Related: [Auth Sessions (Cookies)](#auth-sessions-cookies), [OIDC](#oidc-openid-connect), [CORS](#cors), [14 Authentication and Sessions](../standards/14-authentication-and-sessions.md) (rule 16, internal callers), [13 Messaging and Integration](../standards/13-messaging-and-integration.md) (webhook authentication).

## STRIDE Threat Modelling

STRIDE is a structured way to think through threats. It breaks risks into six categories: **S**poofing, **T**ampering, **R**epudiation, **I**nformation Disclosure, **D**enial of Service, and **E**levation of Privilege. Walk through them systematically for each system boundary and the obvious threats are covered.

- Do a STRIDE walk-through at design time for significant new features or systems.
- Review each system boundary (API endpoints, message queues, storage, user flows) against each threat category.
- Document mitigations and any accepted residual risks.
- Use PASTA or LINDDUN if you need more depth around processes or data protection.

Example: during design review, the team walks through each STRIDE category for the login flow. Spoofing is covered by OIDC and validated ID tokens, repudiation by the audit log, and elevation of privilege by server-side session checks. The mitigations are documented.

Related: [OWASP Top 10](#owasp-top-10), [Audit Logging](#audit-logging).

## OWASP Top 10

Use the OWASP Top 10, the widely used reference for the most critical web application security risks, as a shared baseline for security conversations and as a checklist when reviewing new features.

- Review the Top 10 at design time for significant new features or APIs.
- Map concrete controls to each applicable risk. "We handle this" is not enough without specifics.
- Use OWASP ASVS if you need deeper, control-level guidance beyond the Top 10.

Example: before a new API goes to production, the team walks through the OWASP Top 10 and documents how each relevant risk is mitigated.

Related: [SAST](#sast-static-application-security-testing), [Dependency Scanning](#dependency-scanning), [Safe SQL Access](#safe-sql-access).

## Backend-for-Frontend (BFF)

A BFF is a server dedicated to one frontend: it serves the app and proxies all of the app's API calls, so the browser only ever talks to a single origin. Serving the frontend and its API from the same domain is what makes cookie-based auth, CORS and CSP simple.

- Serve the frontend and its API from the same origin through the BFF. The browser makes same-origin requests only.
- The BFF proxies calls to downstream APIs and attaches credentials server-side. The browser never holds tokens (see [Auth Sessions (Cookies)](#auth-sessions-cookies)).
- Because everything is same-origin, there is no CORS to configure and first-party cookies work without `SameSite=None` workarounds.
- Centralize security headers and CSP at the BFF (see [Frontend Hosting Strategy](#frontend-hosting-strategy)).

Why it matters: a single origin removes whole classes of complexity and risk at once: no cross-origin cookies, no permissive CORS policy, no tokens exposed to JavaScript, and one place to apply security headers. The browser holds only an opaque `HttpOnly` session cookie; the BFF holds any OAuth tokens. This is the default architecture for our browser-facing apps.

Related: [Auth Sessions (Cookies)](#auth-sessions-cookies), [Frontend Hosting Strategy](#frontend-hosting-strategy), [CORS](#cors), [CSRF](#csrf), [14 Authentication and Sessions](../standards/14-authentication-and-sessions.md).

## OIDC (OpenID Connect)

OpenID Connect is the standard authentication layer built on top of OAuth 2.0. It is how we handle user authentication across systems.

- Use standard OIDC flows. Do not roll your own auth.
- Use the authorization code flow. Avoid the implicit flow.
- Validate ID tokens rigorously: check signature, issuer, audience, expiry and nonce.
- Use short-lived ID tokens and rotate access tokens frequently.

Why it matters: OIDC is widely supported, well understood, and has a mature ecosystem of libraries and identity providers. Custom authentication schemes introduce risk without benefit.

Example: the app uses the OIDC authorization code flow. The returned ID token is validated before a server-side session is created.

Related: [OAuth PAR](#oauth-par-pushed-authorization-requests), [Auth Sessions (Cookies)](#auth-sessions-cookies), [Backchannel Logout](#backchannel-logout), [14 Authentication and Sessions](../standards/14-authentication-and-sessions.md).

## OAuth PAR (Pushed Authorization Requests)

Use Pushed Authorization Requests (PAR) to send authorization parameters to the identity provider over a secure back channel, so the browser redirect carries only an opaque reference URI instead of the parameters in the URL.

- Use PAR for sensitive authorization flows.
- Combine with PKCE and strict redirect URI validation.
- Fall back to the authorization code flow with PKCE if PAR is not supported by the identity provider.

Why it matters: authorization parameters in URLs can be logged, cached, or leaked via referrer headers. PAR keeps them out of the browser entirely. The browser redirect only carries a short-lived reference URI, not the actual parameters.

Example: the client POSTs authorization parameters to the PAR endpoint, receives a `request_uri`, then redirects the browser to the identity provider using only that URI.

Related: [OIDC](#oidc-openid-connect), [Auth Sessions (Cookies)](#auth-sessions-cookies), [Backend-for-Frontend (BFF)](#backend-for-frontend-bff).

## Auth Sessions (Cookies)

Authentication state lives on the server. The browser only gets an opaque `HttpOnly` cookie as a session handle and never sees the underlying tokens.

- Keep session state server-side. The browser should have no access to access tokens or refresh tokens.
- Set `HttpOnly`, `Secure` and `SameSite` on session cookies.
- Rotate session identifiers after login and after any privilege change.
- Use the BFF pattern to proxy API calls. The BFF attaches tokens server-side, so they never cross the browser boundary.

Why it matters: tokens stored in `localStorage` or `sessionStorage` are accessible to JavaScript, which means XSS attacks can steal them. An `HttpOnly` cookie cannot be read by JavaScript at all, which limits the blast radius of an XSS vulnerability.

Example: after login, the API issues an `HttpOnly` session cookie. The browser sends this cookie on every request, but never has access to the underlying access token.

Related: [Backend-for-Frontend (BFF)](#backend-for-frontend-bff), [CSRF](#csrf), [OIDC](#oidc-openid-connect), [14 Authentication and Sessions](../standards/14-authentication-and-sessions.md).

## Backchannel Logout

Implement back-channel logout: when a user logs out, the identity provider notifies your services directly instead of relying on the user's browser to propagate the logout.

- Implement a back-channel logout endpoint and register it with the identity provider.
- Revoke the server-side session immediately when a logout token arrives.
- Log logout events for auditing.
- Do not rely on client-side logout only. If the user's browser never makes it back to your app after logging out, the session stays active.

Why it matters: without back-channel logout, sessions can persist even after the user has logged out from the identity provider. This is especially problematic in SSO setups where a user expects logging out of one app to log them out everywhere.

Example: the identity provider sends a signed logout token to the API's back-channel endpoint. The API validates it and invalidates the session immediately, regardless of what the browser does.

Related: [OIDC](#oidc-openid-connect), [Auth Sessions (Cookies)](#auth-sessions-cookies), [14 Authentication and Sessions](../standards/14-authentication-and-sessions.md).

## Short-Lived Tokens

Access tokens should expire quickly. If a token is stolen, a short lifetime limits how long the attacker can use it.

- Keep access tokens short-lived (15 minutes is a common default).
- Use refresh tokens for long-lived sessions. Refresh tokens last longer but can be revoked.
- Monitor refresh token usage for anomalies. Unusual refresh patterns can indicate token theft.
- Store refresh tokens securely. In a BFF setup they stay on the server and are never exposed to the browser.

Example: access tokens expire after 15 minutes and refresh tokens last 30 days. If suspicious activity is detected, the refresh token is revoked and the user has to log in again.

Related: [Auth Sessions (Cookies)](#auth-sessions-cookies), [OIDC](#oidc-openid-connect), [Backchannel Logout](#backchannel-logout).

## ASP.NET Core Data Protection

Use ASP.NET Core's Data Protection API whenever you need to encrypt data your application will later decrypt. It handles key management and encryption for cookie values, anti-forgery tokens and other protected payloads.

- Use the Data Protection API rather than rolling your own encryption.
- Configure a persistent key store (for example a database or cloud storage) so keys survive restarts and scale across multiple instances.
- Set a key lifetime. The default is 90 days, which is usually fine.
- If several services need to share protected data (for example shared auth cookies), configure them to share the same key ring and use an explicit application name discriminator.
- Protect the key store. Keys at rest should be encrypted with Cloud KMS (see [14 Authentication and Sessions](../standards/14-authentication-and-sessions.md), rule 14).

Why it matters: without persistent, shared key storage, encrypted values such as auth cookies break on restart or when running multiple instances. Losing keys also means existing protected data becomes unreadable: users get logged out and tokens become invalid.

Related: [Secret Management](#secret-management), [Auth Sessions (Cookies)](#auth-sessions-cookies), [14 Authentication and Sessions](../standards/14-authentication-and-sessions.md).

## CORS

Configure CORS explicitly and never accept framework defaults without reviewing them. Cross-Origin Resource Sharing controls which origins can make requests to the API from a browser.

- Allow only the origins that actually need access. If it is just your own frontend, allow only that.
- Never use `*` as the allowed origin in production, especially with credentials enabled.
- Set allowed methods and headers explicitly. Do not allow everything.
- The best CORS policy is no CORS at all. If your frontend and API are on the same origin (for example via a BFF), you do not need it.

Example: the API allows only `https://app.example.com` for `GET` and `POST` with credentials. All other origins get a CORS error.

Related: [Auth Sessions (Cookies)](#auth-sessions-cookies), [CSRF](#csrf), [Security Headers](#security-headers), [16 HTTP Security Hardening](../standards/16-http-security-hardening.md).

## CSRF

Protect all state-changing requests against cross-site request forgery: attacks that trick authenticated users into unknowingly submitting requests to your application. This is a real risk with cookie-based auth, since browsers automatically attach cookies to every request.

- Refuse every state-changing request (anything but GET, HEAD and OPTIONS) that does not come from the app's own origin. Browsers say where a request comes from in `Sec-Fetch-Site`; fall back to `Origin` for browsers that do not send it.
- Use `SameSite=Strict` or `SameSite=Lax` on session cookies. This stops other sites, but not other apps under the same registrable domain, which count as the same site.
- Do not rely on CORS or on JSON content types alone. CORS only applies to cross-origin reads, and a POST without a body is not checked for its content type.
- Anti-forgery tokens protect the same requests at the cost of a token endpoint and client code; the origin check makes them unnecessary in a same-origin BFF.

Example: the API answers 403 to a POST whose `Sec-Fetch-Site` is `cross-site` or `same-site`. The SPA, on the same origin, needs no extra header.

Related: [Auth Sessions (Cookies)](#auth-sessions-cookies), [CORS](#cors), [Content Security Policy](#content-security-policy), [16 HTTP Security Hardening](../standards/16-http-security-hardening.md).

## Security Headers

Harden the browser's behavior against common attacks by setting a standard set of HTTP response headers. This takes little effort and pays off.

Set these on all responses:

- `Referrer-Policy: strict-origin-when-cross-origin`
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Cross-Origin-Opener-Policy: same-origin`
- `Cross-Origin-Resource-Policy: same-origin`
- `Permissions-Policy`: restrict access to browser features (camera, geolocation and so on) to least privilege
- A Content Security Policy and, outside development, HSTS (see below)
- Remove the `Server` header where the platform allows it, to avoid leaking server information

This is the set in [16 HTTP Security Hardening](../standards/16-http-security-hardening.md), rule 5. Older headers such as `X-XSS-Protection` add nothing when a CSP is in place.

Guidance:

- Apply headers in middleware so they apply to every response consistently.
- Do not rely on framework defaults without verifying what is actually being sent.
- Enforce at a gateway or CDN for consistent coverage across services.

Related: [Content Security Policy](#content-security-policy), [HSTS](#hsts), [Frontend Hosting Strategy](#frontend-hosting-strategy), [16 HTTP Security Hardening](../standards/16-http-security-hardening.md).

## HSTS

Enable HSTS (HTTP Strict Transport Security) so browsers always use HTTPS for your domain, even if the user types `http://`. It prevents downgrade attacks and accidental HTTP usage.

- Set `Strict-Transport-Security: max-age=31536000` on all HTTPS responses outside local development.
- Add `includeSubDomains` only when every subdomain supports HTTPS and the domain owner agrees.
- Do not allow HTTP in production. Redirect to HTTPS and then rely on HSTS to keep it there.
- Be careful with `preload`. Once you are on the preload list, you are committed. Validate your setup thoroughly first.
- Do not send HSTS for local development hosts, because it pins HTTPS for them for a year.

Caution: misconfiguring HSTS (for example setting it on a domain that does not fully support HTTPS) can lock out users until the HSTS max-age expires. Test carefully before deploying.

Related: [Security Headers](#security-headers), [Zero Trust](#zero-trust).

## Content Security Policy

Set a Content Security Policy: the header that tells the browser exactly which sources may load scripts, styles, images and other resources. Anything not explicitly allowed is blocked.

- Start with a restrictive policy (`default-src 'self'`) and loosen it only as needed. Do not start permissive.
- Use report-only mode when rolling out a new policy. It logs violations without blocking anything, so you can fix issues before enforcing.
- Avoid `unsafe-inline` for scripts. Use nonces or hashes instead.

Why it matters: even with good input validation and output encoding, XSS vulnerabilities can slip through. CSP is a last line of defense that limits what injected scripts can do. A strict CSP significantly reduces the impact of an XSS attack.

Example: a SPA ships with `default-src 'self'` and a nonce-based `script-src`. Report-only mode runs first to catch violations before the policy is enforced.

Related: [CSP Example](#csp-example), [HTML Sanitization](#html-sanitization), [Subresource Integrity (SRI)](#subresource-integrity-sri), [16 HTTP Security Hardening](../standards/16-http-security-hardening.md).

## CSP Example

A starting-point Content Security Policy you can adapt for your application. It matches rule 2 in [16 HTTP Security Hardening](../standards/16-http-security-hardening.md). Start with this and loosen it only as needed based on verified requirements.

- Use report-only mode when rolling out to an existing app, to catch violations before enforcing.
- Use a fresh nonce per request for scripts and styles instead of `unsafe-inline`.
- Do not allow whole CDN hosts in `script-src`. Shared CDNs host many libraries, and some of them can be abused to run attacker script. Self-host the assets instead.
- Each application needs to tune this for its specific third-party dependencies.

Example policy:

```csharp
string csp = $"default-src 'self'; " +
             $"script-src 'self' 'nonce-{nonce}'; " +
             $"style-src 'self' 'nonce-{nonce}'; " +
             "img-src 'self' data:; " +
             "font-src 'self' data:; " +
             "connect-src 'self'; " +
             "object-src 'none'; " +
             "base-uri 'self'; " +
             "frame-ancestors 'none'; " +
             "form-action 'self'";

context.Response.Headers.TryAdd("Content-Security-Policy", csp);
// For an existing app, report violations first without blocking:
// context.Response.Headers.TryAdd("Content-Security-Policy-Report-Only", csp);
```

Related: [Content Security Policy](#content-security-policy), [Security Headers](#security-headers).

## Frontend Hosting Strategy

Serve the frontend's entry point (`index.html`) from the API rather than a separate static file host, so security headers and CSP are applied consistently to every request that matters.

- Have the API return `index.html` and apply all security headers to every response.
- This keeps header configuration in one place and removes the risk of a static host sending weaker headers.
- Static assets (JS, CSS, images) can still be served from a CDN. Make sure the CDN enforces the same headers and CSP for the entry point, or route the entry point through the API.
- Prefer a single origin for the app and its API. It simplifies cookies, CORS and CSP.

Why it matters: if security headers are configured in several places (a static host for the frontend, the API for backend routes), they tend to diverge. The frontend might have weaker headers, or a CDN might strip them. Hosting the entry point from the API centralizes this and removes the drift. A single origin also avoids cross-origin cookie and CORS complexity.

Related: [Security Headers](#security-headers), [Content Security Policy](#content-security-policy), [CORS](#cors), [26 Gateway and Frontend Hosting](../standards/26-gateway-and-frontend-hosting.md).

## HTML Sanitization

Any user-provided HTML must be sanitized before rendering. Rendering raw user input as HTML is one of the most reliable ways to introduce XSS.

- Use a well-maintained sanitizer library. Do not roll your own.
- Use an allowlist: define exactly which tags and attributes are permitted. Everything else is stripped.
- If you can avoid HTML input entirely (for example by using Markdown), do that.
- Always sanitize before rendering. Sanitizing before storage as well adds depth, but running a sanitizer twice can change content, so test that the result is stable.

Example: user comments allow basic formatting (bold, italic, links). All HTML is sanitized to the allowlist before rendering.

Related: [Content Security Policy](#content-security-policy), [OWASP Top 10](#owasp-top-10), [16 HTTP Security Hardening](../standards/16-http-security-hardening.md).

## Subresource Integrity (SRI)

Pin external assets (scripts, stylesheets) to a specific cryptographic hash with Subresource Integrity. If the CDN delivers a different file than expected, whether through compromise or accidental change, the browser rejects it.

- Add `integrity` attributes to any CDN-hosted scripts or styles.
- Update the hash whenever you bump the version of an external asset.
- Use `crossorigin="anonymous"` alongside `integrity` for CORS-enabled CDNs.
- Better still, self-host critical scripts and styles and skip the CDN dependency entirely.

Example:

```html
<script src="https://cdn.example.com/libs/widget/1.4.2/widget.min.js"
        integrity="sha512-..."
        crossorigin="anonymous"></script>
```

Related: [Content Security Policy](#content-security-policy), [Security Headers](#security-headers).

## Rate Limiting

Limit how many requests a client can make in a given time window to protect against abuse, brute force and accidental overload.

- Apply limits per client (IP or user) and per endpoint. Anonymous endpoints, expensive endpoints (search, export) and sensitive ones such as password reset (where an app has one) should have tighter limits.
- Use token bucket or leaky bucket strategies for smooth throttling.
- Return `429 Too Many Requests` with a `Retry-After` header so clients know when to try again.
- Consider WAF-level rate limiting for broader protection against traffic spikes.

Example: the API enforces 10 requests per minute per IP on an anonymous search endpoint. Exceeding the limit returns `429` with a retry hint.

Related: [OWASP Top 10](#owasp-top-10), [16 HTTP Security Hardening](../standards/16-http-security-hardening.md), [13 Messaging and Integration](../standards/13-messaging-and-integration.md) (webhooks), [19 Testing Strategy](../standards/19-testing-strategy.md) (load tests).

## Audit Logging

Keep immutable audit logs of who did what, and when. They are distinct from application logs: not for debugging, but for accountability and investigation.

- Log who did what and when. Include enough context to reconstruct what happened.
- Protect audit logs from tampering. Append-only storage or event streams work well.
- Make logs searchable and retain them per your compliance policy.
- Do not conflate audit logs with application or debug logs. They have different purposes and different retention needs.

Example: admin role changes are recorded with the acting user, timestamp and before/after values. This log is stored separately from application logs and cannot be modified.

Related: [STRIDE Threat Modelling](#stride-threat-modelling), [18 Personal Data and GDPR](../standards/18-personal-data-and-gdpr.md), [22 Observability and Operations](../standards/22-observability-and-operations.md).

## Don't Log Personal Identifiers

Avoid logging sensitive personal data such as national identity numbers, full names combined with account details, or other personally identifiable information (PII). Logs are often less secure than your main storage and may be retained longer.

- Do not log national identity numbers, social security numbers or similar personal identifiers.
- Do not log direct identifiers (names, e-mail, phone, national identity numbers). Do not log a hash of one as a substitute.
- Use correlation IDs (request trace IDs) for troubleshooting instead of personal identifiers.
- A pseudonymous internal ID (one that cannot be turned into a person without another system) may be logged where the data inventory lists it as a logged field. Support often traces a problem through it.
- Review log outputs regularly to catch accidental PII leakage.

Example: the system logs an internal account ID listed in the data inventory and a trace correlation ID, not the national identity number. Investigations go through the trace and the internal ID. See [18 Personal Data and GDPR](../standards/18-personal-data-and-gdpr.md), rule 9.

Related: [Audit Logging](#audit-logging), [DTOs over Schema Exposure](#dtos-over-schema-exposure), [18 Personal Data and GDPR](../standards/18-personal-data-and-gdpr.md), [13 Messaging and Integration](../standards/13-messaging-and-integration.md) (never log message bodies).

## DTOs over Schema Exposure

Do not expose database entities or internal models directly through the API. Use explicit Data Transfer Objects (DTOs) for all inputs and outputs.

- Define a DTO for every API input and output. Map between DTOs and domain models explicitly.
- Keep internal models private. Changes to your database schema should not affect your API contract.
- The DTO should contain only what the client needs, not everything in the model.

Why it matters: exposing database entities directly leaks your internal structure. It makes schema changes breaking API changes, and it can accidentally expose fields that should not be visible. DTOs let you evolve storage and API contracts independently.

Example: the API returns a `UserProfileDto` with only the fields the UI needs, not the full `User` entity including password hash, internal flags and audit fields.

Related: [09 Endpoints and API Design](../standards/09-endpoints-and-api-design.md), [18 Personal Data and GDPR](../standards/18-personal-data-and-gdpr.md).

## GUIDs as IDs

Use GUIDs (or UUIDs) as primary identifiers for public-facing resources, rather than sequential integers.

- Use GUIDs as primary IDs. Sequential integers let anyone enumerate your resources by incrementing a number.
- Consider UUIDv7 when you need sortable identifiers. It is a standard UUID with time-ordering built in, which is friendlier for database indexes. ULIDs are also sortable, but they are not GUIDs and need their own column type or encoding.
- Keep internal surrogate keys if you need them, but never expose sequential IDs through the API.

Why it matters: integer IDs like `/users/1234` make it easy to enumerate all users by incrementing the number. GUIDs make this infeasible. They also enable distributed ID generation without coordination.

Example: public URLs use GUIDs, such as `/users/3fa85f64-5717-4562-b3fc-2c963f66afa6`. No enumeration is possible.

Related: [Security Principle](#security-principle), [OWASP Top 10](#owasp-top-10), [12 Data Access and Migrations](../standards/12-data-access-and-migrations.md).

## Safe SQL Access

Always use parameterized queries or query builders. Never concatenate user input into SQL strings. SQL injection is still one of the most common attack vectors.

- Use parameterized queries everywhere user input touches a query.
- Use EF Core LINQ, or raw SQL with parameters (`FromSql` with an interpolated string is parameterised; `FromSqlRaw` with concatenation is not). Dapper is safe only when values are passed as parameters and never concatenated into the SQL text.
- If you need dynamic SQL, be extremely careful and document why.
- SAST tools should catch string-concatenated SQL. Treat these findings as release blockers.

What to avoid: string interpolation in SQL, such as `$"SELECT * FROM users WHERE name = '{name}'"`. It is an injection vulnerability waiting to happen.

Example: user search uses a parameterized query, `WHERE name = @name`. The value is never concatenated into the SQL string.

Related: [OWASP Top 10](#owasp-top-10), [SAST](#sast-static-application-security-testing), [12 Data Access and Migrations](../standards/12-data-access-and-migrations.md).

## Secret Management

Secrets (database passwords, API keys, tokens, certificates) should never live in source code, committed files or unmanaged configuration. Use a managed secret store.

- Use a managed secret store (GCP Secret Manager, Azure Key Vault and similar).
- Rotate secrets regularly. If you have to rotate a secret manually, you need better automation.
- Apply least privilege: only the services that need a secret should have access to it.
- Inject secrets at runtime as environment variables. Never bake them into container images or config files.

What to avoid: secrets in source code or committed `.env` files, and secrets as plain text in CI/CD configuration.

Example: the service reads a third-party API key from GCP Secret Manager at startup. Rotation is automatic and transparent. Database access uses IAM authentication, so there is no database password to store (see [03 Infrastructure as Code](../standards/03-infrastructure-as-code.md), rule 14).

Related: [ASP.NET Core Data Protection](#aspnet-core-data-protection), [No Secrets in Frontend .env Files](#no-secrets-in-frontend-env-files), [17 Supply Chain and Security Scanning](../standards/17-supply-chain-and-security-scanning.md).

## No Secrets in Frontend .env Files

Never put secrets in frontend `.env` files. Frontend builds are public artifacts, and anything embedded at build time ends up in the JavaScript bundle, visible to anyone who downloads it.

- Treat the frontend bundle as fully public, because it is.
- Never put API keys, tokens or any secret in frontend `.env` files.
- For non-secret configuration that varies by environment (API base URLs, feature flags and so on), fetch it from a configuration endpoint at runtime instead.

Why it matters: Vite and similar tools will inline `VITE_SECRET_KEY=abc123` directly into your bundle. It is not encrypted and not hidden; it is a string in a JavaScript file anyone can read. Fetching config from an API at startup is the right approach.

Example: the frontend fetches a non-secret configuration JSON from the API on startup: API base URLs, feature flags and the environment name.

Related: [Secret Management](#secret-management), [21 Frontend](../standards/21-frontend.md).

## Dependency Scanning

Run automated dependency scanning on every repository. Most vulnerabilities in modern applications come from third-party libraries, not the code we write.

- Enable GitHub Dependabot on all repositories. It is free and easy.
- Review and patch vulnerable dependencies promptly, especially critical and high-severity findings.
- Track transitive dependencies too, not just direct ones. Vulnerabilities often hide several layers deep.
- Configure Dependabot grouped updates to reduce noise from many small PRs.

Example: Dependabot opens a PR to update a vulnerable package. CI validates that the update does not break anything.

Related: [Software Bill of Materials (SBOM)](#software-bill-of-materials-sbom), [SAST](#sast-static-application-security-testing), [17 Supply Chain and Security Scanning](../standards/17-supply-chain-and-security-scanning.md).

## SAST (Static Application Security Testing)

Run SAST in CI so every pull request is checked automatically. These tools analyze code for security vulnerabilities without running it.

- Run SAST on every pull request.
- Treat high-severity findings as release blockers. They do not merge until fixed.
- Tune rules over time to reduce false positives. Alert fatigue causes people to ignore warnings.

Example: CI fails if SAST detects SQL injection patterns or hardcoded credentials. The developer sees the finding immediately rather than in a post-release audit.

Related: [OWASP Top 10](#owasp-top-10), [Dependency Scanning](#dependency-scanning), [05 CI Pipeline](../standards/05-ci-pipeline.md), [17 Supply Chain and Security Scanning](../standards/17-supply-chain-and-security-scanning.md).

## Software Bill of Materials (SBOM)

Maintain an SBOM, a complete inventory of all software components and versions in your system, so that when a new CVE is published you can check at once whether you are affected.

- Generate an SBOM automatically as part of the build pipeline.
- Archive SBOMs alongside deployment artifacts so you can answer "what was running in production on date X".
- Use lockfiles (`pnpm-lock.yaml`, `packages.lock.json`) to make SBOMs accurate.

Example: the build pipeline produces an SBOM file for each release and archives it with the deployment record. When a vulnerability is disclosed, the team queries the SBOM to find affected services in minutes.

Related: [Dependency Scanning](#dependency-scanning), [SAST](#sast-static-application-security-testing), [17 Supply Chain and Security Scanning](../standards/17-supply-chain-and-security-scanning.md).
