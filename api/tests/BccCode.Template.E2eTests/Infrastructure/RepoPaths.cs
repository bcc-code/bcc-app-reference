namespace BccCode.Template.E2eTests.Infrastructure;

public static class RepoPaths
{
    public static string Root { get; } = FindRoot();
    public static string FrontendDist => Path.Combine(Root, "frontend", "dist");

    /// <summary>Traces and screenshots of failed tests. CI uploads <c>TestResults/playwright/</c> at the repo root.</summary>
    public static string Results => Path.Combine(Root, "TestResults", "playwright");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "BccCode.Template.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Could not find BccCode.Template.slnx above the test output directory.");
    }
}
