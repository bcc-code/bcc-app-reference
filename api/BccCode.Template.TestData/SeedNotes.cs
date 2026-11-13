using BccCode.Template.Changes;
using BccCode.Template.Notes;
using BccCode.Template.Persistence;
using Microsoft.EntityFrameworkCore;
using OidcMock;

namespace BccCode.Template.TestData;

/// <summary>One seeded note: the id is fixed, <c>ArchivedAt</c> is set for a note that was archived after it was created.</summary>
public sealed record SeedNote(Guid Id, string Title, string Body, DateTimeOffset CreatedAt, DateTimeOffset? ArchivedAt = null);

/// <summary>
/// The local test data: a few notes for the "user" persona (one archived, so the admin count has both). The ids are fixed,
/// so running the seeder again changes nothing: a note whose id already exists is skipped. Each note is written through its
/// own methods with a change that has no actor and the time the note should have, so the history is as a real one.
/// </summary>
public static class SeedNotes
{
    /// <summary>The owner of the seeded notes: the "user" persona of the OIDC mock.</summary>
    public static Guid Owner { get; } = Guid.Parse(Persona.User.PersonUid);

    private static readonly Guid Welcome = Guid.Parse("0198f5a0-1000-7000-8000-000000000001");
    private static readonly Guid Groceries = Guid.Parse("0198f5a0-1000-7000-8000-000000000002");
    private static readonly Guid Retired = Guid.Parse("0198f5a0-1000-7000-8000-000000000003");

    public static IReadOnlyList<SeedNote> Plan(DateTimeOffset now)
    {
        return
        [
            new(Welcome, "Welcome", "This note was seeded for the user persona.", now.AddDays(-3)),
            new(Groceries, "Groceries", "Milk, bread, coffee.", now.AddDays(-2)),
            new(Retired, "Old plan", "Archived, so it only shows in the admin count.", now.AddDays(-1), ArchivedAt: now.AddHours(-12)),
        ];
    }

    /// <returns>How many notes were added.</returns>
    public static async Task<int> RunAsync(TemplateDb db, Guid ownerPersonUid, TimeProvider time, CancellationToken ct)
    {
        var plan = Plan(time.GetUtcNow());
        var ids = plan.Select(n => n.Id).ToList();
        // Archived notes are hidden by the query filter, so a seeded archived note is only found when the filter is off.
        var existing = await db.Notes.IgnoreQueryFilters().Where(n => ids.Contains(n.Id)).Select(n => n.Id).ToListAsync(ct);

        var added = 0;
        foreach (var seed in plan.Where(n => !existing.Contains(n.Id)))
        {
            if (NoteContent.TryParse(seed.Title, seed.Body, out var content) is not null)
                throw new InvalidOperationException($"The seed note '{seed.Title}' is not valid.");

            var note = Note.Create(seed.Id, new Change(Guid.CreateVersion7(seed.CreatedAt), null, seed.CreatedAt), ownerPersonUid, content);
            db.Notes.Add(note);
            await db.SaveChangesAsync(ct);
            if (seed.ArchivedAt is { } archivedAt)
            {
                // A second save, so the first version lands in the history as it would have in the app.
                note.Archive(new Change(Guid.CreateVersion7(archivedAt), null, archivedAt));
                await db.SaveChangesAsync(ct);
            }

            added++;
        }

        return added;
    }
}
