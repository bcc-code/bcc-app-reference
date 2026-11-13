using BccCode.Template.Persistence;
using BccCode.Template.Security;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;

namespace BccCode.Template.Notes;

/// <param name="At">When the step was taken.</param>
/// <param name="Step">The business step: <c>NoteCreated</c>, <c>NoteEdited</c> or <c>NoteArchived</c>.</param>
/// <param name="Version">The version of the note the step wrote.</param>
/// <param name="Title">The note's title after the step, read from the version the step wrote.</param>
/// <param name="Comment">The reason the person gave for the step, if any.</param>
public sealed record NoteHistoryItem(DateTimeOffset At, string Step, int Version, string Title, string? Comment);

public sealed record GetNoteHistoryResponse(Guid Id, IReadOnlyList<NoteHistoryItem> Entries);

/// <summary>
/// The note's changes in order, each with what the note looked like after it: the version a change wrote is the current row or
/// a row of <c>note_history</c>, found by the change id. The same 403 and 404 rules as a read. No actor is returned: only the owner sees
/// a note, so every step is theirs.
/// </summary>
public static class GetNoteHistoryEndpoint
{
    public static Task<Note?> LoadAsync(Guid noteId, TemplateDb db, CancellationToken ct)
    {
        return db.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == noteId, ct);
    }

    public static Results<NotFound, Forbidden>? Permission(Note? note, CallerAccess access)
    {
        return NoteRules.Permission(note, access);
    }

    // A read opens no transaction: Wolverine would otherwise wrap every chain that takes the DbContext in one.
    [WolverineGet("/api/notes/{noteId}/history"), NonTransactional]
    public static async Task<GetNoteHistoryResponse> Get(Guid noteId, [NotBody] Note note, TemplateDb db, CancellationToken ct)
    {
        var replaced = await db.NoteVersions.AsNoTracking().Where(v => v.Id == noteId).ToListAsync(ct);
        var versions = replaced.Select(v => (v.ChangeId, v.Version, v.Title)).Append((note.ChangeId, note.Version, note.Title))
            .ToDictionary(v => v.ChangeId);

        var changes = await db.Changes.AsNoTracking().Where(c => c.SubjectId == noteId).OrderBy(c => c.Position)
            .Select(c => new { c.Id, c.At, c.Type, c.Reason }).ToListAsync(ct);

        return new GetNoteHistoryResponse(noteId, [.. changes.Select(c => new NoteHistoryItem(c.At, c.Type, versions[c.Id].Version, versions[c.Id].Title, c.Reason))]);
    }
}
