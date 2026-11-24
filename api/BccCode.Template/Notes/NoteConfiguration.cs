using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BccCode.Template.Notes;

/// <summary>The table of <see cref="Note"/>. It lives with the feature; <c>TemplateDb</c> picks it up by scanning the assembly.</summary>
public sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> builder)
    {
        builder.ToTable("note");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();   // created in code (a version 7 uuid)
        builder.Property(n => n.Version).IsConcurrencyToken();   // a stale save fails and answers 409
        builder.Property(n => n.Title).HasMaxLength(NoteTitle.MaxLength).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(NoteBody.MaxLength).IsRequired();
        // Archived notes are hidden everywhere unless a query asks for them (IgnoreQueryFilters), so a read cannot forget the rule.
        builder.HasQueryFilter(n => n.ArchivedAt == null);
        builder.HasIndex(n => new { n.OwnerId, n.CreatedAt }).HasFilter("archived_at is null");   // the list: own, active, newest first
    }
}
