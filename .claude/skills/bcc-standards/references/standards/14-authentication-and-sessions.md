# 14 Authentication and Sessions

How our web apps sign users in, keep sessions, and authenticate machine callers. Web apps use the [Backend-for-Frontend (BFF)](../principles/security.md#backend-for-frontend-bff) pattern: the .NET API serves the SPA, runs the OIDC flow and holds the session in a cookie the browser cannot read.

The pattern combines a BFF cookie with OIDC code flow, slim claims, caller-specific bearer schemes for machine callers, internal service authentication and MFA step-up for administrators.

## Rules

### Sign-in flow

1. **The SPA MUST authenticate through the API as a BFF, using the OIDC authorization code flow with PKCE and PAR (`PushedAuthorizationBehavior.Require` where the identity provider supports PAR; `UseIfAvailable` only where it does not, and then document that PAR is missing).** Tokens never reach the browser, and PAR keeps authorization parameters out of the front-channel URL. `UseIfAvailable` falls back silently, so it is not a way to enforce PAR. See [OIDC](../principles/security.md#oidc-openid-connect) and [OAuth PAR](../principles/security.md#oauth-par-pushed-authorization-requests).
2. **`SaveTokens` MUST be `false` unless the backend calls APIs on the user's behalf; then tokens MUST be kept in a server-side store, never in the cookie.** A token in the cookie lives as long as the cookie and can be replayed if the cookie leaks.
3. **Set `MapInboundClaims = false`, request only the scopes the app uses, and slim the principal to the claims the app reads before the session is written.** Fewer claims mean a smaller cookie or ticket, less personal data at rest, and no surprise claim-type remapping. A small `SessionClaims.Slim` helper that keeps only the needed claims is enough. Do not request `offline_access` when no refresh token is used.
4. **Administrators SHOULD be stepped up to MFA before any session cookie is written.** An `OnTokenValidatedStepUp` handler suppresses the first sign-in for admins and re-challenges requesting MFA through `acr_values`, with the decision in a pure, unit-tested function (`ShouldStepUpAdmin`). The `acr_values` value is specific to the identity provider; use its documented MFA value. Check the `amr` and `acr` claims of the returned token to confirm MFA was done, and do not rely on a custom scope for this.
5. **Startup MUST fail when OIDC authority, client id, client secret or the bearer audience is missing outside Development/Test.** A missing audience silently disables audience validation. Cover this with a test and throw from the OIDC configuration (for example in `ConfigureOidc`).

### Session cookie and store

6. **The session cookie MUST be an opaque `__Host-` cookie with `HttpOnly`, `Secure=Always` and `SameSite=Lax`.** `__Host-` forbids `Domain` and requires `Path=/` and HTTPS. See [Auth Sessions (Cookies)](../principles/security.md#auth-sessions-cookies).
7. **Sessions MUST be stored server side (an `ITicketStore`), so the cookie only carries a key and a session can be revoked.** With a stateless ticket, a stolen cookie is valid until it expires, and revoking one session means rotating all Data Protection keys. A session store can protect the serialised ticket with Data Protection and keep it in Postgres. For EF Core apps the same store can be a table.
8. **Session lifetime MUST be short with sliding expiry, and the chosen value documented with its reason.** Choose the value per app, make it sliding, and write down the reason. For example, a few minutes to an hour for a short self-service flow, or 8 to 12 hours for back-office work. Shorter limits the window for a stolen session; longer means fewer re-logins. With a server-side store, a longer sliding window is acceptable because sessions can be revoked.
9. **When the identity provider supports OIDC back-channel logout, the app MUST expose an endpoint that validates the signed logout token and deletes the sessions by `iss` + `sid`, or by `iss` + `sub` when the token has no `sid`.** Capture `sid`, `sub` and `iss` as claims in `OnTokenValidated` so the store can find them. Where the provider does not support it, rely on the short session lifetime from rule 8 and record that in an ADR. See [Backchannel Logout](../principles/security.md#backchannel-logout).
10. **Logout MUST be a POST, so the cross-origin check covers it ([16](16-http-security-hardening.md) rule 9).** A GET logout can be triggered from any site with an image tag.
11. **The browser-facing "who am I" endpoint MUST return only the fields the SPA needs, not the full claim list.**

### API behaviour

12. **Requests under `/api` MUST get 401/403 instead of redirects, both from the cookie handler and from `OnRedirectToIdentityProvider`.** A redirect to the IdP is useless to `fetch` and hides the real status.
13. **The SPA MUST handle 401 by navigating to the login endpoint with a return URL, and the server MUST validate that URL as local with `RedirectHttpResult.IsLocalUrl` and fall back to `/`.** The built-in rejects absolute URLs, `//host`, `/\host` and control characters; an integration test of the login endpoint with such URLs MUST show it. This prevents open redirects through the login flow. A shared fetch wrapper in the SPA does this for every call.
14. **Data Protection keys MUST be kept in shared durable storage (Postgres or a GCS bucket) and encrypted with Cloud KMS.** The cookies and the ticket store depend on them. See [ASP.NET Core Data Protection](../principles/security.md#aspnet-core-data-protection).

### Machine callers

15. **Each machine caller (M2M client, widget, webhook) MUST have its own JWT bearer scheme with a mandatory audience and an allowlist of clients or scopes, and only the policies that need it MAY accept it.** For example, a widget scheme accepts RS256 only, checks `azp`/`client_id` against `Widget:AllowedClients`, and is accepted by the widget policy only; admin policies are cookie-only.
16. **Internal service-to-service calls (Pub/Sub push, Cloud Tasks, Cloud Scheduler) MUST be authenticated with Google-signed OIDC tokens, one policy per caller.** Each receiver verifies the token's audience (its own URL) and the calling service account (`email` claim). Network position is not trust. See [Zero Trust](../principles/security.md#zero-trust).
17. **No policy SHOULD accept both a session cookie and a bearer token. Where a policy does accept both, a request that carries a session cookie and a bearer token for a different person MUST be rejected with 401.** Otherwise a policy that accepts both schemes merges two identities. A middleware (for example a widget session guard) enforces this.
18. **Test-only authentication (impersonation headers, load-test tokens) MUST be off by default, enabled only by explicit config, limited to an environment allowlist, and MUST refuse to start in production.** Check the allowlist in C# and in a Terraform precondition, and throw when the feature is enabled in Prod.

## Examples

### BFF cookie and OIDC (rules 1, 2, 6, 8)
Cookie and OIDC options for the BFF (excerpt):

```csharp
options.Cookie.Name = SessionCookieName;            // "__Host-BFF.Session"
options.Cookie.HttpOnly = true;
options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
options.Cookie.SameSite = SameSiteMode.Lax;
options.SlidingExpiration = true;
options.ExpireTimeSpan = TimeSpan.FromMinutes(30); // chosen per app, see rule 8
...
options.ResponseType = OpenIdConnectResponseType.Code;
options.UsePkce = true;
options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Require; // UseIfAvailable only if the IdP has no PAR
options.SaveTokens = false;
options.GetClaimsFromUserInfoEndpoint = false;
```

The server-side store is registered with `AddSingleton<SessionStore>()` and wired into the cookie options by a post-configure class.

### Back-channel logout (rules 7, 9)
```csharp
userGroup.MapPost("backchannel-logout", async (HttpContext context,
    IBackchannelLogoutTokenValidator validator, ILogger<...> logger, SessionStore sessionStore) =>
{
    context.Response.Headers.CacheControl = "no-cache, no-store";
    if (!context.Request.HasFormContentType) return Results.BadRequest(...);
    var form = await context.Request.ReadFormAsync();
    var result = await validator.ValidateAsync(form["logout_token"].FirstOrDefault(), context.RequestAborted);
    if (!result.IsValid) return Results.BadRequest(...);
    var token = result.Token!;
    if (!string.IsNullOrEmpty(token.Sid))
        await sessionStore.RemoveByAuthSidAsync(token.Issuer, token.Sid);
    else if (!string.IsNullOrEmpty(token.Subject))
        await sessionStore.RemoveByAuthSubjectAsync(token.Issuer, token.Subject);
    else
        return Results.BadRequest(...); // a logout token needs sid or sub
    return Results.Ok();
})
.AllowAnonymous()
.ExcludeFromDescription();
```

Test the token validator on its own (valid, expired, wrong audience, token with only `sub`, token with neither `sid` nor `sub`) and test the endpoint through the integration test host.

### Slim claims, mandatory config, caller policies (rules 3, 5, 15, 16)
One policy per internal caller. The audience is the receiver's own URL and the `email` claim is the calling service account:

```csharp
foreach (var (policy, audience, email) in callers)
    authorization.AddPolicy(policy, p => p
        .AddAuthenticationSchemes(GoogleOidcScheme)
        .RequireAuthenticatedUser()
        .RequireClaim("aud", audience)
        .RequireClaim("email", email)
        .RequireClaim("email_verified", "true"));
```

When the host runs a JasperFx command instead of serving requests, every internal policy is registered as `RequireAssertion(_ => false)`, so a misdetected host fails closed.

### MFA step-up (rule 4)
Hook `OnTokenValidated` with a step-up handler. The decision `ShouldStepUpAdmin(principal, amr)` is a pure function with unit tests; the handler suppresses the sign-in and returns a challenge that requests MFA through `acr_values` when it is true. After the second sign-in, the `amr`/`acr` claims confirm MFA before the session cookie is written.

### KMS-encrypted Data Protection keys (rule 14)
Keys are persisted in shared durable storage and wrapped with a Cloud KMS key before they are written. One option is Google's `Google.Cloud.AspNetCore.DataProtection.Kms` package (`ProtectKeysWithGoogleKms`); if the team uses its own `IXmlEncryptor` instead, it is in-house code and needs its own tests.

```csharp
services.AddDataProtection()
    .SetApplicationName("my-app")
    .PersistKeysToDbContext<DataProtectionKeysContext>() // shared durable storage (Postgres), here via EF Core
    .ProtectKeysWithGoogleKms(kmsKeyName);               // Cloud KMS wraps each key before it is stored
```

A GCS bucket works equally well as the key store (`PersistKeysToGoogleCloudStorage` from `Google.Cloud.AspNetCore.DataProtection.Storage`). Verify the package and method names against the current package versions.

## Related
- [15 Authorization and Permission Checks](15-authorization-and-permission-checks.md) — what an authenticated caller may do
- [16 HTTP Security Hardening](16-http-security-hardening.md) — CSRF, headers and CORS
- [18 Personal Data and GDPR](18-personal-data-and-gdpr.md) — keeping claims and sessions minimal
- [02 Local Development](02-local-development.md) — OIDC mocks for local sign-in
- [Security principles](../principles/security.md) — BFF, OIDC, PAR, cookies, back-channel logout, short-lived tokens, Data Protection, zero trust
