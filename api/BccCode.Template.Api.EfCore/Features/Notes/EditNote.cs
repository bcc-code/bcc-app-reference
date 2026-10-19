using System.Security.Claims;
using BccCode.Template.Api.EfCore.Configuration;
using BccCode.Template.Web.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine.Http;
using Wolverine.Persistence;

namespace BccCode.Template.Api.EfCore.Features.Notes;

/// <param name="Version">The version the client last read. A different stored version means someone else edited since.</param>
public sealed record EditNote(string Title, string Body, int Version);

/// <summary>
/// Replaces title and body. Order of the checks: a note the caller cannot see is 404 whatever the body holds (so nothing
/// leaks), then a stale version is 409, then the body is validated. The update asserts the version again at commit, so two
/// edits racing past <c>Validate</c> cannot both win (the loser gets a <see cref="DbUpdateConcurrencyException"/>, answered as 409).
/// </summary>
public static class EditNoteEndpoint
{
    // Tracked, so EF remembers the version that was read and puts it in the WHERE clause of the update.
    public static Task<Note?> LoadAsync(Guid noteId, TemplateDbContext db, CancellationToken ct) => db.Notes.FirstOrDefaultAsync(n => n.Id == noteId, ct);

    public static (NoteContent, ProblemDetails) Validate(EditNote command, Note? note, ClaimsPrincipal user)
    {
        if (NoteRules.Visibility(note, user.Subject()) is { } notFound) return (default, notFound);
        if (NoteRules.Freshness(note!, command.Version) is { } stale) return (default, stale);
        return NoteContent.Parse(command.Title, command.Body);
    }

    [WolverinePost("/api/notes/{noteId}/edit")]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public static (NoteResponse, IStorageAction<Note>) Post(Guid noteId, EditNote command, [NotBody] Note note, [NotBody] NoteContent content, TimeProvider time)
    {
        note.Edit(content, time.GetUtcNow());
        return (NoteResponse.From(note), Storage.Update(note));
    }
}
