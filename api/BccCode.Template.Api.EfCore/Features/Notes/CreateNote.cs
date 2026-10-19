using System.Security.Claims;
using BccCode.Template.Web.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;
using Wolverine.Persistence;

namespace BccCode.Template.Api.EfCore.Features.Notes;

public sealed record CreateNote(string Title, string Body);

/// <summary>Adds a note. The title and body are parsed in <c>Validate</c>, so <c>Post</c> only sees valid values.</summary>
public static class CreateNoteEndpoint
{
    public static (NoteContent, ProblemDetails) Validate(CreateNote command) => NoteContent.Parse(command.Title, command.Body);

    [WolverinePost("/api/notes")]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public static (Created<NoteResponse>, IStorageAction<Note>) Post(CreateNote command, [NotBody] NoteContent content, ClaimsPrincipal user, TimeProvider time)
    {
        var now = time.GetUtcNow();
        var note = Note.Create(Guid.CreateVersion7(now), user.Subject(), content, now);
        return (TypedResults.Created($"/api/notes/{note.Id}", NoteResponse.From(note)), Storage.Insert(note));
    }
}
