using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BccCode.Template.Api.EfCore.Configuration;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the context without starting the API host (which would need Wolverine, OIDC and a
/// database). Creating a migration only reads the model, so the connection string is never used.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TemplateDbContext>
{
    public TemplateDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TemplateDbContext>();
        EfCoreConfiguration.ConfigureProvider(options, "Host=localhost;Database=design");
        return new TemplateDbContext(options.Options);
    }
}
