using BccCode.Template.Persistence;
using BccCode.Template.Security;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;

namespace BccCode.Template.Notes;

public sealed record GetNoteResponse(Guid Id, string Title, string Body, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int Version);

/// <summary>One note of the caller. No owner and no other internal field in the answer: the contract is not the stored row.</summary>
public static class GetNoteEndpoint
{
    public static Task<Note?> LoadAsync(Guid noteId, TemplateDb db, CancellationToken cancellationToken)
    {
        return db.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == noteId, cancellationToken);
    }

    public static Results<NotFound, Forbidden>? Permission(Note? note, CallerAccess access)
    {
        return NoteRules.Permission(note, access);
    }

    [WolverineGet("/api/notes/{noteId}")]
    [NonTransactional]   // a read: no transaction and no envelope row to open
    public static GetNoteResponse Get([NotBody] Note note)
    {
        return new GetNoteResponse(note.Id, note.Title, note.Body, note.CreatedAt, note.UpdatedAt, note.Version);
    }
}
