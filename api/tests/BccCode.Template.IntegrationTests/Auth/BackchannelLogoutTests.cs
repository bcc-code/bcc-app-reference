using BccCode.Template.Api.Auth;
using BccCode.Template.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace BccCode.Template.IntegrationTests.Auth;

/// <summary>Back-channel logout (standard 14.9) and the session rows it ends, on the real database.</summary>
public class BackchannelLogoutTests(AppFixture fixture) : AppTest(fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IAuthSessionStorage Sessions => Fixture.Host.Services.GetRequiredService<IAuthSessionStorage>();

    private static AuthSession Session(string? sid, string subject, DateTimeOffset? expires = null)
    {
        return new AuthSession(Guid.NewGuid().ToString("N"), [1, 2, 3], TestOidc.Issuer, sid, subject, expires ?? DateTimeOffset.UtcNow.AddHours(1));
    }

    private Task<IScenarioResult> PostAsync(string token, int status)
    {
        return Fixture.Host.Scenario(s =>
        {
            s.Post.FormData(new Dictionary<string, string> { ["logout_token"] = token }).ToUrl("/auth/backchannel-logout");
            s.StatusCodeShouldBe(status);
        });
    }

    [Fact]
    public async Task A_logout_token_with_a_session_id_ends_the_sessions_of_that_identity_provider_session_only()
    {
        var sid = Guid.NewGuid().ToString("N");
        var ended = Session(sid, "subject-a");
        var other = Session(Guid.NewGuid().ToString("N"), "subject-a");
        await Sessions.AddAsync(ended, Ct);
        await Sessions.AddAsync(other, Ct);

        await PostAsync(TestOidc.LogoutToken(sid, "subject-a"), 200);

        (await Sessions.FindAsync(ended.Key, Ct)).Should().BeNull();
        (await Sessions.FindAsync(other.Key, Ct)).Should().NotBeNull();
    }

    [Fact]
    public async Task A_logout_token_without_a_session_id_ends_every_session_of_the_subject()
    {
        var subject = "subject-" + Guid.NewGuid().ToString("N");
        var first = Session("sid-" + Guid.NewGuid().ToString("N"), subject);
        var second = Session(null, subject);
        var someoneElse = Session("sid-" + Guid.NewGuid().ToString("N"), "another-subject");
        foreach (var session in new[] { first, second, someoneElse }) await Sessions.AddAsync(session, Ct);

        await PostAsync(TestOidc.LogoutToken(sid: null, subject), 200);

        (await Sessions.FindAsync(first.Key, Ct)).Should().BeNull();
        (await Sessions.FindAsync(second.Key, Ct)).Should().BeNull();
        (await Sessions.FindAsync(someoneElse.Key, Ct)).Should().NotBeNull();
    }

    [Fact]
    public async Task A_bad_token_is_refused_without_saying_why_and_ends_nothing()
    {
        var session = Session("sid-keep", "subject-keep");
        await Sessions.AddAsync(session, Ct);

        var bad = await PostAsync("not-a-token", 400);
        await PostAsync(TestOidc.LogoutToken("sid-keep", "subject-keep", audience: "another-client"), 400);
        await PostAsync(TestOidc.LogoutToken("sid-keep", "subject-keep", expires: DateTime.UtcNow.AddMinutes(-10)), 400);

        (await bad.ReadAsTextAsync()).Should().Contain("invalid_request").And.NotContain("not-a-token");
        (await Sessions.FindAsync(session.Key, Ct)).Should().NotBeNull();
    }

    [Fact]
    public async Task A_post_that_is_not_a_form_is_refused()
    {
        await Fixture.Host.Scenario(s => { s.Post.Json(new { logout_token = "x" }).ToUrl("/auth/backchannel-logout"); s.StatusCodeShouldBe(400); });
    }

    [Fact]
    public async Task Updating_a_removed_session_writes_nothing_and_says_so()
    {
        var session = Session("sid-gone", "subject-gone");
        await Sessions.AddAsync(session, Ct);
        await Sessions.DeleteAsync(session.Key, Ct);

        (await Sessions.UpdateAsync(session with { ProtectedTicket = [9] }, Ct)).Should().BeFalse();

        (await Sessions.FindAsync(session.Key, Ct)).Should().BeNull("a renewal must not bring a logged-out session back");
    }

    [Fact]
    public async Task Updating_a_live_session_replaces_the_ticket_and_the_expiry_only()
    {
        var session = Session("sid-live", "subject-live");
        await Sessions.AddAsync(session, Ct);
        var later = session.ExpiresAt.AddHours(2);

        (await Sessions.UpdateAsync(session with { ProtectedTicket = [9], ExpiresAt = later }, Ct)).Should().BeTrue();

        var stored = (await Sessions.FindAsync(session.Key, Ct))!;
        stored.ProtectedTicket.Should().Equal(9);
        stored.ExpiresAt.Should().BeCloseTo(later, TimeSpan.FromMilliseconds(1), "Postgres keeps microseconds");
        (stored.Sid, stored.Subject).Should().Be(("sid-live", "subject-live"));
    }

    [Fact]
    public async Task Deleting_expired_sessions_keeps_the_live_ones()
    {
        var expired = Session("sid-old", "subject-old", DateTimeOffset.UtcNow.AddHours(-1));
        var live = Session("sid-new", "subject-new");
        await Sessions.AddAsync(expired, Ct);
        await Sessions.AddAsync(live, Ct);

        await Sessions.DeleteExpiredAsync(DateTimeOffset.UtcNow, Ct);

        (await Sessions.FindAsync(expired.Key, Ct)).Should().BeNull();
        (await Sessions.FindAsync(live.Key, Ct)).Should().NotBeNull();
    }
}
