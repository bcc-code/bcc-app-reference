using System.Globalization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace BccCode.Template.Api.Configuration;

public static class OpenApiConfiguration
{
    public static void AddTemplateOpenApi(this WebApplicationBuilder builder, string title)
    {
        builder.Services.AddOpenApi(o =>
        {
            o.AddSchemaTransformer((schema, _, _) =>
            {
                // Doc comments come out with the OS's line endings; LF keeps the document the same on Windows and in CI.
                if (schema.Description is not null) schema.Description = schema.Description.ReplaceLineEndings("\n");
                return Task.CompletedTask;
            });
            o.AddOperationTransformer(DescribeEveryErrorType);
            o.AddOperationTransformer(UploadsAreMultipart);
            o.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = title;
                document.Info.Version = "v1";
                document.Servers?.Clear();
                SortForDeterministicOutput(document);
                return Task.CompletedTask;
            });
        });
    }

    // The exporter keeps one body type per status code. An endpoint can declare more for one status (a DomainError and a typed
    // problem such as a ConflictProblem record), so the body becomes oneOf all of them.
    private static async Task DescribeEveryErrorType(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var shared = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<IProducesResponseTypeMetadata>()
            .Where(m => m.Type is not null && m.Type != typeof(void))
            .GroupBy(m => m.StatusCode)
            .Select(g => (Status: g.Key, Types: g.Select(m => m.Type!).Distinct().OrderBy(t => t.Name, StringComparer.Ordinal).ToList()))
            .Where(g => g.Types.Count > 1)
            .ToList();
        foreach (var (status, types) in shared)
        {
            if (operation.Responses is null || !operation.Responses.TryGetValue(status.ToString(CultureInfo.InvariantCulture), out var response)) continue;
            if (response.Content is null) continue;

            // As components, so the generated client names each type.
            var document = context.Document!;
            var oneOf = new List<IOpenApiSchema>();
            foreach (var type in types)
            {
                if (document.Components?.Schemas?.ContainsKey(type.Name) != true)
                    document.AddComponent(type.Name, await context.GetOrCreateSchemaAsync(type, null, cancellationToken));

                oneOf.Add(new OpenApiSchemaReference(type.Name, document));
            }

            // The media type interface is read-only in Microsoft.OpenApi 3, so each is replaced.
            foreach (var contentType in response.Content.Keys.ToList())
                response.Content[contentType] = new OpenApiMediaType { Schema = new OpenApiSchema { OneOf = oneOf } };
        }
    }

    // Wolverine offers a form body as urlencoded too, which cannot carry a file, and the client would pick it. An upload
    // (a form with an IFormFile) is multipart only.
    private static Task UploadsAreMultipart(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var content = operation.RequestBody?.Content;
        if (content is null || !content.ContainsKey("multipart/form-data")) return Task.CompletedTask;

        var hasFile = context.Description.ParameterDescriptions.Any(p => p.Type == typeof(IFormFile));
        if (hasFile) content.Remove("application/x-www-form-urlencoded");
        return Task.CompletedTask;
    }

    /// <summary>The live document, in Development only. The committed copy is api/BccCode.Template.Api/BccCode.Template.Api.json, written by `pnpm --dir frontend generate`.</summary>
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
