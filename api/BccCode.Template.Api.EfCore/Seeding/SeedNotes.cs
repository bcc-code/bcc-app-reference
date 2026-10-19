using BccCode.Template.Api.EfCore.Configuration;
using BccCode.Template.Api.EfCore.Features.Notes;
using Microsoft.EntityFrameworkCore;
using Wolverine.Http;

namespace BccCode.Template.Api.EfCore.Seeding;

/// <summary>
/// The local test data: a few notes for the "user" persona (one archived, so the admin count has both). The ids are fixed,
/// so running the seeder again changes nothing: a note whose id already exists is skipped.
/// </summary>
public static class SeedNotes
{
    private static readonly Guid Welcome = Guid.Parse("0198f5a0-1000-7000-8000-000000000001");
    private static readonly Guid Groceries = Guid.Parse("0198f5a0-1000-7000-8000-000000000002");
    private static readonly Guid Retired = Guid.Parse("0198f5a0-1000-7000-8000-000000000003");

    public static IReadOnlyList<Note> Plan(string userSubject, DateTimeOffset now)
    {
        var retired = Make(Retired, userSubject, "Old plan", "Archived, so it only shows in the admin count.", now.AddDays(-1));
        retired.Archive(now.AddHours(-12));
        return
        [
            Make(Welcome, userSubject, "Welcome", "This note was seeded for the user persona.", now.AddDays(-3)),
            Make(Groceries, userSubject, "Groceries", "Milk, bread, coffee.", now.AddDays(-2)),
            retired,
        ];
    }

    /// <returns>How many notes were added.</returns>
    public static async Task<int> RunAsync(TemplateDbContext db, string userSubject, TimeProvider time, CancellationToken ct)
    {
        var plan = Plan(userSubject, time.GetUtcNow());
        var ids = plan.Select(n => n.Id).ToList();
        var existing = await db.Notes.Where(n => ids.Contains(n.Id)).Select(n => n.Id).ToListAsync(ct);
        var missing = plan.Where(n => !existing.Contains(n.Id)).ToList();

        db.Notes.AddRange(missing);
        await db.SaveChangesAsync(ct);
        return missing.Count;
    }

    private static Note Make(Guid id, string owner, string title, string body, DateTimeOffset createdAt)
    {
        var (content, problem) = NoteContent.Parse(title, body);
        return ReferenceEquals(problem, WolverineContinue.NoProblems) ? Note.Create(id, owner, content, createdAt) : throw new InvalidOperationException($"The seed note '{title}' is not valid.");
    }
}
