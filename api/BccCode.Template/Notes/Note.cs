namespace BccCode.Template.Notes;

/// <summary>
/// A note of one person; the owner is the person id (ADR 0031). The setters are private: the state changes only through
/// <see cref="Create"/>, <see cref="Edit"/> and <see cref="Archive"/>, which keep <see cref="Version"/> moving. The version is
/// the concurrency token, and the client sends it back to prove it edited the latest. Never returned from an endpoint.
/// </summary>
public class Note
{
    protected Note()
    {
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Set when the note is archived. An archive keeps the row: a query filter hides it. Rows are never deleted by the app.</summary>
    public DateTimeOffset? ArchivedAt { get; private set; }

    /// <summary>1 on insert, one more on every edit and archive. EF's concurrency token: a stale save fails and answers 409.</summary>
    public int Version { get; private set; }

    public static Note Create(Guid id, DateTimeOffset now, Guid ownerId, NoteContent content)
    {
        return new Note
        {
            Id = id,
            OwnerId = ownerId,
            Title = content.Title.Value,
            Body = content.Body.Value,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    public void Edit(DateTimeOffset now, NoteContent content)
    {
        (Title, Body, UpdatedAt) = (content.Title.Value, content.Body.Value, now);
        Version++;
    }

    public void Archive(DateTimeOffset now)
    {
        (ArchivedAt, UpdatedAt) = (now, now);
        Version++;
    }
}
