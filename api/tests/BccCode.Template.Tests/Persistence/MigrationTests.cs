using BccCode.Template.Notes;
using BccCode.Template.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BccCode.Template.Tests.Persistence;

/// <summary>ADR 0028: the migrations describe the model, so the schema step creates what the app maps.</summary>
public sealed class MigrationTests
{
    [Fact]
    public void Every_model_change_has_a_migration()
    {
        using var db = new DesignTimeTemplateDbFactory().CreateDbContext([]);

        db.Database.HasPendingModelChanges().Should().BeFalse("run dotnet ef migrations add (project api/BccCode.Template, startup project api/BccCode.Template.Api)");
    }

    [Fact]
    public void The_history_twins_are_in_the_model_but_not_in_the_migrations()
    {
        using var db = new DesignTimeTemplateDbFactory().CreateDbContext([]);
        var model = db.GetService<IDesignTimeModel>().Model;

        // The twin is created by history_sync from the migration, so EF must not create or alter it.
        var twin = model.FindEntityType(typeof(NoteVersion))!;
        twin.GetTableName().Should().Be("note_history");
        model.GetEntityTypes().Where(e => e.GetTableName()!.EndsWith("_history", StringComparison.Ordinal))
            .Should().NotBeEmpty().And.OnlyContain(e => e.IsTableExcludedFromMigrations());
    }
}
