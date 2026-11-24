# 16 HTTP Security Hardening

The HTTP-level controls every BFF app sets: response headers, CSP, CSRF protection, CORS, rate limiting, forwarded headers, redirects and HTML sanitization. They limit the damage of an XSS bug, a malicious site or a misbehaving client.

The headers are written by one middleware, the CSP is built per request with a nonce, and every unsafe request is refused unless it comes from the app's own origin.

## Rules

### Response headers

1. **Security headers MUST be set on every response, including errors, by one middleware that writes them in `Response.OnStarting`.** Setting them in endpoints misses error pages and static files. See [Security Headers](../principles/security.md#security-headers).
2. **The SPA shell MUST carry a CSP with a per-request nonce: `default-src 'self'; script-src 'self' 'nonce-…'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'`, plus only the extra hosts the app needs.** Styles use the same nonce: PrimeVue 4 supports a CSP nonce through its config (`csp: { nonce }`), so use it. `style-src 'unsafe-inline'` is allowed only where a library still injects `<style>` without a nonce, and the reason MUST be in a code comment. See [Content Security Policy](../principles/security.md#content-security-policy) and [CSP Example](../principles/security.md#csp-example).
3. **Existing apps SHOULD roll out CSP as `Content-Security-Policy-Report-Only` first, then enforce it.** This finds inline scripts and third-party hosts without breaking production.
4. **HSTS MUST be sent with `max-age=31536000` outside Development.** Not in Development, because it would pin HTTPS for `*.dev.localhost` for a year. Add `includeSubDomains`/`preload` only when the domain owner agrees. See [HSTS](../principles/security.md#hsts).
5. **Every response MUST carry `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `X-Frame-Options: DENY`, a `Permissions-Policy` that disables unused features (camera, microphone, geolocation and so on), `Cross-Origin-Opener-Policy: same-origin` and `Cross-Origin-Resource-Policy: same-origin`.** `X-Frame-Options` covers browsers that ignore `frame-ancestors`; COOP/CORP stop other origins from holding a window reference or embedding our responses. Widget bundles meant for other origins are the exception to CORP and must be listed.
6. **`/api` responses MUST default to `Cache-Control: no-store` when the endpoint sets nothing.** API answers are per person and must not be stored by shared caches. `index.html` MUST be served with `Cache-Control: no-store` so a deploy is picked up and a stale nonce is never reused (see [26 Gateway and Frontend Hosting](26-gateway-and-frontend-hosting.md)).
7. **The `Server` header SHOULD be removed (`builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false)`).** It tells an attacker the stack and version for free. Cloud Run may add its own `Server` header, which the app cannot remove.
8. **`index.html` MUST be served by the API, not by a bucket or CDN, so the headers and the CSP nonce apply to it.** Hashed assets can come from a bucket. See [Frontend Hosting Strategy](../principles/security.md#frontend-hosting-strategy) and [26 Gateway and Frontend Hosting](26-gateway-and-frontend-hosting.md).

### CSRF

9. **Every unsafe request (any method but GET, HEAD and OPTIONS) MUST be refused with a ProblemDetails 403 unless it comes from the app's own origin.** It does when `Sec-Fetch-Site` is `same-origin` or `none`; a browser that sends no `Sec-Fetch-Site` is judged by `Origin`, which must be absent or equal the request's scheme and host. Run it as one ASP.NET middleware after authentication, so it covers Wolverine and minimal-API endpoints alike and leaves the IdP's form post to `/signin-oidc` to the OIDC handler. ASP.NET's `UseAntiforgery` and Wolverine's `[ValidateAntiforgery]`/`RequireAntiforgeryOnAll` do not protect JSON endpoints: they only mark the endpoint, and nothing refuses a JSON request that fails validation. See [CSRF](../principles/security.md#csrf).
10. **The check MUST apply to every unsafe request, whether or not it carries the session cookie or an `Authorization` header.** An exemption based on a header lets a forged request add that header and still be authenticated by the ambient cookie.
11. **`Sec-Fetch-Site: same-site` MUST be refused.** `SameSite=Lax` keeps other sites' requests from carrying the session cookie, but every app under the same registrable domain (for example `*.bcc.no`) is the same site, so only the origin check stops a form posted from one of them.
12. **Antiforgery tokens SHOULD NOT be added on top of the origin check.** Browsers send `Sec-Fetch-Site`, and `Origin` on cross-origin POSTs, so a token adds a token endpoint, a cookie and retry logic in the frontend without stopping another request. The .NET 11 cross-origin middleware works the same way; switch to it when the app moves to .NET 11.

### CORS

13. **A same-origin BFF MUST NOT enable CORS.** The SPA is served from the API, so no cross-origin access is needed. See [CORS](../principles/security.md#cors).
14. **Where cross-origin access is needed (embeddable widgets), CORS MUST apply only to `/api`, use an explicit origin allowlist validated at startup, fail closed outside Development when the list is empty or malformed, and not allow credentials unless required.** Parse each entry as `scheme://host[:port]`, reject wildcards, and throw when the list is empty outside Development.

### Rate limiting, proxies, redirects

15. **Endpoints that start expensive work (calls to paid or slow dependencies, exports, searches) MUST be rate limited per person (when authenticated) or per IP, returning 429 with `Retry-After` and a ProblemDetails body; cheap anonymous endpoints (a redirect to the IdP, a signature check) need no app-level limit.** In-memory counters are per instance and the IP comes from forwarded headers, so an app limit only stops a stuck client or a simple script; floods belong to the gateway. Write these limits next to the configuration. See [Rate Limiting](../principles/security.md#rate-limiting).
16. **Forwarded headers MUST be enabled behind the platform's proxies, so the app sees the original scheme and host. Code that uses the client IP (rate limiting, audit logs, per-IP rules) MUST first trust forwarded headers only from known proxy networks, with a hop count that matches the header shape the proxy chain really produces, and invalid configuration MUST fail startup.** Validate a CIDR list and throw on a bad entry. On Cloud Run, where the front proxy has no fixed address, the accepted alternative is ingress restricted to the load balancer plus a hop count for the documented header shape, explained in a code comment. For that case, set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` in the deployment instead of writing the middleware setup: the framework then reads `X-Forwarded-For` and `X-Forwarded-Proto` with no known proxies, and the app only sets `ForwardLimit`. Google documents the external Application Load Balancer's `X-Forwarded-For` as `<supplied-value>,<client-ip>,<load-balancer-ip>`, so `ForwardLimit = 1` leaves the app with the load balancer's address and breaks per-IP rate limiting. Use `ForwardLimit = 2` with the load balancer and Google front-end ranges as known networks, or a parser that takes the client entry. Add a test with the real header shape, and verify once on a deployed app by logging `RemoteIpAddress`.
17. **Every redirect target that comes from the request (return URLs, post-logout URLs) MUST be checked as local before use.** Use `RedirectHttpResult.IsLocalUrl`, or `Results.LocalRedirect` where the endpoint returns the redirect itself, as described in [14 Authentication and Sessions](14-authentication-and-sessions.md). Do not write your own check.
18. **User-provided HTML MUST be sanitized with an allowlist sanitizer before it is rendered, SHOULD also be sanitized before it is stored, and `v-html` on user content MUST be blocked by a lint rule unless it goes through the sanitizer.** Sanitizing on render is the control that counts; sanitizing on store as well adds depth, but running a sanitizer twice can alter content, so test that the output is stable. See [HTML Sanitization](../principles/security.md#html-sanitization).

## Examples

### Headers on every response (rules 1, 4, 5, 6)
```csharp
// context.Request.IsHttps below is only correct after UseForwardedHeaders has run (rule 16),
// because the app sits behind a proxy that terminates TLS.
var hsts = !environment.IsDevelopment();
return app.Use((context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        if (hsts && context.Request.IsHttps) headers.StrictTransportSecurity = "max-age=31536000";
        if (AuthConfiguration.IsApiRequest(context.Request) && !headers.ContainsKey("Cache-Control"))
            headers.CacheControl = "no-store";
        return Task.CompletedTask;
    });
    return next(context);
});
```

Add `X-Frame-Options`, `Permissions-Policy`, COOP and CORP to the same block to satisfy rule 5.

### CSP with nonce (rule 2)
A middleware that serves `index.html` generates a fresh 16-byte nonce per request, puts it in the CSP header and injects it into the script tags:

```csharp
// Styles use the same nonce, which the frontend passes to PrimeVue (csp: { nonce }).
// Only add 'unsafe-inline' to style-src with a comment that names the library that needs it.
public static string For(string nonce, IReadOnlyList<string> extraConnectHosts)
{
    var connect = string.Join(' ', ["'self'", .. extraConnectHosts]);
    return $"default-src 'self'; script-src 'self' 'nonce-{nonce}'; style-src 'self' 'nonce-{nonce}'; img-src 'self' data:; " +
           $"font-src 'self' data:; connect-src {connect}; object-src 'none'; base-uri 'self'; frame-ancestors 'none'";
}
```

### Cross-origin check (rules 9 to 11)
```csharp
app.Use(async (context, next) =>
{
    var request = context.Request;
    var site = request.Headers["Sec-Fetch-Site"].ToString();
    var origin = request.Headers.Origin.ToString();
    var sameOrigin = site.Length > 0
        ? site is "same-origin" or "none"
        : origin.Length == 0 || string.Equals(origin, $"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase);
    if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method) || sameOrigin)
        await next(context);
    else
        await Results.Problem(statusCode: 403, title: "Cross-origin request refused.").ExecuteAsync(context);
});
```
Cover it with tests for `cross-site`, `same-site`, a foreign `Origin` without fetch metadata, a `Bearer` header, every unsafe method, and a server-to-server call with neither header.

### Forwarded headers on Cloud Run behind the load balancer (rule 16)
The deployment sets `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, only when ingress is restricted to the load balancer:

```hcl
ASPNETCORE_FORWARDEDHEADERS_ENABLED = tostring(local.api_ingress == "INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER")
```

The framework then adds the middleware first in the pipeline. The app sets the hop count:

```csharp
// Google documents X-Forwarded-For as <supplied-value>,<client-ip>,<load-balancer-ip>,
// so two hops are needed to reach the client IP. ForwardLimit = 1 would give the LB address.
builder.Services.Configure<ForwardedHeadersOptions>(o => o.ForwardLimit = 2);
```

### Forwarded headers from known CIDRs (rule 16)
Where the proxies have fixed addresses, the app configures the middleware itself:

```csharp
options.KnownIPNetworks.Clear();
options.KnownProxies.Clear();
if (trustedProxyNetworks.Length > 0)
{
    foreach (var cidr in trustedProxyNetworks)
    {
        // Fail fast on invalid security configuration to avoid silently running in an unsafe state.
        if (!System.Net.IPNetwork.TryParse(cidr, out var network))
            throw new InvalidOperationException($"Invalid CIDR '{cidr}' in AppSettings:TrustedProxyNetworks.");
        options.KnownIPNetworks.Add(network);
    }
}
else if (!environment.IsDevelopment())
{
    throw new InvalidOperationException("AppSettings:TrustedProxyNetworks must not be empty outside Development.");
}
```

Outside Development the list must not be empty; an empty list must fail startup instead of trusting any source. A test sends the real header shape (`spoofed, client, lb`) and asserts that `RemoteIpAddress` is the client address. Verify the same on a deployed app once by logging `RemoteIpAddress` and the raw header.

### CORS allowlist validation (rules 13, 14)
A `ParseOrigin` function accepts only `scheme://host[:port]` and rejects wildcards. The CORS middleware is added to `/api` only and does not allow credentials. A configuration test covers the empty, malformed and valid lists.

### Rate limiting (rule 15)
Fixed windows per person or IP, 429 with `Retry-After` and a ProblemDetails body, and a doc comment stating the per-instance and forwarded-IP limits.

## Related
- [14 Authentication and Sessions](14-authentication-and-sessions.md) — session cookie, logout and return URLs
- [15 Authorization and Permission Checks](15-authorization-and-permission-checks.md) — who may call an endpoint
- [21 Frontend](21-frontend.md) — fetch wrapper, `v-html` lint rule
- [09 Endpoints and API Design](09-endpoints-and-api-design.md) — ProblemDetails for 400 and 429
- [26 Gateway and Frontend Hosting](26-gateway-and-frontend-hosting.md) — where `index.html` is served
- [Security principles](../principles/security.md) — headers, CSP, HSTS, CSRF, CORS, rate limiting, HTML sanitization and SRI
