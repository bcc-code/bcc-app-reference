using Wolverine.Http;

namespace BccCode.Template.Api.Configuration;

/// <summary>
/// One convention for operation ids and tags, derived from the endpoint class instead of set by hand: the id is the class
/// name without "Endpoint", the tag is the feature folder (<c>BccCode.Template.Notes</c> gives "Notes"). Ids stay stable when a
/// file moves inside its feature, so the generated client keeps its method names.
/// </summary>
public static class OpenApiNaming
{
    private const string FEATURE_ROOT = "BccCode.Template.";

    public static string OperationId(Type endpointType)
    {
        return endpointType.Name.EndsWith("Endpoint", StringComparison.Ordinal) ? endpointType.Name[..^"Endpoint".Length] : endpointType.Name;
    }

    public static string Tag(Type endpointType)
    {
        var ns = endpointType.Namespace ?? string.Empty;
        return ns.StartsWith(FEATURE_ROOT, StringComparison.Ordinal) ? ns[FEATURE_ROOT.Length..].Split('.')[0] : "Default";
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
