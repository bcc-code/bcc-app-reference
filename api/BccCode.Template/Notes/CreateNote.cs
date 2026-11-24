using BccCode.Template.Persistence;
using BccCode.Template.Security;
using Wolverine.Persistence;

namespace BccCode.Template.Notes;

/// <param name="Title">1 to 200 characters, trimmed.</param>
/// <param name="Body">At most 10 000 characters, may be empty.</param>
public sealed record CreateNoteRequest(string Title, string Body);

public sealed record CreateNoteResponse(Guid Id, string Title, string Body, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int Version);

/// <summary>Adds a note for the caller. Anyone signed in may; <c>Validate</c> parses the title and body, so <c>Post</c> only sees valid values.</summary>
public static class CreateNoteEndpoint
{
    public static DomainError? Validate(CreateNoteRequest request)
    {
        return NoteContent.TryParse(request.Title, request.Body, out _);
    }

    [WolverinePost("/api/notes")]
    public static (Created<CreateNoteResponse>, IStorageAction<Note>) Post(CreateNoteRequest request, CallerAccess access, TimeProvider time)
    {
        var now = PostgresTime.Now(time);
        var note = Note.Create(Guid.CreateVersion7(now), now, access.PersonUid, NoteContent.Parse(request.Title, request.Body));
        var response = new CreateNoteResponse(note.Id, note.Title, note.Body, note.CreatedAt, note.UpdatedAt, note.Version);
        return (TypedResults.Created($"/api/notes/{note.Id}", response), Storage.Insert(note));
    }
}
