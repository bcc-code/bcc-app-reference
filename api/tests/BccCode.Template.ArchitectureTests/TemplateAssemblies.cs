using System.Reflection;

namespace BccCode.Template.ArchitectureTests;

/// <summary>
/// The application assemblies the rules scan: the shared web project and every persistence variant
/// (<c>BccCode.Template.Api*</c>: <c>.Marten</c>, <c>.EfCore</c>, or just <c>BccCode.&lt;App&gt;.Api</c> after init-app.ps1). They are found by file name in the output folder, because the project reference in the
/// csproj is a wildcard; a new variant is covered by every rule the moment it exists.
/// </summary>
public static class TemplateAssemblies
{
    public static IReadOnlyList<Assembly> Web { get; } = Load("BccCode.Template.Web.dll");

    public static IReadOnlyList<Assembly> Apis { get; } = Load("BccCode.Template.Api*.dll");

    public static IReadOnlyList<Assembly> All { get; } = [.. Web, .. Apis];

    private static List<Assembly> Load(string pattern) =>
        [.. Directory.GetFiles(AppContext.BaseDirectory, pattern)
            .Where(file => !Path.GetFileName(file).Contains(".Tests.", StringComparison.Ordinal))
            .Select(Assembly.LoadFrom)];
}

public class TemplateAssembliesTests
{
    [Fact]
    public void The_scan_finds_the_web_project_and_at_least_one_variant()
    {
        TemplateAssemblies.Web.Should().ContainSingle();
        TemplateAssemblies.Apis.Should().NotBeEmpty();
    }
}
