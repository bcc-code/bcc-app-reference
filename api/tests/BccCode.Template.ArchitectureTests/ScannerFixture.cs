namespace BccCode.Template.ArchitectureTests;

/// <summary>Never called. It proves the scanner sees a forbidden call, so the real rule cannot pass vacuously.</summary>
internal static class ScannerFixture
{
    public static DateTimeOffset ReadsTheInstanceClock() => DateTimeOffset.UtcNow;

    public static object SchedulesAMessage() => Wolverine.ConfiguredMessageExtensions.DelayedFor(new object(), TimeSpan.FromMinutes(1));
}
