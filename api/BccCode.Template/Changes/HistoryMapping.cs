using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BccCode.Template.Changes;

/// <summary>
/// Maps an <see cref="Audited"/> table and its <c>_history</c> twin. A feature has two <c>IEntityTypeConfiguration</c> classes:
/// the row's calls <see cref="ConfigureAudited{TRow}"/>, the twin's calls <see cref="ConfigureHistory{TVersion}"/>. The twin's
/// class derives from the row's class, so a new property is mapped in both; the migration adds the column to the twin with
/// <c>select history_sync('table')</c> (<see cref="HistorySql"/>), and the history parity test fails if it is missing there.
/// The twin is created by that function, not by EF, so EF leaves it out of migrations.
/// </summary>
/// <remarks>
/// Enums are stored as text (<c>HasConversion&lt;string&gt;()</c>). Write it on the row's configuration and on the twin's, because
/// the twin is a separate entity type and does not inherit the row's settings.
/// </remarks>
public static class HistoryMapping
{
    public static void ConfigureAudited<TRow>(EntityTypeBuilder<TRow> b, string table)
        where TRow : Audited
    {
        b.ToTable(table);
        b.HasKey(r => r.Id).HasName("pk_" + table);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.Version).IsConcurrencyToken();
        b.HasOne(r => r.Change).WithMany().HasForeignKey(r => r.ChangeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(r => r.ChangeId);
    }

    public static void ConfigureHistory<TVersion>(EntityTypeBuilder<TVersion> b, string table)
        where TVersion : Audited, IReplacedVersion
    {
        // Its own table with every column of the row, not an inheritance mapping.
        b.HasBaseType((Type?)null);
        b.ToTable(table + "_history", t => t.ExcludeFromMigrations());
        b.HasKey(v => new { v.Id, v.Version });
        b.Ignore(v => v.Change);
        b.Property(v => v.ReplacedAt);
        b.Property(v => v.ReplacedBy);
    }
}
