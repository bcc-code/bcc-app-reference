namespace BccCode.Template.Web.Http;

/// <summary>Every redirect target that comes from a request (return URLs) goes through here before use.</summary>
public static class LocalRedirectGuard
{
    // Browsers strip tab and newline from URLs, so "/\t/evil.com" would become "//evil.com". Reject control characters outright.
    public static bool IsLocal(string? url) =>
        !string.IsNullOrEmpty(url)
        && url[0] == '/'
        && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))
        && !url.Any(char.IsControl);

    public static string SafeOrRoot(string? url) => IsLocal(url) ? url! : "/";
}
