using System.Security.Claims;
using BccCode.Template.Web.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;
using Wolverine.Marten;

namespace BccCode.Template.Api.Marten.Features.Notes;

public sealed record CreateNote(string Title, string Body);

/// <summary>Starts a note stream. The title and body are parsed in <c>Validate</c>, so <c>Post</c> only sees valid values.</summary>
public static class CreateNoteEndpoint
{
    public static (NoteContent, ProblemDetails) Validate(CreateNote command) => NoteContent.Parse(command.Title, command.Body);

    [WolverinePost("/api/notes")]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public static (Created<NoteResponse>, IStartStream) Post(CreateNote command, [NotBody] NoteContent content, ClaimsPrincipal user, TimeProvider time)
    {
        var now = time.GetUtcNow();
        var id = Guid.CreateVersion7(now);
        var created = new NoteCreated(user.Subject(), content.Title.Value, content.Body.Value, now);
        // A new stream has one event, so its version is 1.
        var response = new NoteResponse(id, created.Title, created.Body, now, now, Version: 1);
        return (TypedResults.Created($"/api/notes/{id}", response), MartenOps.StartStream<Note>(id, created));
    }
}
