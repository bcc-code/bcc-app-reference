using BccCode.Template.Changes;
using BccCode.Template.Persistence;
using BccCode.Template.Security;
using Microsoft.EntityFrameworkCore;

namespace BccCode.Template.Notes;

/// <param name="Version">The version the client last read; a different stored version answers 409.</param>
/// <param name="Comment">Why the note is archived, if the person says; stored as the reason of the change.</param>
public sealed record ArchiveNoteRequest(int Version, string? Comment = null);

/// <summary>Archives a note. The same 403, 404 and 409 rules as an edit; an archived note does not exist for anyone, so archiving twice is a 404.</summary>
public static class ArchiveNoteEndpoint
{
    public static Task<Note?> LoadAsync(Guid noteId, TemplateDb db, CancellationToken cancellationToken)
    {
        return db.Notes.FirstOrDefaultAsync(n => n.Id == noteId, cancellationToken);
    }

    public static Results<NotFound, Forbidden>? Permission(Note? note, CallerAccess access)
    {
        return NoteRules.Permission(note, access);
    }

    public static StaleVersion? Before(ArchiveNoteRequest request, Note note)
    {
        return NoteRules.Freshness(note, request.Version);
    }

    [WolverinePost("/api/notes/{noteId}/archive"), EmptyResponse]
    public static NoteArchivedMessage Post(ArchiveNoteRequest request, [NotBody] Note note, Change change)
    {
        note.Archive(change, request.Comment);
        // The update and the message are stored in one transaction; the message is sent after the commit.
        return new NoteArchivedMessage(note.Id);
    }
}
