using Wolverine.Http;

namespace BccCode.Template.Web.Http;

/// <summary>
/// One convention for operation ids and tags, derived from the endpoint class instead of set by hand: the id is the class
/// name without "Endpoint", the tag is the feature folder (<c>...Features.Notes</c> gives "Notes"). Ids stay stable when a
/// file moves inside its feature, so the generated client keeps its method names.
/// </summary>
public static class OpenApiNaming
{
    private const string FeatureMarker = ".Features.";

    public static string OperationId(Type endpointType) =>
        endpointType.Name.EndsWith("Endpoint", StringComparison.Ordinal) ? endpointType.Name[..^"Endpoint".Length] : endpointType.Name;

    public static string Tag(Type endpointType)
    {
        var ns = endpointType.Namespace ?? string.Empty;
        var marker = ns.IndexOf(FeatureMarker, StringComparison.Ordinal);
        return marker < 0 ? "Default" : ns[(marker + FeatureMarker.Length)..].Split('.')[0];
    }

    public static void Apply(HttpChain chain)
    {
        var type = chain.Method.HandlerType;
        var operationId = OperationId(type);
        chain.WithTags(Tag(type));
        chain.WithName(operationId);
        chain.WithDescription(char.ToLowerInvariant(operationId[0]) + operationId[1..]);   // the Hey API client's method name
    }
}
