using BccCode.Template.Persistence;
using BccCode.Template.Security;
using Microsoft.EntityFrameworkCore;

namespace BccCode.Template.Notes;

/// <param name="Title">The new title: 1 to 200 characters, trimmed.</param>
/// <param name="Body">The new body: at most 10 000 characters, may be empty.</param>
/// <param name="Version">The version the client last read. A different stored version means someone else edited since, and the answer is 409.</param>
public sealed record EditNoteRequest(string Title, string Body, int Version);

public sealed record EditNoteResponse(Guid Id, string Title, string Body, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int Version);

/// <summary>
/// Replaces title and body. Order of the checks: a note that does not exist is 404 and someone else's is 403 whatever the body
/// holds, then a stale version is 409, then the body is validated. The update asserts the version again at commit, so two edits
/// racing past <c>Before</c> cannot both win (the loser gets a <see cref="DbUpdateConcurrencyException"/>, answered as 409 by
/// <c>ConflictPolicy</c>).
/// </summary>
public static class EditNoteEndpoint
{
    // Tracked, so EF remembers the version that was read and puts it in the WHERE clause of the update.
    public static Task<Note?> LoadAsync(Guid noteId, TemplateDb db, CancellationToken cancellationToken)
    {
        return db.Notes.FirstOrDefaultAsync(n => n.Id == noteId, cancellationToken);
    }

    public static Results<NotFound, Forbidden>? Permission(Note? note, CallerAccess access)
    {
        return NoteRules.Permission(note, access);
    }

    public static StaleVersion? Before(EditNoteRequest request, Note note)
    {
        return NoteRules.Freshness(note, request.Version);
    }

    public static DomainError? Validate(EditNoteRequest request)
    {
        return NoteContent.TryParse(request.Title, request.Body, out _);
    }

    [WolverinePost("/api/notes/{noteId}/edit")]
    public static EditNoteResponse Post(EditNoteRequest request, [NotBody] Note note, TimeProvider time)
    {
        // The note is tracked: the transaction's save writes the edit, with the version that was read in the WHERE clause.
        note.Edit(PostgresTime.Now(time), NoteContent.Parse(request.Title, request.Body));
        return new EditNoteResponse(note.Id, note.Title, note.Body, note.CreatedAt, note.UpdatedAt, note.Version);
    }
}
