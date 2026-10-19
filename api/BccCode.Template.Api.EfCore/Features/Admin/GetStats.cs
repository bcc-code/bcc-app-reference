using BccCode.Template.Api.EfCore.Configuration;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;
using Wolverine.Http;

namespace BccCode.Template.Api.EfCore.Features.Admin;

public sealed record StatsResponse(int ActiveNotes, int ArchivedNotes);

public sealed record NoteCounts(int Active, int Archived);

/// <summary>Under <c>/api/admin</c>, so the admin policy applies by convention without an attribute.</summary>
public static class GetStatsEndpoint
{
    public static async Task<NoteCounts> LoadAsync(TemplateDbContext db, CancellationToken ct) =>
        new(await db.Notes.CountAsync(n => !n.Archived, ct), await db.Notes.CountAsync(n => n.Archived, ct));

    [WolverineGet("/api/admin/stats")]
    [NonTransactional]   // a read: no transaction and no envelope row to open
    public static StatsResponse Get([NotBody] NoteCounts counts) => new(counts.Active, counts.Archived);
}
