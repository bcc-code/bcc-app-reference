namespace BccCode.Template.Api.EfCore.Infrastructure;

/// <summary>
/// Log messages are source-generated, one class per area, so their templates are fixed and a log-based metric or alert can
/// match them. Slices and commands take the class, never an <c>ILoggerFactory</c>. Register every log class in <see cref="TemplateLogs"/>.
/// </summary>
public sealed partial class SeedLog(ILogger<SeedLog> logger)
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} notes")]
    public partial void Seeded(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Refusing to seed in environment {Environment}: it is not on the list of disposable environments")]
    public partial void Refused(string environment);
}

public static class TemplateLogs
{
    public static IServiceCollection AddTemplateLogs(this IServiceCollection services) => services.AddSingleton<SeedLog>();
}
