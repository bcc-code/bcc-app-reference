using BccCode.Template.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BccCode.Template.Tests.Persistence;

/// <summary>
/// The AppHost starts a new database from the committed snapshot (<c>api/BccCode.Template.TestData/Snapshots</c>). A snapshot older than
/// the newest migration still works (the schema step applies what is newer), but it makes every fresh start slower and hides a seeder
/// that no longer matches the schema, so the build says when it is stale.
/// </summary>
public sealed class DatabaseSnapshotTests
{
    [Fact]
    public void The_snapshot_contains_the_newest_migration()
    {
        using var db = new DesignTimeTemplateDbFactory().CreateDbContext([]);
        var newest = db.Database.GetMigrations().Last();
        var history = Path.Combine(FindRoot(), "api", "BccCode.Template.TestData", "Snapshots", $"100-public.{TemplateDb.MigrationsHistoryTable}.sql");

        File.Exists(history).Should().BeTrue("the snapshot is missing: run node tools/db-snapshot/update.mjs");
        File.ReadAllText(history).Should().Contain(newest, $"the snapshot is older than the migration {newest}: run node tools/db-snapshot/update.mjs");
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BccCode.Template.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException("Could not find BccCode.Template.slnx above the test output directory.");
    }
}
