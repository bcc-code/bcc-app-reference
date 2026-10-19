using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace BccCode.Template.Web.Auth;

/// <summary>One signed-in browser session. The ticket is encrypted; the other fields are what back-channel logout and expiry need.</summary>
public sealed record AuthSession(string Key, byte[] ProtectedTicket, string? Issuer, string? Sid, DateTimeOffset ExpiresAt);

/// <summary>
/// Where sessions are kept. Each persistence variant implements it (a Marten document, an EF table) and registers it as a
/// singleton, so the web plumbing stays the same for both. An expired session is treated as missing by the ticket store;
/// removing old rows is the variant's housekeeping.
/// </summary>
public interface IAuthSessionStorage
{
    Task SaveAsync(AuthSession session, CancellationToken ct);

    Task<AuthSession?> FindAsync(string key, CancellationToken ct);

    Task DeleteAsync(string key, CancellationToken ct);

    /// <summary>Deletes every session of one identity-provider session (back-channel logout). Returns how many.</summary>
    Task<int> DeleteByIdentityProviderSessionAsync(string issuer, string sid, CancellationToken ct);
}

/// <summary>
/// The server-side session (standard 14, rule 7): the cookie carries only a random key, so a session can be revoked and
/// a stolen cookie is useless once the row is gone.
/// </summary>
public sealed class SessionTicketStore(IAuthSessionStorage storage, IDataProtectionProvider dataProtection, TimeProvider time) : ITicketStore
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(8);

    private readonly IDataProtector _protector = dataProtection.CreateProtector("SessionTicketStore.v1");

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        await RenewAsync(key, ticket);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) =>
        storage.SaveAsync(new AuthSession(
            key,
            _protector.Protect(TicketSerializer.Default.Serialize(ticket)),
            ticket.Principal.FindFirst(TemplateClaims.Issuer)?.Value,
            ticket.Principal.FindFirst(TemplateClaims.SessionId)?.Value,
            ticket.Properties.ExpiresUtc ?? time.GetUtcNow().Add(DefaultLifetime)), CancellationToken.None);

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

    public Task RemoveAsync(string key) => storage.DeleteAsync(key, CancellationToken.None);
}

/// <summary>Wires the store into the cookie options when the container is built, so no service provider is built during registration.</summary>
internal sealed class SessionStorePostConfigure(SessionTicketStore store) : IPostConfigureOptions<CookieAuthenticationOptions>
{
    public void PostConfigure(string? name, CookieAuthenticationOptions options)
    {
        if (name == CookieAuthenticationDefaults.AuthenticationScheme) options.SessionStore = store;
    }
}
