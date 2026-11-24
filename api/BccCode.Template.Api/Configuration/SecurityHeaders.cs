using BccCode.Template.Api.Auth;
using BccCode.Template.Api.Frontend;

namespace BccCode.Template.Api.Configuration;

/// <summary>
/// Standard 16.1 to 16.7. The headers are written in <c>OnStarting</c>, so they are on every response, whatever produced
/// it: endpoints, static files, the Vite proxy, error pages. The SPA shell sets its own CSP with a nonce
/// (<see cref="IndexHtml"/>) and hashed assets their own Cache-Control; both are kept because the defaults here only fill
/// in what is missing.
/// </summary>
public static class SecurityHeaders
{
    private const string PERMISSIONS_POLICY =
        "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";

    // 16.7. Cloud Run adds its own Server header to responses through run.app; the app cannot remove that one.
    public static void AddTemplateSecurityHeaders(this WebApplicationBuilder builder)
    {
        builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);
        // No includeSubDomains and no preload (the defaults): the owner of the domain has not agreed to either.
        builder.Services.AddHsts(o => o.MaxAge = TimeSpan.FromDays(365));
    }

    /// <summary>First in the pipeline, so static files and short-circuited responses get the headers too.</summary>
    public static void UseTemplateSecurityHeaders(this WebApplication app)
    {
        // Not in Development: it would pin HTTPS for *.dev.localhost for a year (16.4). The middleware sends nothing on
        // plain http or for localhost. Behind Cloud Run, Request.IsHttps is true only with forwarded headers
        // (ASPNETCORE_FORWARDEDHEADERS_ENABLED in infra/cloudrun_api.tf).
        if (!app.Environment.IsDevelopment()) app.UseHsts();

        var defaultCsp = FrontendConfiguration.IsVite(app.Configuration)
            ? ContentSecurityPolicy.VITE_DEV_SERVER
            : ContentSecurityPolicy.NO_DOCUMENT;

        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = PERMISSIONS_POLICY;
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                headers["Cross-Origin-Resource-Policy"] = "same-origin";
                if (!headers.ContainsKey("Content-Security-Policy")) headers.ContentSecurityPolicy = defaultCsp;
                // /api answers are per person, so no shared cache may store them unless the endpoint says otherwise (16.6).
                if (AuthConfiguration.IsApiRequest(context.Request) && !headers.ContainsKey("Cache-Control"))
                    headers.CacheControl = "no-store";
                return Task.CompletedTask;
            });
            return next(context);
        });
    }
}
