using BccCode.Template.Persistence;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;

namespace BccCode.Template.Admin;

public sealed record GetStatsResponse(int ActiveNotes, int ArchivedNotes);

public sealed record NoteCounts(int Active, int Archived);

/// <summary>Under <c>/api/admin</c>, so the admin policy applies by convention without an attribute.</summary>
public static class GetStatsEndpoint
{
    public static async Task<NoteCounts> LoadAsync(TemplateDb db, CancellationToken ct)
    {
        // The archived notes are the ones the query filter hides, so the count asks for them explicitly.
        var notes = db.Notes.IgnoreQueryFilters();
        return new(await notes.CountAsync(n => n.ArchivedAt == null, ct), await notes.CountAsync(n => n.ArchivedAt != null, ct));
    }

    [WolverineGet("/api/admin/stats")]
    [NonTransactional]   // a read: no transaction and no envelope row to open
    public static GetStatsResponse Get([NotBody] NoteCounts counts)
    {
        return new(counts.Active, counts.Archived);
    }
}
