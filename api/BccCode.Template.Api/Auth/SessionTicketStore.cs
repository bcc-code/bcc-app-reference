using System.Security.Cryptography;
using BccCode.Template.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace BccCode.Template.Api.Auth;

/// <summary>One signed-in browser session. The ticket is encrypted; the other fields are what back-channel logout and expiry need.</summary>
public sealed record AuthSession(string Key, byte[] ProtectedTicket, string Issuer, string? Sid, string Subject, DateTimeOffset ExpiresAt);

/// <summary>Where sessions are kept (<c>EfAuthSessions</c>, a row of <c>auth_sessions</c>), registered as a singleton.</summary>
public interface IAuthSessionStorage
{
    Task AddAsync(AuthSession session, CancellationToken cancellationToken);

    /// <summary>Replaces the ticket and the expiry of a session. Returns false, and writes nothing, when the row is gone.</summary>
    Task<bool> UpdateAsync(AuthSession session, CancellationToken cancellationToken);

    Task<AuthSession?> FindAsync(string key, CancellationToken cancellationToken);

    Task DeleteAsync(string key, CancellationToken cancellationToken);

    /// <summary>Deletes the sessions that expired before <paramref name="now"/>.</summary>
    Task DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Deletes every session of one identity-provider session (back-channel logout with a <c>sid</c>). Returns how many.</summary>
    Task<int> DeleteByIdentityProviderSessionAsync(string issuer, string sid, CancellationToken cancellationToken);

    /// <summary>Deletes every session of one subject at one issuer (back-channel logout without a <c>sid</c>). Returns how many.</summary>
    Task<int> DeleteBySubjectAsync(string issuer, string subject, CancellationToken cancellationToken);
}

/// <summary>
/// The server-side session (standard 14, rule 7): the cookie carries only a random key, so a session can be revoked and
/// a stolen cookie is useless once the row is gone. There are no background jobs (Wolverine serverless), so rows of expired
/// sessions are deleted when a new session is stored.
/// </summary>
public sealed class SessionTicketStore(IAuthSessionStorage storage, IDataProtectionProvider dataProtection, TimeProvider time) : ITicketStore
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("SessionTicketStore.v1");

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        await storage.DeleteExpiredAsync(time.GetUtcNow(), CancellationToken.None);
        await storage.AddAsync(ToSession(key, ticket), CancellationToken.None);
        return key;
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        // Removed while the request ran (logout, back-channel logout): the row stays gone, an upsert would bring it back.
        await storage.UpdateAsync(ToSession(key, ticket), CancellationToken.None);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        var session = await storage.FindAsync(key, CancellationToken.None);
        if (session is null || session.ExpiresAt <= time.GetUtcNow()) return null;
        try
        {
            return TicketSerializer.Default.Deserialize(_protector.Unprotect(session.ProtectedTicket));
        }
        catch (CryptographicException)
        {
            // Keys rotated away or a damaged row: the user signs in again.
            return null;
        }
    }

    public Task RemoveAsync(string key)
    {
        return storage.DeleteAsync(key, CancellationToken.None);
    }

    private AuthSession ToSession(string key, AuthenticationTicket ticket)
    {
        var principal = ticket.Principal;
        var issuer = principal.FindFirst(AuthClaims.Issuer)?.Value ?? throw new InvalidOperationException("The session has no iss claim.");
        var subject = principal.FindFirst(AuthClaims.Subject)?.Value ?? throw new InvalidOperationException("The session has no sub claim.");
        return new AuthSession(
            key,
            _protector.Protect(TicketSerializer.Default.Serialize(ticket)),
            issuer,
            principal.FindFirst(AuthClaims.SessionId)?.Value,
            subject,
            // The cookie handler sets ExpiresUtc before it stores; the fallback only applies to a ticket without one.
            ticket.Properties.ExpiresUtc ?? time.GetUtcNow().Add(AuthConfiguration.SessionLifetime));
    }
}

/// <summary>Wires the store into the cookie options when the container is built, so no service provider is built during registration.</summary>
internal sealed class SessionStorePostConfigure(SessionTicketStore store) : IPostConfigureOptions<CookieAuthenticationOptions>
{
    public void PostConfigure(string? name, CookieAuthenticationOptions options)
    {
        if (name == CookieAuthenticationDefaults.AuthenticationScheme) options.SessionStore = store;
    }
}
