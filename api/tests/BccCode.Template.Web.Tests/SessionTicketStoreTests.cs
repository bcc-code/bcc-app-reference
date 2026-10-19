using System.Security.Claims;
using BccCode.Template.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Time.Testing;

namespace BccCode.Template.Web.Tests;

public class SessionTicketStoreTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero));
    private readonly InMemorySessions _sessions = new();
    private readonly SessionTicketStore _store;

    public SessionTicketStoreTests() => _store = new SessionTicketStore(_sessions, new EphemeralDataProtectionProvider(), _time);

    private static AuthenticationTicket Ticket(string sub = "subject-1", string sid = "sid-1", DateTimeOffset? expires = null) =>
        new(
            SessionClaims.Slim(
                new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", sub), new Claim("name", "Una User"), new Claim("sid", sid), new Claim("email", "dropped@example.test")], "oidc")),
                "https://idp.example"),
            new AuthenticationProperties { ExpiresUtc = expires },
            "Cookies");

    [Fact]
    public async Task A_stored_ticket_comes_back_with_the_same_claims()
    {
        var key = await _store.StoreAsync(Ticket());

        var ticket = await _store.RetrieveAsync(key);

        ticket!.Principal.FindFirst("sub")!.Value.Should().Be("subject-1");
        ticket.Principal.FindFirst("name")!.Value.Should().Be("Una User");
    }

    [Fact]
    public async Task The_cookie_key_is_random_and_the_ticket_is_not_readable_in_storage()
    {
        var first = await _store.StoreAsync(Ticket());
        var second = await _store.StoreAsync(Ticket());

        first.Should().NotBe(second).And.HaveLength(43);
        System.Text.Encoding.UTF8.GetString(_sessions.Rows[first].ProtectedTicket).Should().NotContain("subject-1");
    }

    [Fact]
    public async Task Only_the_claims_the_app_reads_are_kept()
    {
        var ticket = await _store.RetrieveAsync(await _store.StoreAsync(Ticket()));

        ticket!.Principal.Claims.Select(c => c.Type).Should().BeEquivalentTo("sub", "name", "sid", "iss");
    }

    [Fact]
    public async Task The_issuer_and_the_identity_provider_session_are_stored_for_back_channel_logout()
    {
        var key = await _store.StoreAsync(Ticket(sid: "sid-9"));

        _sessions.Rows[key].Should().Match<AuthSession>(s => s.Issuer == "https://idp.example" && s.Sid == "sid-9");
    }

    [Fact]
    public async Task An_expired_session_is_treated_as_missing()
    {
        var key = await _store.StoreAsync(Ticket(expires: _time.GetUtcNow().AddMinutes(10)));
        _time.Advance(TimeSpan.FromMinutes(11));

        (await _store.RetrieveAsync(key)).Should().BeNull();
    }

    [Fact]
    public async Task A_removed_session_cannot_be_retrieved()
    {
        var key = await _store.StoreAsync(Ticket());

        await _store.RemoveAsync(key);

        (await _store.RetrieveAsync(key)).Should().BeNull();
    }

    [Fact]
    public async Task A_ticket_that_cannot_be_decrypted_is_treated_as_missing()
    {
        var key = await _store.StoreAsync(Ticket());
        _sessions.Rows[key] = _sessions.Rows[key] with { ProtectedTicket = [1, 2, 3] };

        (await _store.RetrieveAsync(key)).Should().BeNull();
    }

    [Fact]
    public async Task Renewing_keeps_the_key_and_replaces_the_ticket()
    {
        var key = await _store.StoreAsync(Ticket());

        await _store.RenewAsync(key, Ticket(sub: "subject-2"));

        (await _store.RetrieveAsync(key))!.Principal.FindFirst("sub")!.Value.Should().Be("subject-2");
    }

    private sealed class InMemorySessions : IAuthSessionStorage
    {
        public Dictionary<string, AuthSession> Rows { get; } = [];

        public Task SaveAsync(AuthSession session, CancellationToken ct)
        {
            Rows[session.Key] = session;
            return Task.CompletedTask;
        }

        public Task<AuthSession?> FindAsync(string key, CancellationToken ct) => Task.FromResult(Rows.GetValueOrDefault(key));

        public Task DeleteAsync(string key, CancellationToken ct)
        {
            Rows.Remove(key);
            return Task.CompletedTask;
        }

        public Task<int> DeleteByIdentityProviderSessionAsync(string issuer, string sid, CancellationToken ct) =>
            Task.FromResult(Rows.Values.Where(s => s.Issuer == issuer && s.Sid == sid).ToList().Count(s => Rows.Remove(s.Key)));
    }
}
