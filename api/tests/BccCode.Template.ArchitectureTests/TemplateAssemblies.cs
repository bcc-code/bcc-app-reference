using System.Reflection;

namespace BccCode.Template.ArchitectureTests;

/// <summary>
/// The two application assemblies the rules scan: the feature project <c>BccCode.Template</c> (slices, the database context,
/// the changes) and the host <c>BccCode.Template.Api</c> (composition, sign-in, HTTP plumbing). Slices, handlers and messages are in
/// the feature project; hosted services and logging classes could be in either.
/// </summary>
public static class TemplateAssemblies
{
    public static Assembly Features { get; } = typeof(TemplateAssemblyMarker).Assembly;

    public static Assembly Host { get; } = typeof(Program).Assembly;

    public static IReadOnlyList<Assembly> All { get; } = [Features, Host];
}

public class TemplateAssembliesTests
{
    [Fact]
    public void The_scan_finds_the_feature_project_and_the_host()
    {
        TemplateAssemblies.Features.GetName().Name.Should().Be("BccCode.Template");
        TemplateAssemblies.Host.GetName().Name.Should().Be("BccCode.Template.Api");
    }
}
