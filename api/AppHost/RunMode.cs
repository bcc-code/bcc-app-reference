/// <summary>
/// How this AppHost runs. Fixed ports and a persistent database container belong to the sole default instance only: parallel
/// worktrees (<c>--isolated</c>) and E2E runs would otherwise fight over the ports and share one database.
/// </summary>
/// <param name="IsE2E">An E2E run: the lean topology on a throwaway database, browsed on plain localhost.</param>
/// <param name="IsIsolated">An <c>--isolated</c> run that is not an E2E run: random ports and a database volume of its own.</param>
/// <param name="InstanceSlug">The checkout folder's name as a slug for an isolated run; empty for every other run.</param>
internal sealed record RunMode(bool IsE2E, bool IsIsolated, string InstanceSlug)
{
    public bool IsSoleInstance => !IsE2E && !IsIsolated;

    /// <summary>
    /// The Docker volume of an isolated run's Postgres, or null when the run keeps no data. <c>tools/worktree-cleanup/prune-volumes.mjs</c>
    /// removes it once the worktree is gone and relies on this name.
    /// </summary>
    public string? DatabaseVolumeName => IsIsolated ? $"bcc-template-{InstanceSlug}-postgres" : null;

    /// <summary>
    /// The host name the browser uses. Cookies are scoped by host and ignore ports, so instances that all browsed
    /// <c>template.dev.localhost</c> would share the API's <c>__Host-</c> session cookie and sign each other out; each isolated run
    /// gets a host of its own. Browsers resolve <c>*.localhost</c> themselves and the dev certificate covers <c>*.dev.localhost</c>
    /// (one label, so a sibling name and not a subdomain). E2E runs use plain localhost: their browser contexts are fresh.
    /// </summary>
    public string BrowserHost => IsE2E ? "localhost" : IsIsolated ? $"template-{InstanceSlug}.dev.localhost" : "template.dev.localhost";

    /// <summary>
    /// Suffix for the OIDC mock's session cookie. The mock is always reached on localhost (the API's server-side calls cannot
    /// resolve <c>*.dev.localhost</c>), so instances are kept apart by cookie name instead. Empty for the sole instance.
    /// </summary>
    public string CookieSuffix => InstanceSlug;

    public static RunMode From(IDistributedApplicationBuilder builder)
    {
        var isE2E = builder.Configuration["E2E"] == "true";
        var randomizePorts = bool.TryParse(builder.Configuration["DcpPublisher:RandomizePorts"], out var randomized) && randomized;
        var isIsolated = !isE2E && randomizePorts;
        return new RunMode(isE2E, isIsolated, isIsolated ? SlugOfCheckout(builder.AppHostDirectory) : "");
    }

    /// <summary>The fixed port for the sole instance; null (a random port) for every other run.</summary>
    public int? Port(int port)
    {
        return IsSoleInstance ? port : null;
    }

    /// <summary>
    /// The checkout folder (the AppHost is in <c>api/AppHost</c>) as a slug: lower case, anything but an ASCII letter or digit
    /// becomes a hyphen, trimmed, at most 40 characters. <c>prune-volumes.mjs</c> uses the same rules.
    /// </summary>
    private static string SlugOfCheckout(string appHostDirectory)
    {
        var checkout = new DirectoryInfo(appHostDirectory).Parent?.Parent?.Name ?? "";
        var slug = new string(checkout.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (slug.Length > 40)
            slug = slug[..40].TrimEnd('-');
        return slug.Length > 0 ? slug : "isolated";
    }
}

/// <summary>
/// The port layout of the sole instance, in slots from the base 16000 (<c>scripts/init-app.ps1 -PortBase</c> moves the whole range).
/// The dashboard (+50), OTLP (+51) and resource service (+52) are in <c>Properties/launchSettings.json</c>. The slots +30..+32
/// (platform mock, Mailpit) and +40 (storage emulator) are kept free: an app that needs a stand-in for the BCC platform, e-mail or a
/// files bucket uses them, so every app of the family has the same layout.
/// </summary>
internal static class LocalPorts
{
    public const int Api = 16000;
    public const int Vite = 16001;
    public const int Postgres = 16010;
    public const int DatabaseUi = 16011;
    public const int OidcMock = 16020;
    public const int Architecture = 16060;
}
