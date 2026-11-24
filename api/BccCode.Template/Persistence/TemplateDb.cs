using BccCode.Template.Notes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace BccCode.Template.Persistence;

/// <summary>
/// The relational store: ordinary tables. Each feature maps its tables in its own <c>IEntityTypeConfiguration</c>; this class finds them by
/// scanning the assembly, so a new feature adds its table without an edit here. Endpoints use it directly: no repository
/// on top of it (standard 12).
/// </summary>
/// <param name="options">The settings from <see cref="Configure"/> and the provider.</param>
/// <param name="schema">
/// The Postgres schema the tables are in (<see cref="PersistenceSchema"/>; a PR preview has its own). Null for migrations,
/// which are written without a schema and applied with <c>search_path</c> set (the host's <c>TemplateMigrations</c>).
/// </param>
public sealed class TemplateDb(DbContextOptions<TemplateDb> options, TemplateDbSchema schema) : DbContext(options)
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public string? Schema { get; } = schema.Name;

    public DbSet<Note> Notes => Set<Note>();

    // The host's sign-in sessions and the Data Protection key ring.
    public DbSet<AuthSessionRow> AuthSessions => Set<AuthSessionRow>();

    public DbSet<DataProtectionKeyRow> DataProtectionKeys => Set<DataProtectionKeyRow>();

    /// <summary>
    /// Deletes every row of every table in the model without firing triggers, except the tables of the entity
    /// types in <paramref name="keep"/>. Only the seeder's reset and the integration tests call it; they run only against
    /// disposable databases.
    /// </summary>
    public async Task DeleteAllDataAsync(IReadOnlyCollection<Type>? keep = null, CancellationToken cancellationToken = default)
    {
        var tables = Model.GetEntityTypes().Where(e => keep?.Contains(e.ClrType) != true).Select(e => e.GetTableName()!).Distinct()
            .Select(table => Schema is null ? Quote(table) : $"{Quote(Schema)}.{Quote(table)}");
#pragma warning disable EF1002 // Table names from the model, quoted.
        await Database.ExecuteSqlRawAsync($"truncate {string.Join(", ", tables)}", cancellationToken);
#pragma warning restore EF1002
    }

    private static string Quote(string identifier)
    {
        return new NpgsqlCommandBuilder().QuoteIdentifier(identifier);
    }

    /// <summary>The settings every <see cref="TemplateDb"/> shares: the app's, the migrations' and the design-time factory's.</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder builder)
    {
        return builder
            .UseSnakeCaseNamingConvention()
            // One model per schema: the app's is qualified with its schema, the migrations' is not.
            .ReplaceService<IModelCacheKeyFactory, SchemaModelCacheKeyFactory>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (Schema is not null) modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TemplateDb).Assembly);
    }

    private sealed class SchemaModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
        {
            return (context.GetType(), (context as TemplateDb)?.Schema, designTime);
        }
    }
}

/// <summary>The schema a <see cref="TemplateDb"/> maps its tables to; <see cref="ForMigrations"/> maps none.</summary>
public sealed record TemplateDbSchema(string? Name)
{
    public static readonly TemplateDbSchema ForMigrations = new((string?)null);
}
