using System.Reflection;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace BccCode.Template;

/// <summary>
/// The client edited an older version of a record than the stored one: 409 with a ProblemDetails body that carries the i18n
/// key. A <c>Before</c> or <c>Validate</c> step returns it when the version in the request is not the record's; a change that
/// slips in between that check and the save fails the save and answers the same (<c>ConflictPolicy</c>).
/// </summary>
public sealed class StaleVersion(int currentVersion, string key) : IResult, IStatusCodeHttpResult, IEndpointMetadataProvider
{
    public const string Title = "The record was changed by someone else. Reload it and try again.";
    private const string CONTENT_TYPE = "application/problem+json";

    public int CurrentVersion { get; } = currentVersion;

    public string Key { get; } = key;

    public int? StatusCode
    {
        get { return StatusCodes.Status409Conflict; }
    }

    /// <param name="currentVersion">The stored version, which the client did not send.</param>
    /// <param name="key">The i18n key the SPA translates (<c>errors.note.staleVersion</c>).</param>
    public static StaleVersion Problem(int currentVersion, string key = Problems.Keys.Conflict)
    {
        return new StaleVersion(currentVersion, key);
    }

    public Task ExecuteAsync(HttpContext httpContext)
    {
        return TypedResults.Problem(Problems.Create(StatusCodes.Status409Conflict, Key, Title)).ExecuteAsync(httpContext);
    }

    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status409Conflict, typeof(ProblemDetails), [CONTENT_TYPE]));
    }
}
