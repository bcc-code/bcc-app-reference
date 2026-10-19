using System.Security.Claims;
using BccCode.Template.Api.EfCore.Configuration;
using BccCode.Template.Web.Auth;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;
using Wolverine.Http;

namespace BccCode.Template.Api.EfCore.Features.Notes;

/// <summary>The caller's own, non-archived notes, newest first.</summary>
public static class ListNotesEndpoint
{
    public static async Task<IReadOnlyList<Note>> LoadAsync(ClaimsPrincipal user, TemplateDbContext db, CancellationToken ct)
    {
        var subject = user.Subject();
        return await db.Notes.AsNoTracking().Where(n => n.OwnerId == subject && !n.Archived).OrderByDescending(n => n.CreatedAt).ToListAsync(ct);
    }

    [WolverineGet("/api/notes")]
    [NonTransactional]   // a read: no transaction and no envelope row to open
    public static IReadOnlyList<NoteResponse> Get([NotBody] IReadOnlyList<Note> notes) => [.. notes.Select(NoteResponse.From)];
}
