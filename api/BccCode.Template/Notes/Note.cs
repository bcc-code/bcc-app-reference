using BccCode.Template.Changes;

namespace BccCode.Template.Notes;

/// <summary>
/// A note of one person; the owner is the BCC person uid. The setters are private: the state changes only through
/// <see cref="Create"/>, <see cref="Edit"/> and <see cref="Archive"/>, which name the business step on the <see cref="Change"/>
/// the request made (<see cref="Audited.Record"/>) and so keep <see cref="Audited.Version"/> moving. The version is the
/// concurrency token, and the client sends it back to prove it edited the latest. Never returned from an endpoint.
/// </summary>
public class Note : Audited
{
    public const string Created = "NoteCreated";
    public const string Edited = "NoteEdited";
    public const string Archived = "NoteArchived";

    protected Note()
    {
    }

    public Guid OwnerId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Set when the note is archived. An archive is a removal that keeps the row: a query filter hides it, and the change that
    /// archived it says who and when. Rows are never deleted by the app (ADR 0028).
    /// </summary>
    public DateTimeOffset? RemovedAt { get; private set; }

    protected override Guid SubjectId => Id;

    public static Note Create(Guid id, Change change, Guid ownerId, NoteContent content)
    {
        var note = new Note
        {
            Id = id,
            OwnerId = ownerId,
            Title = content.Title.Value,
            Body = content.Body.Value,
            CreatedAt = change.At,
            UpdatedAt = change.At,
        };
        note.Record(change, Created);
        return note;
    }

    public void Edit(Change change, NoteContent content)
    {
        (Title, Body, UpdatedAt) = (content.Title.Value, content.Body.Value, change.At);
        Record(change, Edited);
    }

    public void Archive(Change change, string? comment = null)
    {
        (RemovedAt, UpdatedAt) = (change.At, change.At);
        Record(change, Archived, comment);
    }
}

/// <summary>An earlier version of a <see cref="Note"/>, from <c>note_history</c>.</summary>
public sealed class NoteVersion : Note, IReplacedVersion
{
    private NoteVersion()
    {
    }

    public DateTimeOffset ReplacedAt { get; private set; }

    public Guid? ReplacedBy { get; private set; }
}
