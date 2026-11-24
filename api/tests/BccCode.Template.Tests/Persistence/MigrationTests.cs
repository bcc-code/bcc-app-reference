using BccCode.Template.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BccCode.Template.Tests.Persistence;

/// <summary>The migrations describe the model, so the schema step creates what the app maps.</summary>
public sealed class MigrationTests
{
    [Fact]
    public void Every_model_change_has_a_migration()
    {
        using var db = new DesignTimeTemplateDbFactory().CreateDbContext([]);

        db.Database.HasPendingModelChanges().Should().BeFalse("run dotnet ef migrations add (project api/BccCode.Template, startup project api/BccCode.Template.Api)");
    }
}
