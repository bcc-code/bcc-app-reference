using Microsoft.Extensions.Configuration;

/// <summary>
/// How this AppHost runs. Fixed ports and a persistent database belong to the sole default instance only: parallel worktrees
/// (<c>--isolated</c>) and E2E runs would otherwise fight over the ports and share one database.
/// </summary>
internal sealed record RunMode(bool IsE2E, bool IsSoleInstance)
{
    public static RunMode From(IConfiguration configuration)
    {
        var isE2E = configuration["E2E"] == "true";
        var isIsolated = bool.TryParse(configuration["DcpPublisher:RandomizePorts"], out var randomizePorts) && randomizePorts;
        return new RunMode(isE2E, IsSoleInstance: !isE2E && !isIsolated);
    }

    /// <summary>The fixed port for the sole instance; null (a random port) for every other run.</summary>
    public int? Port(int port)
    {
        return IsSoleInstance ? port : null;
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
