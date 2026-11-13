using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BccCode.Template.ArchitectureTests;

public class LoggingRulesTests
{
    private const BindingFlags ALL_MEMBERS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>Messages are source-generated in a log class per area, so nothing creates loggers by hand.</summary>
    [Fact]
    public void Nothing_takes_an_ILoggerFactory()
    {
        TemplateAssemblies.All.SelectMany(a => a.GetTypes()).SelectMany(t => t.GetMembers(ALL_MEMBERS).OfType<MethodBase>())
            .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(ILoggerFactory)))
            .Select(m => $"{m.DeclaringType?.FullName}.{m.Name}")
            .Should().BeEmpty();
    }

    /// <summary>
    /// Log classes are named <c>&lt;Area&gt;Log</c>; a class that is built by hand and takes the logger as a parameter
    /// (<c>IntegrationPushMapper</c>, <c>TemplateMigrations</c>) is not one. The feature project registers its log classes in
    /// <c>AddTemplateLogs</c>. The host has no log class of its own; the seeder's is in <c>BccCode.Template.TestData</c>.
    /// </summary>
    [Theory]
    [InlineData("Features", "AddTemplateLogs")]
    public void Every_log_class_is_registered_by_its_projects_logs_method(string project, string method)
    {
        var assembly = project == "Features" ? TemplateAssemblies.Features : TemplateAssemblies.Host;
        var addLogs = assembly.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static)).Single(m => m.Name == method);
        var services = new ServiceCollection();
        addLogs.Invoke(null, [services]);
        var registered = services.Select(d => d.ServiceType).ToHashSet();

        var logClasses = assembly.GetTypes().Where(t => t.Name.EndsWith("Log", StringComparison.Ordinal)).Where(t => t.GetMethods(ALL_MEMBERS).Any(m => m.GetCustomAttribute<LoggerMessageAttribute>() is not null)).ToList();

        logClasses.Should().NotBeEmpty();
        logClasses.Where(t => !registered.Contains(t)).Select(t => t.FullName).Should().BeEmpty();
    }
}
