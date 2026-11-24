using Microsoft.AspNetCore.Http.HttpResults;

namespace BccCode.Template.Api.Configuration;

/// <summary>Every redirect target that comes from a request (return URLs) goes through here before use.</summary>
public static class LocalRedirectGuard
{
    public static bool IsLocal(string? url)
    {
        // why: IsLocalUrl is the framework's own check. The control-character rejection is an extra condition: browsers strip tab and
        // newline from URLs, so "/\t/evil.com" would become "//evil.com".
        return RedirectHttpResult.IsLocalUrl(url) && !url!.Any(char.IsControl);
    }

    public static string SafeOrRoot(string? url)
    {
        return IsLocal(url) ? url! : "/";
    }
}
