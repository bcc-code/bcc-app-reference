using BccCode.Template.Notes;
using BccCode.Template.Persistence;
using Microsoft.EntityFrameworkCore;
using OidcMock;

namespace BccCode.Template.TestData;

/// <summary>One seeded note: the id is fixed, <c>ArchivedAt</c> is set for a note that was archived after it was created.</summary>
public sealed record SeedNote(Guid Id, string Title, string Body, DateTimeOffset CreatedAt, DateTimeOffset? ArchivedAt = null);

/// <summary>
/// The local test data: a few notes for the "user" persona (one archived, so the admin count has both). The ids and the times
/// are fixed, so the same seed gives the same rows byte for byte (the committed database snapshot depends on it), and running
/// the seeder again changes nothing: a note whose id already exists is skipped. Each note is written through its own methods with
/// the time the note should have, in the order of <see cref="Plan"/>.
/// </summary>
public static class SeedNotes
{
    /// <summary>The owner of the seeded notes: the "user" persona of the OIDC mock.</summary>
    public static Guid Owner { get; } = Guid.Parse(Persona.User.PersonUid);

    private static readonly Guid Welcome = Guid.Parse("0198f5a0-1000-7000-8000-000000000001");
    private static readonly Guid Groceries = Guid.Parse("0198f5a0-1000-7000-8000-000000000002");
    private static readonly Guid Retired = Guid.Parse("0198f5a0-1000-7000-8000-000000000003");

    // A fixed date, never the clock: a seed that depends on "now" would give a different snapshot on every run.
    private static readonly DateTimeOffset SeededAt = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<SeedNote> Plan()
    {
        return
        [
            new(Welcome, "Welcome", "This note was seeded for the user persona.", SeededAt),
            new(Groceries, "Groceries", "Milk, bread, coffee.", SeededAt.AddDays(1)),
            new(Retired, "Old plan", "Archived, so it only shows in the admin count.", SeededAt.AddDays(2), ArchivedAt: SeededAt.AddDays(2).AddHours(3)),
        ];
    }

    /// <returns>How many notes were added.</returns>
    public static async Task<int> RunAsync(TemplateDb db, Guid ownerPersonUid, CancellationToken ct)
    {
        var plan = Plan();
        var ids = plan.Select(n => n.Id).ToList();
        // Archived notes are hidden by the query filter, so a seeded archived note is only found when the filter is off.
        var existing = await db.Notes.IgnoreQueryFilters().Where(n => ids.Contains(n.Id)).Select(n => n.Id).ToListAsync(ct);

        var added = 0;
        foreach (var seed in plan.Where(n => !existing.Contains(n.Id)))
        {
            if (NoteContent.TryParse(seed.Title, seed.Body, out var content) is not null)
                throw new InvalidOperationException($"The seed note '{seed.Title}' is not valid.");

            var note = Note.Create(seed.Id, seed.CreatedAt, ownerPersonUid, content);
            if (seed.ArchivedAt is { } archivedAt) note.Archive(archivedAt);
            db.Notes.Add(note);
            await db.SaveChangesAsync(ct);

            added++;
        }

        return added;
    }
}
