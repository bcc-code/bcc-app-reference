namespace BccCode.Template.Api.EfCore.Features.Notes;

/// <summary>
/// A note of one user; the owner is the OIDC subject. The setters are private: the state changes only through
/// <see cref="Edit"/> and <see cref="Archive"/>, which keep <see cref="Version"/> moving. The stored version is the
/// concurrency token (<see cref="NoteConfiguration"/>), and the client sends it back to prove it edited the latest.
/// </summary>
public sealed class Note
{
    private Note()
    {
    }

    public Guid Id { get; private set; }
    public int Version { get; private set; }
    public string OwnerId { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public bool Archived { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>A new note is at version 1, the same as a new stream in the Marten variant.</summary>
    public static Note Create(Guid id, string ownerId, NoteContent content, DateTimeOffset now) => new()
    {
        Id = id,
        Version = 1,
        OwnerId = ownerId,
        Title = content.Title.Value,
        Body = content.Body.Value,
        CreatedAt = Stored(now),
        UpdatedAt = Stored(now),
    };

    public void Edit(NoteContent content, DateTimeOffset now)
    {
        (Title, Body, UpdatedAt) = (content.Title.Value, content.Body.Value, Stored(now));
        Version++;
    }

    public void Archive(DateTimeOffset now)
    {
        (Archived, UpdatedAt) = (true, Stored(now));
        Version++;
    }

    /// <summary>
    /// Postgres keeps microseconds and .NET ticks are 100 ns. Cutting the time here makes the response of a write equal to what a
    /// later read returns, so a client can compare the two.
    /// </summary>
    private static DateTimeOffset Stored(DateTimeOffset time) => new(time.Ticks - time.Ticks % 10, time.Offset);
}
