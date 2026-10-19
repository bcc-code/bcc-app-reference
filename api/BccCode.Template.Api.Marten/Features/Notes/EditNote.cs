using System.Security.Claims;
using BccCode.Template.Web.Auth;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;
using Wolverine.Marten;

namespace BccCode.Template.Api.Marten.Features.Notes;

/// <param name="Version">The version the client last read. A different stored version means someone else edited since.</param>
public sealed record EditNote(string Title, string Body, int Version);

/// <summary>
/// Replaces title and body. Order of the checks: a note the caller cannot see is 404 whatever the body holds (so nothing
/// leaks), then a stale version is 409, then the body is validated. The append asserts the version again at commit, so two
/// edits racing past <c>Validate</c> cannot both win.
/// </summary>
public static class EditNoteEndpoint
{
    public static Task<Note?> LoadAsync(Guid noteId, IQuerySession session, CancellationToken ct) => session.LoadAsync<Note>(noteId, ct);

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
    public static (NoteResponse, IMartenOp) Post(Guid noteId, EditNote command, [NotBody] Note note, [NotBody] NoteContent content, TimeProvider time)
    {
        var now = time.GetUtcNow();
        var edited = new NoteEdited(content.Title.Value, content.Body.Value, now);
        var response = new NoteResponse(noteId, edited.Title, edited.Body, note.CreatedAt, now, note.Version + 1);
        // Marten asserts the version the stream will have after the append, so one past the version that was read.
        return (response, MartenOps.Append(noteId, note.Version + 1, edited));
    }
}
