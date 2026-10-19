using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BccCode.Template.ArchitectureTests;

public class LoggingRulesTests
{
    private const BindingFlags AllMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>Messages are source-generated in a log class per area, so nothing creates loggers by hand.</summary>
    [Fact]
    public void Nothing_takes_an_ILoggerFactory() =>
        TemplateAssemblies.All.SelectMany(a => a.GetTypes()).SelectMany(t => t.GetMembers(AllMembers).OfType<MethodBase>())
            .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(ILoggerFactory)))
            .Select(m => $"{m.DeclaringType?.FullName}.{m.Name}")
            .Should().BeEmpty();

    [Fact]
    public void Every_log_class_is_registered_by_AddTemplateLogs()
    {
        foreach (var api in TemplateAssemblies.Apis)
        {
            var addLogs = api.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Single(m => m.Name == "AddTemplateLogs");
            var services = new ServiceCollection();
            addLogs.Invoke(null, [services]);
            var registered = services.Select(d => d.ServiceType).ToHashSet();

            var logClasses = api.GetTypes().Where(t => t.GetMethods(AllMembers).Any(m => m.GetCustomAttribute<LoggerMessageAttribute>() is not null));

            logClasses.Should().NotBeEmpty();
            logClasses.Where(t => !registered.Contains(t)).Select(t => t.FullName).Should().BeEmpty();
        }
    }
}
