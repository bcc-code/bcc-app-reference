using System.Security.Cryptography;

namespace BccCode.Template.Api.Frontend;

/// <summary>
/// Serves the SPA shell with a fresh CSP nonce per response (standard 16.2). It is an endpoint, not a static file, so
/// <c>UseStaticFiles</c> never sees index.html: the static file middleware skips requests that matched an endpoint. The endpoint is under
/// the fallback policy, so a signed-out person is sent straight to the identity provider before any JavaScript loads.
/// </summary>
internal sealed class IndexHtml(string path)
{
    /// <summary>Written by Vite on every script and stylesheet tag and on the csp-nonce meta tag (html.cspNonce in vite.config.ts).</summary>
    internal const string NONCE_PLACEHOLDER = "__CSP_NONCE__";

    // Never cached, so a deploy is picked up at once and a nonce is never reused (16.6).
    private const string NO_CACHE = "no-store, no-cache, must-revalidate, max-age=0";

    // The file only changes with a new image, so it is read once. A build without the placeholder would load no script
    // under the CSP, so it fails every request loudly instead.
    private readonly Lazy<string> _template = new(() =>
    {
        var html = File.ReadAllText(path);
        return html.Contains(NONCE_PLACEHOLDER, StringComparison.Ordinal)
            ? html
            : throw new InvalidOperationException($"{path} has no {NONCE_PLACEHOLDER}; check html.cspNonce in vite.config.ts.");
    });

    public Task WriteAsync(HttpContext context)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var response = context.Response;
        response.ContentType = "text/html; charset=utf-8";
        response.Headers.CacheControl = NO_CACHE;
        response.Headers.ContentSecurityPolicy = ContentSecurityPolicy.ForShell(nonce);
        return response.WriteAsync(_template.Value.Replace(NONCE_PLACEHOLDER, nonce, StringComparison.Ordinal), context.RequestAborted);
    }
}
