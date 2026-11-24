using System.Security.Claims;
using BccCode.Template.Api.Auth;
using BccCode.Template.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Time.Testing;

namespace BccCode.Template.Tests;

public class SessionTicketStoreTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero));
    private readonly InMemorySessions _sessions = new();
    private readonly SessionTicketStore _store;

    public SessionTicketStoreTests() => _store = new SessionTicketStore(_sessions, new EphemeralDataProtectionProvider(), _time);

    private static AuthenticationTicket Ticket(string person = "a0000000-0000-4000-8000-000000000001", string sid = "sid-1", DateTimeOffset? expires = null, string subject = "subject-1")
    {
        return new(
            SessionClaims.FromIdToken(
                new ClaimsPrincipal(new ClaimsIdentity([new Claim(AuthClaims.Subject, subject), new Claim(AuthClaims.PersonUid, person), new Claim("name", "Una User"), new Claim("sid", sid), new Claim("email", "dropped@example.test")], "oidc")),
                "https://idp.example")!,
            new AuthenticationProperties { ExpiresUtc = expires },
            "Cookies");
    }

    [Fact]
    public async Task A_stored_ticket_comes_back_with_the_same_claims()
    {
        var key = await _store.StoreAsync(Ticket());

        var ticket = await _store.RetrieveAsync(key);

        ticket!.Principal.FindFirst(AuthClaims.PersonUid)!.Value.Should().Be("a0000000-0000-4000-8000-000000000001");
        ticket.Principal.FindFirst("name")!.Value.Should().Be("Una User");
    }

    [Fact]
    public async Task The_cookie_key_is_random_and_the_ticket_is_not_readable_in_storage()
    {
        var first = await _store.StoreAsync(Ticket());
        var second = await _store.StoreAsync(Ticket());

        first.Should().NotBe(second).And.HaveLength(43);
        System.Text.Encoding.UTF8.GetString(_sessions.Rows[first].ProtectedTicket).Should().NotContain("a0000000-0000-4000-8000-000000000001");
    }

    [Fact]
    public async Task Only_the_claims_the_app_reads_are_kept()
    {
        var ticket = await _store.RetrieveAsync(await _store.StoreAsync(Ticket()));

        ticket!.Principal.Claims.Select(c => c.Type).Should().BeEquivalentTo(AuthClaims.Subject, AuthClaims.PersonUid, "name", "sid", "iss");
    }

    [Fact]
    public async Task The_issuer_the_session_id_and_the_subject_are_stored_for_back_channel_logout()
    {
        var key = await _store.StoreAsync(Ticket(sid: "sid-9", subject: "subject-9"));

        _sessions.Rows[key].Should().Match<AuthSession>(s => s.Issuer == "https://idp.example" && s.Sid == "sid-9" && s.Subject == "subject-9");
    }

    [Fact]
    public async Task Storing_a_session_deletes_the_expired_ones()
    {
        var expired = await _store.StoreAsync(Ticket(expires: _time.GetUtcNow().AddMinutes(10)));
        var live = await _store.StoreAsync(Ticket(expires: _time.GetUtcNow().AddHours(8)));
        _time.Advance(TimeSpan.FromMinutes(11));

        var added = await _store.StoreAsync(Ticket());

        _sessions.Rows.Keys.Should().BeEquivalentTo([live, added]).And.NotContain(expired);
    }

    [Fact]
    public async Task Renewing_a_removed_session_does_not_bring_it_back()
    {
        var key = await _store.StoreAsync(Ticket());
        await _store.RemoveAsync(key);

        await _store.RenewAsync(key, Ticket());

        _sessions.Rows.Should().NotContainKey(key);
        (await _store.RetrieveAsync(key)).Should().BeNull();
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

        await _store.RenewAsync(key, Ticket(person: "a0000000-0000-4000-8000-000000000002"));

        (await _store.RetrieveAsync(key))!.Principal.FindFirst(AuthClaims.PersonUid)!.Value.Should().Be("a0000000-0000-4000-8000-000000000002");
    }

    private sealed class InMemorySessions : IAuthSessionStorage
    {
        public Dictionary<string, AuthSession> Rows { get; } = [];

        public Task AddAsync(AuthSession session, CancellationToken cancellationToken)
        {
            Rows[session.Key] = session;
            return Task.CompletedTask;
        }

        public Task<bool> UpdateAsync(AuthSession session, CancellationToken cancellationToken)
        {
            if (!Rows.ContainsKey(session.Key)) return Task.FromResult(false);
            Rows[session.Key] = session;
            return Task.FromResult(true);
        }

        public Task<AuthSession?> FindAsync(string key, CancellationToken cancellationToken)
        {
            return Task.FromResult(Rows.GetValueOrDefault(key));
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken)
        {
            Rows.Remove(key);
            return Task.CompletedTask;
        }

        public Task DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            foreach (var key in Rows.Values.Where(s => s.ExpiresAt < now).Select(s => s.Key).ToList()) Rows.Remove(key);
            return Task.CompletedTask;
        }

        public Task<int> DeleteByIdentityProviderSessionAsync(string issuer, string sid, CancellationToken cancellationToken)
        {
            return Task.FromResult(Rows.Values.Where(s => s.Issuer == issuer && s.Sid == sid).ToList().Count(s => Rows.Remove(s.Key)));
        }

        public Task<int> DeleteBySubjectAsync(string issuer, string subject, CancellationToken cancellationToken)
        {
            return Task.FromResult(Rows.Values.Where(s => s.Issuer == issuer && s.Subject == subject).ToList().Count(s => Rows.Remove(s.Key)));
        }
    }
}
