using BccCode.Template.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BccCode.Template.Api.Auth;

/// <summary>Sessions as rows of <c>auth_sessions</c>.</summary>
public sealed class EfAuthSessions(IDbContextFactory<TemplateDb> factory) : IAuthSessionStorage
{
    public async Task AddAsync(AuthSession session, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.AuthSessions.Add(new AuthSessionRow
        {
            Key = session.Key,
            ProtectedTicket = session.ProtectedTicket,
            Issuer = session.Issuer,
            Sid = session.Sid,
            Subject = session.Subject,
            ExpiresAt = session.ExpiresAt,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(AuthSession session, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var updated = await db.AuthSessions.Where(s => s.Key == session.Key).ExecuteUpdateAsync(
            set => set.SetProperty(s => s.ProtectedTicket, session.ProtectedTicket).SetProperty(s => s.ExpiresAt, session.ExpiresAt), cancellationToken);
        return updated > 0;
    }

    public async Task<AuthSession?> FindAsync(string key, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.AuthSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        return row is null ? null : new AuthSession(row.Key, row.ProtectedTicket, row.Issuer, row.Sid, row.Subject, row.ExpiresAt);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await db.AuthSessions.Where(s => s.Key == key).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await db.AuthSessions.Where(s => s.ExpiresAt < now).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> DeleteByIdentityProviderSessionAsync(string issuer, string sid, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.AuthSessions.Where(s => s.Issuer == issuer && s.Sid == sid).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> DeleteBySubjectAsync(string issuer, string subject, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.AuthSessions.Where(s => s.Issuer == issuer && s.Subject == subject).ExecuteDeleteAsync(cancellationToken);
    }
}
