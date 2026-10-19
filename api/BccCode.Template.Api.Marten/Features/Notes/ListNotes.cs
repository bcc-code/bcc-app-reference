using System.Security.Claims;
using BccCode.Template.Web.Auth;
using Marten;
using Wolverine.Http;

namespace BccCode.Template.Api.Marten.Features.Notes;

/// <summary>The caller's own, non-archived notes, newest first.</summary>
public static class ListNotesEndpoint
{
    public static Task<IReadOnlyList<Note>> LoadAsync(ClaimsPrincipal user, IQuerySession session, CancellationToken ct)
    {
        var subject = user.Subject();
        return session.Query<Note>().Where(n => n.OwnerId == subject && !n.Archived).OrderByDescending(n => n.CreatedAt).ToListAsync(ct);
    }

    [WolverineGet("/api/notes")]
    public static IReadOnlyList<NoteResponse> Get([NotBody] IReadOnlyList<Note> notes) => [.. notes.Select(NoteResponse.From)];
}
