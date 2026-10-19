using BccCode.Template.Web.Http;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace BccCode.Template.Api.Marten.Features.Notes;

/// <summary>A title of 1 to 200 characters, trimmed. Code past the boundary cannot receive an invalid one.</summary>
public readonly record struct NoteTitle
{
    public const int MaxLength = 200;

    private NoteTitle(string value) => Value = value;

    public string Value { get; }

    /// <returns>The i18n key of the problem, or null when the input is valid.</returns>
    public static string? TryParse(string? input, out NoteTitle title)
    {
        title = default;
        var trimmed = input?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return "errors.note.titleRequired";
        if (trimmed.Length > MaxLength) return "errors.note.titleTooLong";
        title = new NoteTitle(trimmed);
        return null;
    }
}

/// <summary>A body of at most 10 000 characters. An empty body is fine.</summary>
public readonly record struct NoteBody
{
    public const int MaxLength = 10_000;

    private NoteBody(string value) => Value = value;

    public string Value { get; }

    /// <returns>The i18n key of the problem, or null when the input is valid.</returns>
    public static string? TryParse(string? input, out NoteBody body)
    {
        body = default;
        var text = input ?? string.Empty;
        if (text.Length > MaxLength) return "errors.note.bodyTooLong";
        body = new NoteBody(text);
        return null;
    }
}

/// <summary>What a create or an edit writes, parsed from the request body.</summary>
public readonly record struct NoteContent(NoteTitle Title, NoteBody Body)
{
    /// <summary>The content and <see cref="WolverineContinue.NoProblems"/>, or a 400 with the i18n key of every field that failed.</summary>
    public static (NoteContent Content, ProblemDetails Problem) Parse(string? title, string? body)
    {
        var errors = new Dictionary<string, string[]>();
        if (NoteTitle.TryParse(title, out var parsedTitle) is { } titleError) errors["title"] = [titleError];
        if (NoteBody.TryParse(body, out var parsedBody) is { } bodyError) errors["body"] = [bodyError];
        return errors.Count > 0
            ? (default, Problems.Validation(errors))
            : (new NoteContent(parsedTitle, parsedBody), WolverineContinue.NoProblems);
    }
}
