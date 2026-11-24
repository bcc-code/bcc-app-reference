using System.Reflection;
using Microsoft.AspNetCore.Http.Metadata;

namespace BccCode.Template;

/// <summary>
/// 403 without a body, for a caller who may not use a resource that exists (standard 15.8). Not
/// <c>TypedResults.Forbid</c>, which challenges the cookie scheme.
/// </summary>
public sealed class Forbidden : IResult, IStatusCodeHttpResult, IEndpointMetadataProvider
{
    public static Forbidden Result { get; } = new();

    public int? StatusCode
    {
        get { return StatusCodes.Status403Forbidden; }
    }

    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status403Forbidden, typeof(void)));
    }
}
