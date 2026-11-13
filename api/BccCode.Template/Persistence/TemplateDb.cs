using BccCode.Template.Changes;
using BccCode.Template.Notes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace BccCode.Template.Persistence;

/// <summary>
/// The relational store (ADR 0028): ordinary tables, one typed <c>_history</c> twin per audited table and the table of
/// business steps. Each feature maps its tables in its own <c>IEntityTypeConfiguration</c>; this class finds them by
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

    public DbSet<Change> Changes => Set<Change>();

    public DbSet<Actor> Actors => Set<Actor>();

    public DbSet<Note> Notes => Set<Note>();

    public DbSet<NoteVersion> NoteVersions => Set<NoteVersion>();

    // Not audited: the host's sign-in sessions and the Data Protection key ring have no history.
    public DbSet<AuthSessionRow> AuthSessions => Set<AuthSessionRow>();

    public DbSet<DataProtectionKeyRow> DataProtectionKeys => Set<DataProtectionKeyRow>();

    /// <summary>
    /// Deletes every row of every table in the model, history included, without firing triggers, except the tables of the entity
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

    /// <summary>The quoted, schema-qualified table of an entity, for the few statements EF cannot write.</summary>
    public string TableOf<T>()
    {
        var entity = Model.FindEntityType(typeof(T)) ?? throw new InvalidOperationException($"{typeof(T).Name} is not mapped.");
        var table = Quote(entity.GetTableName()!);
        var tableSchema = entity.GetSchema();
        return tableSchema is null ? table : $"{Quote(tableSchema)}.{table}";
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

        modelBuilder.Entity<Change>(b =>
        {
            b.ToTable("change");
            b.HasKey(c => c.Id);
            // Created in code. Not "generated", so EF inserts a new change it finds through a row's navigation.
            b.Property(c => c.Id).ValueGeneratedNever();
            b.Property(c => c.Type).IsRequired();
            b.Property(c => c.Position).UseIdentityAlwaysColumn();
            b.HasIndex(c => new { c.SubjectId, c.Position });
            b.HasOne<Actor>().WithMany().HasForeignKey(c => c.ActorId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Actor>(b =>
        {
            b.ToTable("actor");
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).ValueGeneratedNever();
            b.HasIndex(a => a.PersonUid).IsUnique();
        });

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
