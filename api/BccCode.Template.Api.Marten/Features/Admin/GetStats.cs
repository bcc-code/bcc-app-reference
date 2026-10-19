using BccCode.Template.Api.Marten.Features.Notes;
using Marten;
using Wolverine.Http;

namespace BccCode.Template.Api.Marten.Features.Admin;

public sealed record StatsResponse(int ActiveNotes, int ArchivedNotes);

public sealed record NoteCounts(int Active, int Archived);

/// <summary>Under <c>/api/admin</c>, so the admin policy applies by convention without an attribute.</summary>
public static class GetStatsEndpoint
{
    public static async Task<NoteCounts> LoadAsync(IQuerySession session, CancellationToken ct) =>
        new(await session.Query<Note>().CountAsync(n => !n.Archived, ct), await session.Query<Note>().CountAsync(n => n.Archived, ct));

    [WolverineGet("/api/admin/stats")]
    public static StatsResponse Get([NotBody] NoteCounts counts) => new(counts.Active, counts.Archived);
}
