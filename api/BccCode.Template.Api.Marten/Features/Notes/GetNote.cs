using System.Security.Claims;
using BccCode.Template.Web.Auth;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace BccCode.Template.Api.Marten.Features.Notes;

public static class GetNoteEndpoint
{
    public static Task<Note?> LoadAsync(Guid noteId, IQuerySession session, CancellationToken ct) => session.LoadAsync<Note>(noteId, ct);

    public static ProblemDetails Validate(Note? note, ClaimsPrincipal user) =>
        NoteRules.Visibility(note, user.Subject()) ?? WolverineContinue.NoProblems;

    [WolverineGet("/api/notes/{noteId}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public static NoteResponse Get([NotBody] Note note) => NoteResponse.From(note);
}
