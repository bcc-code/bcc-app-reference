using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BccCode.Template.Persistence;

// The two tables the host needs next to the features' data. They are not audited, have no history and hold no business data.
// They are mapped here, not in the host, because the migrations belong to this project: one model, one migration history.
// The stores that read and write them (EfAuthSessions, EfDataProtectionKeys) are the host's own.

/// <summary>The stored form of a sign-in session. The key is the primary key. The subject is the identity provider's id of the person (the <c>sub</c> claim).</summary>
public sealed class AuthSessionRow
{
    public string Key { get; set; } = string.Empty;
    public byte[] ProtectedTicket { get; set; } = [];
    public string Issuer { get; set; } = string.Empty;
    public string? Sid { get; set; }
    public string Subject { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class AuthSessionRowConfiguration : IEntityTypeConfiguration<AuthSessionRow>
{
    public void Configure(EntityTypeBuilder<AuthSessionRow> builder)
    {
        builder.ToTable("auth_sessions");
        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key).HasMaxLength(100);
        builder.HasIndex(s => new { s.Issuer, s.Sid });   // back-channel logout finds sessions by them
        builder.HasIndex(s => new { s.Issuer, s.Subject });   // and, when the logout token has no sid, by the subject
    }
}

/// <summary>A Data Protection key ring entry, shared by every API instance.</summary>
public sealed class DataProtectionKeyRow
{
    public string FriendlyName { get; set; } = string.Empty;
    public string Xml { get; set; } = string.Empty;
}

public sealed class DataProtectionKeyRowConfiguration : IEntityTypeConfiguration<DataProtectionKeyRow>
{
    public void Configure(EntityTypeBuilder<DataProtectionKeyRow> builder)
    {
        builder.ToTable("data_protection_keys");
        builder.HasKey(k => k.FriendlyName);
        builder.Property(k => k.FriendlyName).HasMaxLength(200);
    }
}
