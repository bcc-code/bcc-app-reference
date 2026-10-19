---
standard: 16 HTTP Security Hardening
assessed: 2026-10-19
commit: e4b2b86
updated: 2026-10-19 (gaps 1–4 of the index fixed)
---
# 16 HTTP Security Hardening — assessment

Headers, nonce CSP, CSRF (no bearer exemption), Strict antiforgery cookie, rate limiting with 429 problems and local-redirect
checks are implemented and tested in both variants. Static-file serving still runs before authentication, but it now skips
`/index.html` in any letter case, so the shell only comes from the middleware that requires a session and sets the nonce CSP;
a test in both variants covers it. The health probes are not rate limited, and no lint rule blocks `v-html`.

**Score: Strong** · ✅ 14 · ⚠️ 2 · ❌ 0 · N/A 2

## Rules

| # | Rule (short) | Level | Status | Evidence |
|---|---|---|---|---|
| 1 | Security headers on every response via one `OnStarting` middleware | MUST | ✅ | `api/BccCode.Template.Web/Http/SecurityHeaders.cs:9-33`, registered after the exception handler and before everything else (`TemplateWeb.cs:39`). Test: `SecurityTests.Every_response_carries_the_security_headers_errors_included` (200, 401, 404; both variants) |
| 2 | SPA shell CSP with per-request nonce; `unsafe-inline` styles explained | MUST | ✅ | `Frontend/ContentSecurityPolicy.cs:5-10` (PrimeVue reason in comment); nonce per request in `IndexHtmlMiddleware.cs:31-36`. Tests: `SecurityTests.The_shell_needs_a_session_and_carries_a_nonce_csp`, `IndexHtmlTests` (2). Vite mode (local dev only) sends no CSP (`IndexHtmlMiddleware.cs:35-36`). See rule 8 for `/index.html` |
| 3 | Existing apps roll out CSP as Report-Only first | SHOULD | N/A | A template has no production app; the comment at `ContentSecurityPolicy.cs:6-7` tells adopters with an existing app to start in Report-Only |
| 4 | HSTS `max-age=31536000` outside Development | MUST | ✅ | `SecurityHeaders.cs:11-13,25` (no `includeSubDomains`/`preload`, reason stated). Test: `SecurityTests.Hsts_is_sent_over_https_outside_development` |
| 5 | nosniff, Referrer-Policy, X-Frame-Options, Permissions-Policy, COOP, CORP | MUST | ✅ | `SecurityHeaders.cs:19-24`; asserted in `Every_response_carries_the_security_headers_errors_included` |
| 6 | `/api` defaults to `Cache-Control: no-store` | MUST | ✅ | `SecurityHeaders.cs:26-28`; asserted in the same test |
| 7 | `Server` header removed | MUST | ✅ | `TemplateWeb.cs:17` `AddServerHeader = false`; asserted (`headers.ContainsKey("Server").Should().BeFalse()`) |
| 8 | `index.html` served by the API so headers and nonce apply | MUST | ✅ | The shell is served by `IndexHtmlMiddleware` for client routes. In Static mode `UseStaticFiles` still runs before routing, authentication and authorization (`TemplateWeb.cs:40-45`), but it is wrapped in `UseWhen(!IsIndexHtml(path))` with a case-insensitive comparison (`Frontend/FrontendConfiguration.cs:37-51`), so `/index.html` never reaches the file. Test: `SecurityTests.The_built_index_html_is_never_served_as_a_static_file` (both variants): anonymous `/index.html` and `/INDEX.HTML` get 302 to `/user/login`, a signed-in request gets 404. The test failed (200) before the fix. A live run in the E2E topology returned 302 for anonymous `/index.html`, and E2E passed 6/6 |
| 9 | Double-submit antiforgery on every unsafe cookie request | MUST | ✅ | `Http/AntiforgeryMiddleware.cs:22-34`, added to every Wolverine chain (`EndpointConventions.cs:15`) and called by `/user/logout`. Tests: `CsrfTests.An_unsafe_request_with_the_session_cookie_and_no_token_is_refused_with_a_refresh_hint`, `An_unsafe_request_with_a_valid_token_goes_through`, `A_safe_request_with_the_session_cookie_needs_no_token` |
| 10 | No exemption for a `Bearer` header | MUST | ✅ | Exemption only when the session cookie is absent (`AntiforgeryMiddleware.cs:27-28`). Test: `CsrfTests.A_bearer_header_does_not_exempt_a_request_that_has_the_session_cookie` |
| 11 | Failed check → ProblemDetails 400 + `X-Csrf-Refresh: 1`; client retries once on it only | MUST | ✅ | `AntiforgeryMiddleware.cs:32-33`; `frontend/src/lib/templateFetch.ts:54-57`. Tests: the refresh-hint test above; vitest `refreshes the token and retries once…`, `retries only once`, `does not retry a 400 without the refresh header` |
| 12 | Antiforgery cookie `SameSite=Strict` | MUST | ✅ | `AuthConfiguration.cs:67-73`; readable `XSRF-TOKEN` also Strict (`AuthEndpoints.cs:43-49`). Test: `CsrfTests.The_antiforgery_cookie_is_strict_and_the_request_token_is_readable_by_script` |
| 13 | Same-origin BFF does not enable CORS | MUST | ✅ | No `AddCors`/`UseCors` in `api/` (grep) |
| 14 | Cross-origin CORS only with validated allowlist | MUST | N/A | No cross-origin consumers |
| 15 | Anonymous/expensive endpoints rate limited, 429 + Retry-After + problem, limits documented | MUST | ⚠️ | `/user/login`, `/user/antiforgery-token`, `/user/backchannel-logout` use the per-IP limiter (`Http/RateLimiting.cs:21-60`, limits and IP caveat documented at `:13-20`). Test: `SecurityTests.The_anonymous_routes_are_rate_limited_with_a_retry_after_and_a_problem`. `/health` and `/alive` are anonymous and not rate limited; they are output-cached for 10 s with a 5 s timeout instead (`api/ServiceDefaults/Extensions.cs:24-26,73-76`) |
| 16 | Forwarded headers only from known proxies, or LB-only ingress + `ForwardLimit = 1` documented | MUST | ✅ | `Http/ForwardedHeaders.cs:5-21` (accepted Cloud Run alternative, documented); `infra/cloudrun_api.tf:32` `INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER`. Previews use `INGRESS_TRAFFIC_ALL` (`infra/preview/main.tf:37`, marked TODO) |
| 17 | Request-derived redirect targets checked as local | MUST | ✅ | `Http/LocalRedirectGuard.cs`, used for `/user/login` (`AuthEndpoints.cs:25`) and the Auth0 `returnTo` (`AuthConfiguration.cs:156`). Tests: `LocalRedirectGuardTests` (incl. `//`, `/\`, tab and CRLF cases) |
| 18 | User HTML sanitized; `v-html` blocked by lint | MUST | ⚠️ | No user HTML and no `v-html` in `frontend/src` (grep). No lint rule blocks it: `frontend/eslint.config.ts` uses `pluginVue.configs['flat/essential']`, which does not include `vue/no-v-html`, and `.oxlintrc.json` does not enable it |

## Gaps to close

- **15**: Either put `/health` and `/alive` behind the anonymous rate limiter, or record in a comment next to the probe mapping
  (`api/ServiceDefaults/Extensions.cs`) why output caching and the timeout are the accepted control for these two routes.
- **18**: Enable `vue/no-v-html` (error) in `frontend/eslint.config.ts`, with a documented exception only for content that
  goes through a sanitizer.

## Not verifiable in the repo

- That the external load balancer is the only ingress and that it sets `X-Forwarded-Proto`/`X-Forwarded-For` as
  `ForwardedHeaders.cs` assumes; check the gateway module configuration and the Cloud Run service in GCP.
- That previews are not publicly reachable once the TODO in `infra/preview/main.tf:35-37` is done.
