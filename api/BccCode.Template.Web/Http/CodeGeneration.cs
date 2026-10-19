using System.Reflection;

namespace BccCode.Template.Web.Http;

public static class CodeGeneration
{
    /// <summary>True while the build runs the app to write the OpenAPI document (Microsoft.Extensions.ApiDescription.Server).</summary>
    public static bool IsGeneratingOpenApi() => Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
}
