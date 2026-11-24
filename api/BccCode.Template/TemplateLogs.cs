using BccCode.Template.Notes;

namespace BccCode.Template;

/// <summary>
/// Log messages are source-generated, one class per area, so their templates are fixed and a log-based metric or alert can
/// match them. Slices and commands take the class, never an <c>ILoggerFactory</c>. Register every log class of this project here;
/// the host registers its own in <c>ApiLogs</c>.
/// </summary>
public static class TemplateLogs
{
    public static IServiceCollection AddTemplateLogs(this IServiceCollection services)
    {
        return services.AddSingleton<NotesLog>();
    }
}
