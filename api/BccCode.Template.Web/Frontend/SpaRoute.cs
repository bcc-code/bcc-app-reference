namespace BccCode.Template.Web.Frontend;

public static class SpaRoute
{
    private static readonly string[] ReservedPrefixes =
        ["/api", "/user", "/health", "/alive", "/openapi", "/signin-oidc", "/signout-callback-oidc", "/assets"];

    /// <summary>A browser navigation to a client-side route: the SPA shell answers it.</summary>
    public static bool IsSpaRoute(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)) return false;
        if (request.HttpContext.WebSockets.IsWebSocketRequest) return false;
        var path = request.Path.Value ?? "/";
        if (path.StartsWith("/@", StringComparison.Ordinal) || path.StartsWith("/__", StringComparison.Ordinal)
            || path.StartsWith("/node_modules", StringComparison.Ordinal)) return false;   // Vite internals
        if (ReservedPrefixes.Any(prefix => request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))) return false;
        if (Path.HasExtension(path)) return false;
        return request.Headers.Accept.Any(value => value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true);
    }
}
