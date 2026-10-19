using BccCode.Template.Web.Auth;
using Marten;

namespace BccCode.Template.Api.Marten.Infrastructure;

/// <summary>The stored form of an <see cref="AuthSession"/>. The key is the document id.</summary>
public sealed class AuthSessionDocument
{
    public string Id { get; set; } = string.Empty;
    public byte[] ProtectedTicket { get; set; } = [];
    public string? Issuer { get; set; }
    public string? Sid { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>Sessions as Marten documents. Rows of expired sessions stay until a cleanup job removes them.</summary>
public sealed class MartenAuthSessions(IDocumentStore store) : IAuthSessionStorage
{
    public async Task SaveAsync(AuthSession session, CancellationToken ct)
    {
        await using var write = store.LightweightSession();
        write.Store(new AuthSessionDocument
        {
            Id = session.Key,
            ProtectedTicket = session.ProtectedTicket,
            Issuer = session.Issuer,
            Sid = session.Sid,
            ExpiresAt = session.ExpiresAt,
        });
        await write.SaveChangesAsync(ct);
    }

    public async Task<AuthSession?> FindAsync(string key, CancellationToken ct)
    {
        await using var read = store.QuerySession();
        var document = await read.LoadAsync<AuthSessionDocument>(key, ct);
        return document is null ? null : new AuthSession(document.Id, document.ProtectedTicket, document.Issuer, document.Sid, document.ExpiresAt);
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        await using var write = store.LightweightSession();
        write.Delete<AuthSessionDocument>(key);
        await write.SaveChangesAsync(ct);
    }

    public async Task<int> DeleteByIdentityProviderSessionAsync(string issuer, string sid, CancellationToken ct)
    {
        await using var write = store.LightweightSession();
        var sessions = await write.Query<AuthSessionDocument>().Where(d => d.Issuer == issuer && d.Sid == sid).ToListAsync(ct);
        foreach (var session in sessions) write.Delete(session);
        await write.SaveChangesAsync(ct);
        return sessions.Count;
    }
}
