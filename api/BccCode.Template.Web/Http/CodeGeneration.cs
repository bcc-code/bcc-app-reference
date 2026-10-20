using System.Reflection;

namespace BccCode.Template.Web.Http;

public static class CodeGeneration
{
    /// <summary>The JasperFx commands that run the app's own host without serving requests: the schema steps, the seed step and code generation.</summary>
    private static readonly string[] ToolCommands = ["codegen", "resources", "migrate", "seed"];

    /// <summary>True while the build runs the app to write the OpenAPI document (Microsoft.Extensions.ApiDescription.Server).</summary>
    public static bool IsGeneratingOpenApi() => Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

    /// <summary>
    /// True when this process is a command-line tool (<c>codegen write</c>, <c>resources setup</c>, <c>migrate</c>, <c>seed</c>)
    /// and not the web host. A tool sends no messages and gets no calls, so it needs no Pub/Sub or <c>Internal</c> settings
    /// (the Cloud Run migrate job has none), and its internal policies deny everything.
    /// </summary>
    public static bool IsCommandLineTool() =>
        Environment.GetCommandLineArgs().Skip(1).FirstOrDefault() is { } command && ToolCommands.Contains(command, StringComparer.OrdinalIgnoreCase);
}
