namespace BccCode.Template.Web.Frontend;

public static class ContentSecurityPolicy
{
    // style-src allows inline styles only because PrimeVue injects <style> elements at runtime. Remove it when it stops.
    // New apps that were never in production can enforce at once; an existing app should send this as
    // Content-Security-Policy-Report-Only first to find inline scripts and third-party hosts.
    public static string For(string nonce) =>
        $"default-src 'self'; script-src 'self' 'nonce-{nonce}'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "font-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'";
}
