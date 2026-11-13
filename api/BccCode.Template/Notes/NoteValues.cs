namespace BccCode.Template.Notes;

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

/// <summary>What a create or an edit writes, parsed from the request.</summary>
public readonly record struct NoteContent(NoteTitle Title, NoteBody Body)
{
    /// <summary>
    /// The 400 for the first field that fails (the title before the body), or null with the content. <c>Validate</c> calls this so
    /// the endpoint only runs on valid input.
    /// </summary>
    public static DomainError? TryParse(string? title, string? body, out NoteContent content)
    {
        content = default;
        if (NoteTitle.TryParse(title, out var parsedTitle) is { } titleKey) return new DomainError(titleKey, "Invalid title");
        if (NoteBody.TryParse(body, out var parsedBody) is { } bodyKey) return new DomainError(bodyKey, "Invalid body");
        content = new NoteContent(parsedTitle, parsedBody);
        return null;
    }

    /// <summary>The content of a request that <c>Validate</c> has accepted. Throws on invalid input, which is a bug in the endpoint.</summary>
    public static NoteContent Parse(string? title, string? body)
    {
        return TryParse(title, body, out var content) is null
            ? content
            : throw new InvalidOperationException("The request was not validated before it was parsed.");
    }
}
