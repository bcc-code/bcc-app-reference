using System.Reflection;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace BccCode.Template;

/// <summary>
/// A business rule the request breaks: 400 with a ProblemDetails body that carries the i18n <see cref="Key"/> (standard 09.11).
/// An error that carries data is a <see cref="TypedProblem"/> record instead. A Validate step returns it as is, alone or in a
/// <c>Results&lt;...&gt;</c>. Unlike <c>BadRequest&lt;ProblemDetails&gt;</c> it is written and documented as application/problem+json.
/// </summary>
public sealed class DomainError(string key, string title) : IResult, IStatusCodeHttpResult, IEndpointMetadataProvider
{
    private const string CONTENT_TYPE = "application/problem+json";

    public string Key { get; } = key;

    public string Title { get; } = title;

    public int? StatusCode
    {
        get { return StatusCodes.Status400BadRequest; }
    }

    public Task ExecuteAsync(HttpContext httpContext)
    {
        return TypedResults.Problem(Problems.Create(StatusCodes.Status400BadRequest, Key, Title)).ExecuteAsync(httpContext);
    }

    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status400BadRequest, typeof(ProblemDetails), [CONTENT_TYPE]));
    }
}
