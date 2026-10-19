using BccCode.Template.Api.Marten.Features.Notes;
using Marten;

namespace BccCode.Template.Api.Marten.Seeding;

/// <summary>
/// The local test data: a few notes for the "user" persona (one archived, so the admin count has both). The ids are fixed,
/// so running the seeder again changes nothing: a note whose stream already exists is skipped.
/// </summary>
public static class SeedNotes
{
    public sealed record Seeded(Guid Id, NoteCreated Created, NoteArchived? Archived = null);

    private static readonly Guid Welcome = Guid.Parse("0198f5a0-1000-7000-8000-000000000001");
    private static readonly Guid Groceries = Guid.Parse("0198f5a0-1000-7000-8000-000000000002");
    private static readonly Guid Retired = Guid.Parse("0198f5a0-1000-7000-8000-000000000003");

    public static IReadOnlyList<Seeded> Plan(string userSubject, DateTimeOffset now) =>
    [
        new(Welcome, new NoteCreated(userSubject, "Welcome", "This note was seeded for the user persona.", now.AddDays(-3))),
        new(Groceries, new NoteCreated(userSubject, "Groceries", "Milk, bread, coffee.", now.AddDays(-2))),
        new(Retired, new NoteCreated(userSubject, "Old plan", "Archived, so it only shows in the admin count.", now.AddDays(-1)),
            new NoteArchived(now.AddHours(-12))),
    ];

    /// <returns>How many notes were added.</returns>
    public static async Task<int> RunAsync(IDocumentStore store, string userSubject, TimeProvider time, CancellationToken ct)
    {
        var added = 0;
        await using var session = store.LightweightSession();
        foreach (var note in Plan(userSubject, time.GetUtcNow()))
        {
            if (await session.Events.FetchStreamStateAsync(note.Id, ct) is not null) continue;
            object[] events = note.Archived is null ? [note.Created] : [note.Created, note.Archived];
            session.Events.StartStream<Note>(note.Id, events);
            added++;
        }

        await session.SaveChangesAsync(ct);
        return added;
    }
}
