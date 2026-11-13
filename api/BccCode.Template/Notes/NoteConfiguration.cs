using BccCode.Template.Changes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BccCode.Template.Notes;

/// <summary>The table of <see cref="Note"/>. It lives with the feature; <c>TemplateDb</c> picks it up by scanning the assembly.</summary>
public sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> builder)
    {
        HistoryMapping.ConfigureAudited(builder, "note");
        builder.Property(n => n.Title).HasMaxLength(NoteTitle.MaxLength).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(NoteBody.MaxLength).IsRequired();
        // Archived notes are hidden everywhere unless a query asks for them (IgnoreQueryFilters), so a read cannot forget the rule.
        builder.HasQueryFilter(n => n.RemovedAt == null);
        builder.HasIndex(n => new { n.OwnerId, n.CreatedAt }).HasFilter("removed_at is null");   // the list: own, active, newest first
    }
}

/// <summary>The twin of <see cref="NoteConfiguration"/>: <c>note_history</c>, created by <c>history_sync</c>, not by EF.</summary>
public sealed class NoteVersionConfiguration : IEntityTypeConfiguration<NoteVersion>
{
    public void Configure(EntityTypeBuilder<NoteVersion> builder)
    {
        HistoryMapping.ConfigureHistory(builder, "note");
    }
}
