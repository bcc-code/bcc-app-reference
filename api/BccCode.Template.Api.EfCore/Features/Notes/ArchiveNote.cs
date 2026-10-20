using System.Security.Claims;
using BccCode.Template.Api.EfCore.Configuration;
using BccCode.Template.Web.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine.Http;
using Wolverine.Persistence;

namespace BccCode.Template.Api.EfCore.Features.Notes;

public sealed record ArchiveNote(int Version);

/// <summary>Archives a note. The same 404 and 409 rules as an edit; an archived note is invisible, so archiving twice is a 404.</summary>
public static class ArchiveNoteEndpoint
{
    public static Task<Note?> LoadAsync(Guid noteId, TemplateDbContext db, CancellationToken ct) => db.Notes.FirstOrDefaultAsync(n => n.Id == noteId, ct);

    public static ProblemDetails Validate(ArchiveNote command, Note? note, ClaimsPrincipal user) =>
        NoteRules.Visibility(note, user.Subject())
        ?? NoteRules.Freshness(note!, command.Version)
        ?? WolverineContinue.NoProblems;

    [WolverinePost("/api/notes/{noteId}/archive"), EmptyResponse]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public static (IStorageAction<Note>, NoteArchivedMessage) Post(Guid noteId, ArchiveNote command, [NotBody] Note note, TimeProvider time)
    {
        note.Archive(time.GetUtcNow());
        // The update and the message are stored in one transaction; the message is sent after the commit.
        return (Storage.Update(note), new NoteArchivedMessage(noteId));
    }
}
