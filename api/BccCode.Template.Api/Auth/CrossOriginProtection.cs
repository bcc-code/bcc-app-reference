namespace BccCode.Template.Api.Auth;

/// <summary>
/// CSRF check for unsafe methods (standard 16.9): only a request from our own origin may change state. Browsers mark every
/// request with Sec-Fetch-Site; without it (an older browser) the Origin header decides, and a request with neither is not from
/// a web page (the identity provider's back-channel logout). This also refuses the other apps on the same site, which the Lax
/// session cookie does not. There are no tokens to fetch or send. It runs after authentication, which has already answered the
/// identity provider's cross-site form post to /signin-oidc.
/// </summary>
/// <remarks>
/// Copied from bcc-ateam. ASP.NET Core 11.0 has no public API for this: its Antiforgery assembly holds an internal
/// CsrfProtection class, and the checks of the preview issue (dotnet/aspnetcore#65127) are not released API. Replace this when
/// a release exposes it.
/// </remarks>
public static class CrossOriginProtection
{
    public static void UseTemplateCrossOriginProtection(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (IsSafe(context.Request.Method) || IsSameOrigin(context.Request))
            {
                await next(context);
                return;
            }

            await Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Cross-origin request refused.").ExecuteAsync(context);
        });
    }

    private static bool IsSafe(string method)
    {
        return HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
    }

    private static bool IsSameOrigin(HttpRequest request)
    {
        var site = request.Headers["Sec-Fetch-Site"].ToString();
        // "none" is a navigation the person started (address bar, bookmark), not one a page caused.
        if (site.Length > 0) return site is "same-origin" or "none";
        var origin = request.Headers.Origin.ToString();
        return origin.Length == 0 || string.Equals(origin, $"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase);
    }
}
