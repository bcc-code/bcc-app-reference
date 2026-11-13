namespace BccCode.Template.Api.Frontend;

/// <summary>The CSP values the app sends (standard 16.2).</summary>
internal static class ContentSecurityPolicy
{
    /// <summary>
    /// The SPA shell. Vite puts the nonce on every script and stylesheet tag it emits, and the frontend passes it to PrimeVue for
    /// the theme &lt;style&gt; elements it injects (frontend/src/csp.ts), so neither needs 'unsafe-inline'. Fonts and the bundles
    /// are same-origin: the API serves them, or in Cloud Run the gateway serves /assets from the bucket under the same host.
    /// </summary>
    public static string ForShell(string nonce)
    {
        return $"default-src 'self'; script-src 'self' 'nonce-{nonce}'; style-src 'self' 'nonce-{nonce}'; img-src 'self' data:; " +
        "font-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'";
    }

    /// <summary>
    /// Every other response (API answers, problem details, static files) is never meant to render as a page, so if a browser does
    /// open one directly, nothing in it may load or run.
    /// </summary>
    public const string NO_DOCUMENT = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";

    /// <summary>
    /// Development only (Frontend:Mode=Vite). The page comes from the Vite dev server through YARP, so the API cannot put a nonce in
    /// it: script-src is 'self' only (Vite's scripts are all files), and style-src is 'unsafe-inline' because Vite injects every
    /// stylesheet as a &lt;style&gt; element and draws its error overlay with inline styles. The rest matches the shell policy, so an
    /// inline script or a third-party host still shows up as a violation in the console before it reaches a deployed environment.
    /// The Vite HMR websocket is same-origin (through YARP), which connect-src 'self' allows. The .NET hot reload script
    /// (_framework/aspnetcore-browser-refresh.js, injected when the API runs under aspire or dotnet watch) opens a websocket to a
    /// random localhost port, hence ws/wss on localhost.
    /// </summary>
    public const string VITE_DEV_SERVER =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "font-src 'self' data:; connect-src 'self' ws://localhost:* wss://localhost:*; object-src 'none'; " +
        "base-uri 'self'; frame-ancestors 'none'";
}
