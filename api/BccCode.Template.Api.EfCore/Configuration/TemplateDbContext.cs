using BccCode.Template.Api.EfCore.Features.Notes;
using BccCode.Template.Api.EfCore.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BccCode.Template.Api.EfCore.Configuration;

/// <summary>
/// The one database context. Each entity's table is described next to the entity (an <c>IEntityTypeConfiguration</c> in its
/// feature or infrastructure file), and this class finds them by scanning the assembly, so a new feature adds its table
/// without an edit here. Endpoints use it directly: no repository on top of it (standard 12).
/// </summary>
public sealed class TemplateDbContext(DbContextOptions<TemplateDbContext> options) : DbContext(options)
{
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<AuthSessionRow> AuthSessions => Set<AuthSessionRow>();
    public DbSet<DataProtectionKeyRow> DataProtectionKeys => Set<DataProtectionKeyRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TemplateDbContext).Assembly);
}
