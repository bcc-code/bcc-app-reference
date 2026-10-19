using System.Security.Claims;
using BccCode.Template.Api.EfCore.Configuration;
using BccCode.Template.Web.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;
using Wolverine.Http;

namespace BccCode.Template.Api.EfCore.Features.Notes;

public static class GetNoteEndpoint
{
    public static Task<Note?> LoadAsync(Guid noteId, TemplateDbContext db, CancellationToken ct) =>
        db.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == noteId, ct);

    public static ProblemDetails Validate(Note? note, ClaimsPrincipal user) =>
        NoteRules.Visibility(note, user.Subject()) ?? WolverineContinue.NoProblems;

    [WolverineGet("/api/notes/{noteId}")]
    [NonTransactional]   // a read: no transaction and no envelope row to open
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public static NoteResponse Get([NotBody] Note note) => NoteResponse.From(note);
}
