namespace BccCode.Template.Api.Marten.Features.Notes;

/// <summary>A note was written. The stream id is the note id; the owner is the OIDC subject of the signed-in user.</summary>
public sealed record NoteCreated(string OwnerId, string Title, string Body, DateTimeOffset CreatedAt);

public sealed record NoteEdited(string Title, string Body, DateTimeOffset EditedAt);

/// <summary>An archived note is gone for its owner: it is not listed and answers 404. Only the admin count still sees it.</summary>
public sealed record NoteArchived(DateTimeOffset ArchivedAt);

/// <summary>
/// The note stream aggregate, kept as an inline snapshot (<see cref="TemplateStore"/>), so reads and writes share one
/// document. State and Apply methods only; the decisions are in the slices and in <see cref="NoteRules"/>.
/// <see cref="Version"/> is the stream version Marten maintains: the client sends it back to prove it edited the latest.
/// </summary>
public sealed class Note
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static Note Create(NoteCreated e) => new()
    {
        OwnerId = e.OwnerId,
        Title = e.Title,
        Body = e.Body,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.CreatedAt,
    };

    public void Apply(NoteEdited e) => (Title, Body, UpdatedAt) = (e.Title, e.Body, e.EditedAt);

    public void Apply(NoteArchived e) => (Archived, UpdatedAt) = (true, e.ArchivedAt);
}
