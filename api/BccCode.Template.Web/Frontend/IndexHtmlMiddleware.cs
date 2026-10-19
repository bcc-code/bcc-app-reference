using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Model;

namespace BccCode.Template.Web.Frontend;

/// <summary>
/// Serves the SPA shell from the API, never from a bucket or CDN, so the security headers and the CSP nonce apply to it
/// and sign-in is enforced before any JavaScript loads. It answers browser navigations to client-side routes; assets and
/// the proxied Vite dev server go on to the rest of the pipeline.
/// </summary>
public sealed class IndexHtmlMiddleware(RequestDelegate next, IOptions<FrontendOptions> options)
{
    public async Task InvokeAsync(HttpContext context, IIndexHtmlTemplate template)
    {
        var endpoint = context.GetEndpoint();
        var unmatchedOrProxied = endpoint is null || endpoint.Metadata.GetMetadata<RouteModel>() is not null;
        if (!unmatchedOrProxied || !SpaRoute.IsSpaRoute(context.Request))
        {
            await next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            var returnUrl = context.Request.Path + context.Request.QueryString;
            context.Response.Redirect("/user/login?returnUrl=" + Uri.EscapeDataString(returnUrl));
            return;
        }

        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var html = IndexHtmlRenderer.AddNonce(await template.GetAsync(context.RequestAborted), nonce);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        // Vite's dev client injects inline scripts, so the dev server runs without the CSP.
        if (options.Value.Mode != FrontendMode.Vite) context.Response.Headers.ContentSecurityPolicy = ContentSecurityPolicy.For(nonce);
        await context.Response.WriteAsync(html, context.RequestAborted);
    }
}
