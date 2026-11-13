using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Metadata;

namespace BccCode.Template;

/// <summary>
/// An error that carries data. The record's properties are the ProblemDetails extensions, so the OpenAPI document and the
/// client know their types. Derive from <see cref="BadRequestProblem{TSelf}"/>, <see cref="ConflictProblem{TSelf}"/>,
/// <see cref="ContentTooLargeProblem{TSelf}"/> or <see cref="TooManyRequestsProblem{TSelf}"/>; an error with only a title is a
/// <see cref="DomainError"/>.
/// </summary>
public abstract record TypedProblem(int Status, string Title) : IResult, IStatusCodeHttpResult
{
    internal const string CONTENT_TYPE = "application/problem+json";

    /// <summary>The i18n key the SPA translates, <c>errors.</c> and the record's name in camel case (standard 09.11).</summary>
    [JsonPropertyName("key")]
    public string Key
    {
        get { return "errors." + JsonNamingPolicy.CamelCase.ConvertName(GetType().Name); }
    }

    /// <summary>A stable URI from the key, the same as for every other problem (<see cref="Problems.TypeOf"/>).</summary>
    public string Type
    {
        get { return Problems.TypeOf(Key); }
    }

    int? IStatusCodeHttpResult.StatusCode
    {
        get { return Status; }
    }

    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = Status;
        AddHeaders(httpContext.Response);
        return httpContext.Response.WriteAsJsonAsync(this, GetType(), options: null, contentType: CONTENT_TYPE);
    }

    /// <summary>Headers that go with the status, such as Retry-After on a 429.</summary>
    protected virtual void AddHeaders(HttpResponse response)
    {
    }
}

public abstract record BadRequestProblem<TSelf>(string Title)
    : TypedProblem(StatusCodes.Status400BadRequest, Title), IEndpointMetadataProvider
    where TSelf : BadRequestProblem<TSelf>
{
    // IEndpointMetadataProvider is a static interface member; ASP.NET calls it on the concrete record.
    [SuppressMessage("Design", "CA1000", Justification = "Implements a static interface member.")]
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status400BadRequest, typeof(TSelf), [CONTENT_TYPE]));
    }
}

/// <summary>A limit refused the request (standard 16.15): 429 with a Retry-After header in seconds, which the body repeats.</summary>
public abstract record TooManyRequestsProblem<TSelf>(string Title, int RetryAfterSeconds)
    : TypedProblem(StatusCodes.Status429TooManyRequests, Title), IEndpointMetadataProvider
    where TSelf : TooManyRequestsProblem<TSelf>
{
    // IEndpointMetadataProvider is a static interface member; ASP.NET calls it on the concrete record.
    [SuppressMessage("Design", "CA1000", Justification = "Implements a static interface member.")]
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status429TooManyRequests, typeof(TSelf), [CONTENT_TYPE]));
    }

    protected override void AddHeaders(HttpResponse response)
    {
        response.Headers.RetryAfter = RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>The request's content is larger than the endpoint takes: 413.</summary>
public abstract record ContentTooLargeProblem<TSelf>(string Title)
    : TypedProblem(StatusCodes.Status413PayloadTooLarge, Title), IEndpointMetadataProvider
    where TSelf : ContentTooLargeProblem<TSelf>
{
    // IEndpointMetadataProvider is a static interface member; ASP.NET calls it on the concrete record.
    [SuppressMessage("Design", "CA1000", Justification = "Implements a static interface member.")]
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status413PayloadTooLarge, typeof(TSelf), [CONTENT_TYPE]));
    }
}

public abstract record ConflictProblem<TSelf>(string Title)
    : TypedProblem(StatusCodes.Status409Conflict, Title), IEndpointMetadataProvider
    where TSelf : ConflictProblem<TSelf>
{
    // IEndpointMetadataProvider is a static interface member; ASP.NET calls it on the concrete record.
    [SuppressMessage("Design", "CA1000", Justification = "Implements a static interface member.")]
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status409Conflict, typeof(TSelf), [CONTENT_TYPE]));
    }
}
