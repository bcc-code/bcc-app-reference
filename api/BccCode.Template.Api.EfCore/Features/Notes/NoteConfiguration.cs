using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BccCode.Template.Api.EfCore.Features.Notes;

/// <summary>The table of <see cref="Note"/>. It lives with the feature; <c>TemplateDbContext</c> picks it up by scanning the assembly.</summary>
public sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> builder)
    {
        builder.ToTable("notes");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();   // ids are version 7 UUIDs made by the app
        builder.Property(n => n.OwnerId).HasMaxLength(200).IsRequired();
        builder.Property(n => n.Title).HasMaxLength(NoteTitle.MaxLength).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(NoteBody.MaxLength).IsRequired();
        // An explicit integer instead of Postgres xmin: the API returns the new version in the same response as the write,
        // and xmin is only known after the commit. EF adds "and version = <the value that was read>" to every update, so a
        // second writer gets DbUpdateConcurrencyException (answered as 409) instead of overwriting.
        builder.Property(n => n.Version).IsConcurrencyToken();
        builder.HasIndex(n => new { n.OwnerId, n.CreatedAt }).HasFilter("NOT archived");   // the list: own, active, newest first
    }
}
