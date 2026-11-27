using BccCode.Template.Persistence;
using BccCode.Template.Security;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;

namespace BccCode.Template.Notes;

public sealed record NoteItem(Guid Id, string Title, string Body, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int Version);

public sealed record ListNotesResponse(IReadOnlyList<NoteItem> Notes);

/// <summary>The caller's own, non-archived notes (the query filter hides archived ones), newest first.</summary>
public static class ListNotesEndpoint
{
    public static async Task<IReadOnlyList<Note>> LoadAsync(CallerAccess access, TemplateDb db, CancellationToken cancellationToken)
    {
        return await db.Notes.AsNoTracking().Where(n => n.OwnerId == access.PersonId).OrderByDescending(n => n.CreatedAt).ToListAsync(cancellationToken);
    }

    [WolverineGet("/api/notes")]
    [NonTransactional]   // a read: no transaction and no envelope row to open
    public static ListNotesResponse Get([NotBody] IReadOnlyList<Note> notes)
    {
        return new ListNotesResponse([.. notes.Select(n => new NoteItem(n.Id, n.Title, n.Body, n.CreatedAt, n.UpdatedAt, n.Version))]);
    }
}
