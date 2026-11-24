using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BccCode.Template.Persistence;

/// <summary>
/// The model <c>dotnet ef migrations add</c> and <c>has-pending-model-changes</c> compare against: no schema, as the
/// migrations are applied with <c>search_path</c> set. It never connects.
/// </summary>
public sealed class DesignTimeTemplateDbFactory : IDesignTimeDbContextFactory<TemplateDb>
{
    public TemplateDb CreateDbContext(string[] args)
    {
        var options = TemplateDb.Configure(new DbContextOptionsBuilder<TemplateDb>())
            .UseNpgsql("Host=localhost;Database=design-time", npgsql => npgsql.MigrationsHistoryTable(TemplateDb.MigrationsHistoryTable))
            .Options;
        return new TemplateDb((DbContextOptions<TemplateDb>)options, TemplateDbSchema.ForMigrations);
    }
}
