using System.Diagnostics.CodeAnalysis;
using BccCode.Template.Persistence;
using BccCode.Template.Security;
using Microsoft.EntityFrameworkCore;

namespace BccCode.Template.Notes;

/// <param name="Version">The version the client last read; a different stored version answers 409.</param>
public sealed record ArchiveNoteRequest(int Version);

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
    [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Wolverine binds the request body for Before only when Post takes the request too.")]
    public static NoteArchivedMessage Post(ArchiveNoteRequest request, [NotBody] Note note, TimeProvider time)
    {
        note.Archive(time.GetUtcNow());
        // The update and the message are stored in one transaction; the message is sent after the commit.
        return new NoteArchivedMessage(note.Id);
    }
}
