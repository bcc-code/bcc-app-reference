using System.Security.Claims;
using BccCode.Template.Web.Auth;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;
using Wolverine.Marten;

namespace BccCode.Template.Api.Marten.Features.Notes;

public sealed record ArchiveNote(int Version);

/// <summary>Archives a note. The same 404 and 409 rules as an edit; an archived note is invisible, so archiving twice is a 404.</summary>
public static class ArchiveNoteEndpoint
{
    public static Task<Note?> LoadAsync(Guid noteId, IQuerySession session, CancellationToken ct) => session.LoadAsync<Note>(noteId, ct);

    public static ProblemDetails Validate(ArchiveNote command, Note? note, ClaimsPrincipal user) =>
        NoteRules.Visibility(note, user.Subject())
        ?? NoteRules.Freshness(note!, command.Version)
        ?? WolverineContinue.NoProblems;

    [WolverinePost("/api/notes/{noteId}/archive"), EmptyResponse]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public static (IMartenOp, NoteArchivedMessage) Post(Guid noteId, ArchiveNote command, [NotBody] Note note, TimeProvider time) =>
        // The event and the message are stored in one transaction; the message is sent after the commit.
        (MartenOps.Append(noteId, note.Version + 1, new NoteArchived(time.GetUtcNow())), new NoteArchivedMessage(noteId));
}
