using Microsoft.OpenApi;

namespace BccCode.Template.Web.Http;

public static class OpenApiConfiguration
{
    public static void AddTemplateOpenApi(this WebApplicationBuilder builder, string title) => builder.Services.AddOpenApi(o =>
        o.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = title;
            document.Info.Version = "v1";
            document.Servers?.Clear();
            SortForDeterministicOutput(document);
            return Task.CompletedTask;
        }));

    /// <summary>The live document, in Development only. The committed copy is api/openapi/openapi.json.</summary>
    public static void MapTemplateOpenApi(this WebApplication app)
    {
        if (app.Environment.IsDevelopment()) app.MapOpenApi().AllowAnonymous();
    }

    /// <summary>Route of the development document, for <see cref="AnonymousRouteGuard"/>.</summary>
    public const string DevelopmentDocumentRoute = "/openapi/{documentName}.json";

    // Byte-stable output across machines, so the committed document only changes when the contract does. Discovery order
    // differs between machines; sorting paths, responses, schemas and tags removes that noise.
    private static void SortForDeterministicOutput(OpenApiDocument document)
    {
        if (document.Components?.Schemas is not null) ReorderByKey(document.Components.Schemas);
        if (document.Tags is { Count: > 0 })
        {
            var tags = document.Tags.OrderBy(tag => tag.Name, StringComparer.Ordinal).ToList();
            document.Tags.Clear();
            foreach (var tag in tags) document.Tags.Add(tag);
        }

        if (document.Paths is null) return;
        ReorderByKey(document.Paths);
        foreach (var operation in document.Paths.Values.Where(p => p.Operations is not null).SelectMany(p => p.Operations!.Values))
            if (operation.Responses is not null) ReorderByKey(operation.Responses);
    }

    private static void ReorderByKey<T>(IDictionary<string, T> dictionary)
    {
        var sorted = dictionary.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToList();
        dictionary.Clear();
        foreach (var entry in sorted) dictionary.Add(entry.Key, entry.Value);
    }
}
