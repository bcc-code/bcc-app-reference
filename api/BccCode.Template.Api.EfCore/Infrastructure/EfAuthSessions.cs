using BccCode.Template.Api.EfCore.Configuration;
using BccCode.Template.Web.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BccCode.Template.Api.EfCore.Infrastructure;

/// <summary>The stored form of an <see cref="AuthSession"/>. The key is the primary key.</summary>
public sealed class AuthSessionRow
{
    public string Key { get; set; } = string.Empty;
    public byte[] ProtectedTicket { get; set; } = [];
    public string? Issuer { get; set; }
    public string? Sid { get; set; }
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
    }
}

/// <summary>Sessions as rows. Rows of expired sessions stay until a cleanup job removes them.</summary>
public sealed class EfAuthSessions(IDbContextFactory<TemplateDbContext> factory) : IAuthSessionStorage
{
    public async Task SaveAsync(AuthSession session, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.AuthSessions.FindAsync([session.Key], ct);
        if (row is null)
        {
            row = new AuthSessionRow { Key = session.Key };
            db.AuthSessions.Add(row);
        }

        (row.ProtectedTicket, row.Issuer, row.Sid, row.ExpiresAt) = (session.ProtectedTicket, session.Issuer, session.Sid, session.ExpiresAt);
        await db.SaveChangesAsync(ct);
    }

    public async Task<AuthSession?> FindAsync(string key, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.AuthSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);
        return row is null ? null : new AuthSession(row.Key, row.ProtectedTicket, row.Issuer, row.Sid, row.ExpiresAt);
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.AuthSessions.Where(s => s.Key == key).ExecuteDeleteAsync(ct);
    }

    public async Task<int> DeleteByIdentityProviderSessionAsync(string issuer, string sid, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.AuthSessions.Where(s => s.Issuer == issuer && s.Sid == sid).ExecuteDeleteAsync(ct);
    }
}
