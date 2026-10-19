namespace BccCode.Template.Web.Http;

/// <summary>
/// Headers on every response, error responses included: they are set in <c>OnStarting</c>, so whatever produces the
/// response, they are there. The SPA shell adds its own CSP and <c>no-store</c> in <c>IndexHtmlMiddleware</c>.
/// </summary>
public static class SecurityHeaders
{
    public static IApplicationBuilder UseTemplateSecurityHeaders(this IApplicationBuilder app, IHostEnvironment environment)
    {
        // HSTS would pin https for template.dev.localhost (and the browser's view of *.localhost) for a year, so it is not
        // sent in Development. No includeSubDomains and no preload: the domain owner has not agreed to either.
        var hsts = !environment.IsDevelopment();
        return app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                headers["Cross-Origin-Resource-Policy"] = "same-origin";
                if (hsts && context.Request.IsHttps) headers.StrictTransportSecurity = "max-age=31536000";
                // /api answers are per person, so no shared cache may store them unless the endpoint says otherwise.
                if (Auth.AuthConfiguration.IsApiRequest(context.Request) && !headers.ContainsKey("Cache-Control"))
                    headers.CacheControl = "no-store";
                return Task.CompletedTask;
            });
            return next(context);
        });
    }
}
